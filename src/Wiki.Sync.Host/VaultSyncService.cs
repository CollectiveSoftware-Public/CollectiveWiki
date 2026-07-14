// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using Collective.Platform.Secrets;
using Wiki.Core.Vault;
using Wiki.Sync;
using Wiki.Sync.Transport;

namespace Wiki.Sync.Host;

/// <summary>The UI-free orchestrator the desktop head (Plan F2) drives: composes the device identity, the
/// authorized-peers roster, the content-key ring, the vault replica + bridge, and the Plan D/E transport, and
/// persists every durable artifact under `.cwiki/sync/`. Owner operations (share / add collaborator / accept /
/// revoke) go through a rehydrated <see cref="PairingCoordinator"/>; sync (serve + pull) goes through the
/// transport. Pairing artifacts (invite → join request → signed roster) are exchanged by the caller in v1 — an
/// over-the-wire pairing handshake is a follow-up.</summary>
public sealed class VaultSyncService
{
    private readonly DeviceIdentity _identity;
    private readonly VaultReplica _replica;
    private readonly VaultReplicaBridge _bridge;
    private readonly AuthenticatingReconciler _reconciler;
    private readonly ContentKeySealer _contentSealer;
    private readonly ChangeSigner _signer;
    private readonly AuthorizedPeersStore _peersStore;
    private readonly KeyRingStore _keyRingStore;
    private readonly ReplicaStateStore _stateStore;
    private readonly ISyncStore _syncStore;

    private PairingCoordinator? _coordinator;   // owner only
    private VaultKeyRing? _keyRing;             // owner only
    private AuthorizedPeersList? _peers;        // current signed roster (owner: from coordinator; joiner: received)
    private string? _pinnedOwnerId;

    private sealed record OwnerConfig(string VaultId, string Name, string Email);

    public VaultSyncService(
        DeviceIdentity identity, VaultReplica replica, VaultReplicaBridge bridge,
        AuthenticatingReconciler reconciler, ContentKeySealer contentSealer,
        AuthorizedPeersStore peersStore, KeyRingStore keyRingStore, ReplicaStateStore stateStore, ISyncStore syncStore)
    {
        _identity = identity;
        _replica = replica;
        _bridge = bridge;
        _reconciler = reconciler;
        _contentSealer = contentSealer;
        _signer = new ChangeSigner(identity);
        _peersStore = peersStore;
        _keyRingStore = keyRingStore;
        _stateStore = stateStore;
        _syncStore = syncStore;
    }

    public string DeviceId => _identity.DeviceId;
    public DeviceIdentity Identity => _identity;
    public VaultReplica Replica => _replica;
    public AuthorizedPeersList? Peers => _peers;
    public string? OwnerDeviceId => _pinnedOwnerId;
    public bool IsOwner => _coordinator is not null;

    /// <summary>Rehydrate persisted state (replica, roster, key ring, and — if we are the owner — the pairing
    /// coordinator seeded from the saved roster). Call once after construction (the factory does this).</summary>
    public void LoadPersistedState()
    {
        _stateStore.Load(_replica);
        var peers = _peersStore.Load();
        if (peers is not null) { _peers = peers; _pinnedOwnerId = peers.OwnerDeviceId; }
        _keyRing = _keyRingStore.Load(_contentSealer);

        var owner = LoadOwnerConfig();
        if (owner is not null && peers is not null && peers.OwnerDeviceId == _identity.DeviceId)
            _coordinator = new PairingCoordinator(_identity, Guid.Parse(owner.VaultId), owner.Name, owner.Email, peers.Peers);
    }

    // ---- owner ----------------------------------------------------------

    /// <summary>Become the owner of this (open) vault: seed the replica from disk, start a key ring + an
    /// owner roster, and persist everything. Re-sharing keeps the existing vault id + roster.</summary>
    public void ShareVault(string ownerName, string ownerEmail)
    {
        var existing = LoadOwnerConfig();
        var vaultId = existing is not null ? Guid.Parse(existing.VaultId) : Guid.NewGuid();
        var priorRoster = _peers is not null && _peers.OwnerDeviceId == _identity.DeviceId ? _peers.Peers : null;

        _coordinator = new PairingCoordinator(_identity, vaultId, ownerName, ownerEmail, priorRoster);
        _keyRing ??= VaultKeyRing.Start(_contentSealer);
        _peers = _coordinator.CurrentList();
        _pinnedOwnerId = _identity.DeviceId;
        _bridge.SeedFromVault(_replica);

        SaveOwnerConfig(new OwnerConfig(vaultId.ToString(), ownerName, ownerEmail));
        _peersStore.Save(_peers);
        _keyRingStore.Save(_keyRing);
        _stateStore.Save(_replica);
    }

    /// <summary>Mint a role-tagged, single-use invite code for a new collaborator.</summary>
    public string AddCollaborator(PeerRole role, DateTimeOffset expiresAt, string discoveryHint = "")
    {
        RequireOwner();
        return InviteCodec.Encode(_coordinator!.Issue(role, expiresAt, discoveryHint));
    }

    /// <summary>Validate a joiner's request against a pending invite; on success record the collaborator into
    /// the owner-signed roster and persist it. Returns the pairing verdict.</summary>
    public PairingOutcome AcceptJoin(JoinRequest request, DateTimeOffset now)
    {
        RequireOwner();
        var result = _coordinator!.Accept(request, now);
        if (result.Outcome == PairingOutcome.Accepted)
        {
            _peers = result.UpdatedList!;
            _peersStore.Save(_peers);
        }
        return result.Outcome;
    }

    /// <summary>Revoke a device: drop it from the roster, rotate the content key (post-revocation secrecy),
    /// and persist both.</summary>
    public void Revoke(string deviceId)
    {
        RequireOwner();
        _peers = _coordinator!.Revoke(deviceId);
        _keyRing?.Rotate();
        _peersStore.Save(_peers);
        if (_keyRing is not null) _keyRingStore.Save(_keyRing);
    }

    // ---- joiner ---------------------------------------------------------

    /// <summary>Joiner: parse an invite code and build a signed join request; pins the invite's owner id.
    /// Returns null if the code is malformed.</summary>
    public JoinRequest? CreateJoinRequest(string inviteCode, string name, string email)
    {
        if (!InviteCodec.TryParse(inviteCode, out var invite) || invite is null) return null;
        _pinnedOwnerId = invite.OwnerDeviceId;
        return JoinRequestFactory.Create(_identity, invite, name, email);
    }

    /// <summary>Joiner: adopt the owner-signed roster received back after pairing (delivered by the caller in
    /// v1). Verifies against the pinned owner, seeds the replica from the local folder, and persists.</summary>
    public bool AdoptRoster(AuthorizedPeersList list)
    {
        if (_pinnedOwnerId is null || !list.Verify(_pinnedOwnerId)) return false;
        _peers = list;
        _bridge.SeedFromVault(_replica);
        _peersStore.Save(_peers);
        _stateStore.Save(_replica);
        return true;
    }

    // ---- sync -----------------------------------------------------------

    /// <summary>Bring the replica in line with the current on-disk notes (edits made in the app or while it was
    /// closed) and persist. The head calls this before serving/pulling so local changes propagate. Returns the
    /// changed paths.</summary>
    public IReadOnlyList<string> RefreshFromVault()
    {
        var changed = _bridge.RefreshFromVault(_replica);
        if (changed.Count > 0) _stateStore.Save(_replica);
        return changed;
    }

    /// <summary>A <see cref="SyncServer"/> over the live replica (for a SyncListener/RelaySyncListener).</summary>
    public SyncServer BuildSyncServer() => new(new ReplicaContentProvider(_replica, _signer));

    /// <summary>Whether a connecting device is on the roster (the acceptPeer gate for listeners).</summary>
    public bool AcceptPeer(string deviceId) => _peers?.Find(deviceId) is not null;

    /// <summary>Pull a peer's changes over an authenticated connection, reconcile, flush the results to the
    /// vault, and persist the new replica state.</summary>
    public async Task<AuthenticatedReport> PullFromAsync(TlsPeerConnection conn, DateTimeOffset now, CancellationToken ct = default)
    {
        if (_peers is null || _pinnedOwnerId is null) throw new InvalidOperationException("no roster — share or join first");
        var report = await new SyncClient(_reconciler)
            .PullAsync(conn.Stream, conn.RemoteDeviceId, _replica, _peers, _pinnedOwnerId, now, ct);
        _bridge.FlushToVault(_replica);
        _stateStore.Save(_replica);
        return report;
    }

    // ---- pairing over the wire ------------------------------------------

    /// <summary>Owner side of the pairing handshake over an authenticated connection: read the joiner's
    /// request, bind it to the TLS-verified caller, accept it against a pending invite (recording + persisting
    /// the roster), and reply with the signed roster + the content key sealed for the new device. On any
    /// rejection, reply with the outcome. Returns the outcome.</summary>
    public async Task<PairingOutcome> ServePairingAsync(TlsPeerConnection conn, DateTimeOffset now, CancellationToken ct = default)
    {
        RequireOwner();
        var (type, payload) = await PairingWire.ReadFrameAsync(conn.Stream, ct);
        if (type != PairingWire.MessageType.JoinRequest)
            throw new InvalidDataException($"expected a JoinRequest frame, got {type}");
        var request = PairingWire.DecodeJoinRequest(payload);

        // The device that authenticated the TLS session must be the one asking to join.
        if (conn.RemoteDeviceId != request.Applicant.DeviceId)
        {
            await PairingWire.WriteFrameAsync(conn.Stream, PairingWire.MessageType.Rejected,
                PairingWire.EncodeRejected(PairingOutcome.IdentityMismatch), ct);
            return PairingOutcome.IdentityMismatch;
        }

        var outcome = AcceptJoin(request, now);   // validates the token, records + persists the roster on success
        if (outcome != PairingOutcome.Accepted)
        {
            await PairingWire.WriteFrameAsync(conn.Stream, PairingWire.MessageType.Rejected,
                PairingWire.EncodeRejected(outcome), ct);
            return outcome;
        }

        var newPeer = _peers!.Find(request.Applicant.DeviceId)!;
        var sealedKey = _contentSealer.Seal(_identity, newPeer, _keyRing!.Current);
        await PairingWire.WriteFrameAsync(conn.Stream, PairingWire.MessageType.Accepted,
            PairingWire.EncodeAccepted(_peers!, sealedKey), ct);
        return PairingOutcome.Accepted;
    }

    /// <summary>Joiner side: send our signed join request over an authenticated connection and apply the
    /// reply — on Accepted, adopt + persist the owner-signed roster and unseal + persist the content key; on
    /// Rejected, return the owner's outcome. Returns WrongVault if the invite code is malformed.</summary>
    public async Task<PairingOutcome> RequestPairingAsync(TlsPeerConnection conn, string inviteCode, string name, string email, CancellationToken ct = default)
    {
        var request = CreateJoinRequest(inviteCode, name, email);   // parses the invite + pins the owner id
        if (request is null) return PairingOutcome.WrongVault;

        await PairingWire.WriteFrameAsync(conn.Stream, PairingWire.MessageType.JoinRequest,
            PairingWire.EncodeJoinRequest(request), ct);

        var (type, payload) = await PairingWire.ReadFrameAsync(conn.Stream, ct);
        if (type == PairingWire.MessageType.Rejected) return PairingWire.DecodeRejected(payload);
        if (type != PairingWire.MessageType.Accepted) throw new InvalidDataException($"unexpected reply frame {type}");

        var (roster, sealedKey) = PairingWire.DecodeAccepted(payload);
        if (!AdoptRoster(roster)) return PairingOutcome.InvalidSignature;   // roster failed to verify against the pinned owner

        var ownerPeer = roster.Find(roster.OwnerDeviceId)!;
        var key = _contentSealer.Unseal(_identity, ownerPeer.PublicKey, ownerPeer.DeviceId, sealedKey);
        if (key is not null)
        {
            _keyRing = new VaultKeyRing(key, _contentSealer);
            _keyRingStore.Save(_keyRing);
        }
        return PairingOutcome.Accepted;
    }

    // ---- helpers --------------------------------------------------------

    private void RequireOwner()
    {
        if (_coordinator is null) throw new InvalidOperationException("not the owner of a shared vault");
    }

    private void SaveOwnerConfig(OwnerConfig c) => _syncStore.WriteBytes("owner.json", JsonSerializer.SerializeToUtf8Bytes(c));

    private OwnerConfig? LoadOwnerConfig()
    {
        var b = _syncStore.ReadBytes("owner.json");
        return b is null ? null : JsonSerializer.Deserialize<OwnerConfig>(b);
    }
}

/// <summary>Composition root: load-or-create the at-rest device identity, wire the `.cwiki/sync/` stores,
/// rehydrate persisted state, and return a ready <see cref="VaultSyncService"/> for an open vault. `secrets`
/// is the OS keystore (DpapiSecretStore on Windows); `contentSealer` + `reconciler` come from AddWikiSync.</summary>
public static class VaultSyncHost
{
    public static VaultSyncService Open(IVaultFileSystem vault, ISyncStore syncStore, ISecretStore secrets,
        ContentKeySealer contentSealer, AuthenticatingReconciler reconciler)
    {
        var sealer = new AtRestSealer(secrets);
        var identity = DeviceIdentityProvider.LoadOrCreate(new EncryptedFileIdentityStore(syncStore, sealer));
        var replica = new VaultReplica(identity.DeviceId);
        var service = new VaultSyncService(identity, replica, new VaultReplicaBridge(vault),
            reconciler, contentSealer,
            new AuthorizedPeersStore(syncStore), new KeyRingStore(syncStore, sealer), new ReplicaStateStore(syncStore), syncStore);
        service.LoadPersistedState();
        return service;
    }
}
