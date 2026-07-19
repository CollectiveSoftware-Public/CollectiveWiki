// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using Wiki.Core.Sync;
using Wiki.Sync;
using Wiki.Sync.Host;
using Wiki.Sync.Transport;

namespace Wiki.Desktop.Sync;

/// <summary>Sync orchestration + presentation for one open vault. Owns a <see cref="VaultSyncService"/> and,
/// while sharing, the LAN listeners (serve + accept-pairing) and an auto-pull loop. Follows the head's MVVM:
/// plain methods invoked from Click handlers + [ObservableProperty] state, no ICommand. Socket work is awaited
/// off the UI thread; observable state is mutated after the await (resuming on the captured context).
/// Callbacks that arrive on background threads — the auto-pull tick and the pairing accept — marshal through
/// <c>dispatch</c> (the head passes Dispatcher.UIThread.Post) so bound state never mutates cross-thread.</summary>
public partial class SyncViewModel : ObservableObject, IDisposable
{
    private readonly VaultSyncService _service;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<Action> _dispatch;
    private readonly PublicEndpointProvider _endpoints;
    private readonly IFirewallOpener _firewall;
    private readonly Dictionary<string, IReadOnlyList<IPEndPoint>> _peerSyncEndpoints = new();
    private readonly Dictionary<string, CollaboratorRow> _rows = new();

    private IReadOnlyList<SyncCandidate> _localCandidates = Array.Empty<SyncCandidate>();
    private Task? _reachabilityWork;
    private SyncListener? _syncListener;
    private PairingListener? _pairingListener;
    private CancellationTokenSource? _autoPull;

    public SyncViewModel(VaultSyncService service, Func<DateTimeOffset>? clock = null, Action<Action>? dispatch = null,
        PublicEndpointProvider? endpoints = null, IFirewallOpener? firewall = null)
    {
        _service = service;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _dispatch = dispatch ?? (a => a());
        _endpoints = endpoints ?? new PublicEndpointProvider();
        _firewall = firewall ?? FirewallOpeners.CreateDefault();
        RefreshCollaborators();
    }

    [ObservableProperty] private SyncStatus _status = SyncStatus.Idle;
    [ObservableProperty] private bool _isSharing;
    [ObservableProperty] private string? _lastInvite;
    [ObservableProperty] private DateTimeOffset? _lastSyncedAt;

    public ObservableCollection<CollaboratorRow> Collaborators { get; } = new();

    /// <summary>How many collaborators were reachable on the last sync (for the status summary).</summary>
    public int OnlinePeers => Collaborators.Count(c => c.Online);

    public string DeviceId => _service.DeviceId;
    public bool IsOwner => _service.IsOwner;
    public bool HasRoster => _service.Peers is not null;
    public IPEndPoint? SyncEndpoint { get; private set; }
    public IPEndPoint? PairingEndpoint { get; private set; }

    // ---- owner ----------------------------------------------------------

    /// <summary>Become the owner. Seeding the replica reads every note in the vault, so the service call
    /// runs off the calling (UI) thread; observable state updates after the await, back on it.</summary>
    public async Task ShareVaultAsync(string name, string email)
    {
        await Task.Run(() => _service.ShareVault(name, email));
        RefreshCollaborators();
    }

    /// <summary>Bind the serve + accept-pairing listeners and start the auto-pull loop. The bind family is gated
    /// on <paramref name="internetEnabled"/>: when off we bind IPv4 (<see cref="IPAddress.Any"/>) only — the
    /// pre-branch behavior, so an opt-out never leaves an internet-facing port on the host's global IPv6; when on
    /// we bind dual-stack (<see cref="IPAddress.IPv6Any"/>) so both IPv4-mapped and IPv6 dialers can reach us,
    /// falling back to IPv4 on a host with no IPv6 stack. Ports default to 0 (OS-assigned) for tests; the head
    /// passes the configured ports. The fast local candidates (LAN + global IPv6) are gathered synchronously so
    /// the first <see cref="AddCollaborator"/> invite carries them; only when <paramref name="internetEnabled"/>
    /// is the slow UPnP/NAT-PMP mapping gathered in the background and appended once it lands. Returns the bound
    /// sync endpoint.</summary>
    public IPEndPoint StartServing(int pairingPort = 0, int syncPort = 0, bool internetEnabled = false, TimeSpan? autoPullEvery = null)
    {
        if (IsSharing) return SyncEndpoint!;
        _syncListener = new SyncListener(_service.Identity, _service.BuildSyncServer(), _service.AcceptPeer);
        SyncEndpoint = BindListener(_syncListener.Start, syncPort, internetEnabled);
        _pairingListener = new PairingListener(_service.Identity, ServePairingAsync);
        PairingEndpoint = BindListener(_pairingListener.Start, pairingPort, internetEnabled);
        IsSharing = true;
        Status = SyncStatus.Idle;

        // The first invite must carry the fast, local candidates (LAN IPv4 + global IPv6) — both are instant, no
        // network wait — so gather them synchronously here, before AddCollaborator can be called.
        _localCandidates = _endpoints.GatherLocal(PairingEndpoint.Port, SyncEndpoint.Port, internetEnabled);

        // Only the slow reachability work runs off-thread, and only when internet sync is on: open the host
        // firewall (so the advertised global-IPv6 endpoint is actually reachable — enumerating an address does
        // nothing if the OS drops the inbound SYN) and gather the UPnP/NAT-PMP public-IPv4 candidate. When the
        // gather lands we swap in the full candidate set (LAN + IPv6 + mapped public IPv4). Off = neither runs.
        // Ports are captured up front so a concurrent StopServing (which nulls the endpoints) can't NRE us.
        if (internetEnabled)
        {
            var pairPort = PairingEndpoint.Port;
            var syncP = SyncEndpoint.Port;
            _reachabilityWork = Task.Run(async () =>
            {
                var admit = _firewall.EnsureInboundAllowedAsync(pairPort, syncP, CancellationToken.None);
                var cands = await _endpoints.GatherAsync(pairPort, syncP, internetEnabled, CancellationToken.None);
                _dispatch(() => _localCandidates = cands);
                try { await admit; } catch { /* firewall admission is best-effort — never surface it as a sync crash */ }
            });
        }

        StartAutoPull(autoPullEvery ?? TimeSpan.FromSeconds(30));
        return SyncEndpoint;
    }

    /// <summary>Bind one listener with the family gated on the internet-sync opt-in. OFF binds IPv4
    /// (<see cref="IPAddress.Any"/>) only — no internet-facing port. ON binds dual-stack
    /// (<see cref="IPAddress.IPv6Any"/>), falling back to IPv4 when the host has no IPv6 stack (the dual-stack
    /// bind throws <see cref="System.Net.Sockets.SocketException"/>).</summary>
    private static IPEndPoint BindListener(Func<IPEndPoint, IPEndPoint> start, int port, bool internetEnabled)
    {
        if (internetEnabled)
        {
            try { return start(new IPEndPoint(IPAddress.IPv6Any, port)); }
            catch (System.Net.Sockets.SocketException) { /* no IPv6 stack — fall back to IPv4 */ }
        }
        return start(new IPEndPoint(IPAddress.Any, port));
    }

    public void StopServing()
    {
        _autoPull?.Cancel(); _autoPull?.Dispose(); _autoPull = null;
        _syncListener?.Dispose(); _syncListener = null;
        _pairingListener?.Dispose(); _pairingListener = null;
        SyncEndpoint = null; PairingEndpoint = null;
        IsSharing = false;
    }

    /// <summary>Release the host firewall admission that <see cref="StartServing"/> opened for internet sync.
    /// The head calls this only when the user turns internet sync off, so the port stops being admitted instead
    /// of being left open. Best-effort (may raise a one-time elevation prompt); safe when nothing was opened.</summary>
    public Task RemoveInternetReachabilityAsync() => _firewall.RemoveAsync(CancellationToken.None);

    /// <summary>Mint a role-tagged invite whose discovery hint carries our full advertisable candidate set
    /// (LAN always; global IPv6 + public IPv4 when internet sync is on). Falls back to a fresh LAN-only
    /// candidate if the background gather has not landed yet. Must be serving.</summary>
    public string AddCollaborator(PeerRole role, TimeSpan validFor)
    {
        if (PairingEndpoint is null || SyncEndpoint is null)
            throw new InvalidOperationException("start serving before adding a collaborator");
        var candidates = _localCandidates.Count > 0
            ? _localCandidates
            : new[] { new SyncCandidate(LanEndpoints.LocalIPv4(), PairingEndpoint.Port, SyncEndpoint.Port) };
        var hint = SyncDiscoveryHint.FormatMany(candidates);
        LastInvite = _service.AddCollaborator(role, _clock().Add(validFor), hint);
        return LastInvite;
    }

    public void Revoke(string deviceId)
    {
        _service.Revoke(deviceId);
        _peerSyncEndpoints.Remove(deviceId);
        _rows.Remove(deviceId);
        RefreshCollaborators();
    }

    private async Task ServePairingAsync(TlsPeerConnection conn, CancellationToken ct)
    {
        var outcome = await _service.ServePairingAsync(conn, _clock(), ct).ConfigureAwait(false);
        // Read the connection before dispatching — the listener disposes it once this handler returns.
        var deviceId = conn.RemoteDeviceId;
        var remote = conn.RemoteEndPoint;
        // This runs on the accept-loop thread; marshal the state + bound-collection updates to the UI.
        _dispatch(() =>
        {
            // Learn the new collaborator's return address (assume they serve on the same default sync port).
            if (outcome == PairingOutcome.Accepted && SyncEndpoint is not null && remote is { } r)
                RememberPeer(deviceId, new IPEndPoint(r.Address, SyncEndpoint.Port));
            RefreshCollaborators();
        });
    }

    // ---- joiner ---------------------------------------------------------

    /// <summary>Paste-to-join: dial the owner over the invite's candidate ladder (LAN → global IPv6 → public
    /// IPv4, in advertised order; a <paramref name="hostOverride"/> is tried first), pair over the wire, then do
    /// the initial sync. No relay tier in v1. Maps a dial failure to <see cref="PairingOutcome.NoRoute"/> when
    /// the owner advertised nothing globally reachable (LAN-only), or <see cref="PairingOutcome.OwnerUnreachable"/>
    /// when it advertised a global candidate but none connected; a failure AFTER the connection was established
    /// is <see cref="PairingOutcome.ExchangeFailed"/> — the owner WAS reached, so it must never masquerade as
    /// unreachability. Returns the pairing outcome.</summary>
    public async Task<PairingOutcome> JoinAsync(string invite, string name, string email, string? hostOverride = null)
    {
        if (!InviteCodec.TryParse(invite, out var payload) || payload is null) return PairingOutcome.WrongVault;
        if (!SyncDiscoveryHint.TryParseMany(payload.DiscoveryHint, out var hintCands)) return PairingOutcome.WrongVault;

        // Build the pairing + sync ladders (in advertised order). A host override, when supplied, is tried first
        // on the first candidate's ports — the manual escape hatch for a LAN peer whose advertised address we
        // cannot reach (so it must cover the initial sync too, not just pairing).
        var pairing = new List<IPEndPoint>();
        var sync = new List<IPEndPoint>();
        var anyGlobal = false;
        if (!string.IsNullOrWhiteSpace(hostOverride) && IPAddress.TryParse(hostOverride, out var ov))
        {
            pairing.Add(new IPEndPoint(ov, hintCands[0].PairingPort));
            sync.Add(new IPEndPoint(ov, hintCands[0].SyncPort));
        }
        foreach (var c in hintCands)
        {
            if (!IPAddress.TryParse(c.Host, out var addr)) continue;   // hints carry literal IPs
            anyGlobal |= AddressScope.IsGlobal(addr);
            pairing.Add(new IPEndPoint(addr, c.PairingPort));
            sync.Add(new IPEndPoint(addr, c.SyncPort));
        }
        if (pairing.Count == 0) return PairingOutcome.NoRoute;

        // The unreachability catches cover ONLY the dial — once a connection exists, "reached but broke" must
        // not be reported as "couldn't reach" (the honesty these outcomes exist for).
        TlsPeerConnection conn;
        try { conn = await PeerConnector.ConnectAsync(_service.Identity, payload.OwnerDeviceId, pairing); }
        catch (InvalidOperationException) { return PairingOutcome.NoRoute; }   // nothing dialable to try
        catch { return anyGlobal ? PairingOutcome.OwnerUnreachable : PairingOutcome.NoRoute; }

        PairingOutcome outcome;
        try { using (conn) outcome = await _service.RequestPairingAsync(conn, invite, name, email); }
        catch { return PairingOutcome.ExchangeFailed; }   // established, then the exchange died mid-flight

        if (outcome == PairingOutcome.Accepted)
        {
            RememberPeer(payload.OwnerDeviceId, sync);   // remember the owner's full sync ladder
            RefreshCollaborators();
            await SyncNowAsync();   // initial sync
        }
        return outcome;
    }

    // ---- sync -----------------------------------------------------------

    public void RememberPeer(string deviceId, IReadOnlyList<IPEndPoint> endpoints) => _peerSyncEndpoints[deviceId] = endpoints;

    /// <summary>Single-endpoint convenience for callers (and tests) that know exactly one return address.</summary>
    public void RememberPeer(string deviceId, IPEndPoint endpoint) => RememberPeer(deviceId, new[] { endpoint });

    /// <summary>Reflect local/offline edits into the replica so peers pulling us see them (also keeps our
    /// served content fresh). Call after a note save. Runs off the UI thread.</summary>
    public Task NotifyLocalChangeAsync() => Task.Run(() => _service.RefreshFromVault());

    /// <summary>Refresh our replica from disk, then pull every peer whose endpoint we know. Updates status +
    /// per-collaborator presence.</summary>
    public async Task SyncNowAsync()
    {
        if (!HasRoster) return;
        await Task.Run(() => _service.RefreshFromVault());
        if (_peerSyncEndpoints.Count == 0) { Status = SyncStatus.Idle; return; }

        Status = SyncStatus.Syncing;
        var reachable = false;
        foreach (var (deviceId, endpoints) in _peerSyncEndpoints.ToArray())
        {
            try
            {
                using var conn = await PeerConnector.ConnectAsync(_service.Identity, deviceId, endpoints);
                await _service.PullFromAsync(conn, _clock());
                reachable = true;
                MarkPeer(deviceId, online: true);
            }
            catch { MarkPeer(deviceId, online: false); }
        }
        if (reachable) LastSyncedAt = _clock();
        Status = reachable ? SyncStatus.Idle : SyncStatus.Offline;
        RefreshCollaborators();
    }

    private void StartAutoPull(TimeSpan every)
    {
        _autoPull = new CancellationTokenSource();
        var ct = _autoPull.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                using var timer = new PeriodicTimer(every);
                while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                {
                    // Start the round on the UI context: SyncNowAsync mutates observable state that the
                    // Share dialog binds to, and Avalonia rejects those writes from a pool thread (which
                    // silently killed every auto-pull round). The socket work inside is still awaited.
                    _dispatch(() => _ = SyncNowAsync());
                }
            }
            catch (OperationCanceledException) { /* shutting down */ }
        }, ct);
    }

    // ---- collaborator projection ----------------------------------------

    private void MarkPeer(string deviceId, bool online)
    {
        if (_rows.TryGetValue(deviceId, out var row))
        {
            row.Online = online;
            if (online) row.LastSynced = _clock();
        }
    }

    private void RefreshCollaborators()
    {
        Collaborators.Clear();
        var peers = _service.Peers;
        if (peers is null) return;
        foreach (var p in peers.Peers)
        {
            if (p.DeviceId == _service.DeviceId) continue;   // don't list ourselves
            if (!_rows.TryGetValue(p.DeviceId, out var row))
            {
                row = new CollaboratorRow { DeviceId = p.DeviceId, Name = p.Name, Email = p.Email, Role = p.Role };
                _rows[p.DeviceId] = row;
            }
            Collaborators.Add(row);
        }
    }

    public void Dispose() => StopServing();
}
