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
    private readonly Dictionary<string, IPEndPoint> _peerSyncEndpoints = new();
    private readonly Dictionary<string, CollaboratorRow> _rows = new();

    private SyncListener? _syncListener;
    private PairingListener? _pairingListener;
    private CancellationTokenSource? _autoPull;

    public SyncViewModel(VaultSyncService service, Func<DateTimeOffset>? clock = null, Action<Action>? dispatch = null)
    {
        _service = service;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _dispatch = dispatch ?? (a => a());
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

    /// <summary>Bind the serve + accept-pairing listeners and start the auto-pull loop. Ports default to 0
    /// (OS-assigned) for tests; the head passes the configured ports. Returns the bound sync endpoint.</summary>
    public IPEndPoint StartServing(IPAddress bind, int pairingPort = 0, int syncPort = 0, TimeSpan? autoPullEvery = null)
    {
        if (IsSharing) return SyncEndpoint!;
        _syncListener = new SyncListener(_service.Identity, _service.BuildSyncServer(), _service.AcceptPeer);
        SyncEndpoint = _syncListener.Start(new IPEndPoint(bind, syncPort));
        _pairingListener = new PairingListener(_service.Identity, ServePairingAsync);
        PairingEndpoint = _pairingListener.Start(new IPEndPoint(bind, pairingPort));
        IsSharing = true;
        Status = SyncStatus.Idle;
        StartAutoPull(autoPullEvery ?? TimeSpan.FromSeconds(30));
        return SyncEndpoint;
    }

    public void StopServing()
    {
        _autoPull?.Cancel(); _autoPull?.Dispose(); _autoPull = null;
        _syncListener?.Dispose(); _syncListener = null;
        _pairingListener?.Dispose(); _pairingListener = null;
        SyncEndpoint = null; PairingEndpoint = null;
        IsSharing = false;
    }

    /// <summary>Mint a role-tagged invite embedding our LAN endpoint as the discovery hint. Must be serving.</summary>
    public string AddCollaborator(PeerRole role, TimeSpan validFor)
    {
        if (PairingEndpoint is null || SyncEndpoint is null)
            throw new InvalidOperationException("start serving before adding a collaborator");
        var hint = SyncDiscoveryHint.Format(LanEndpoints.LocalIPv4(), PairingEndpoint.Port, SyncEndpoint.Port);
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

    /// <summary>Paste-to-join: dial the owner (host from the invite, or <paramref name="hostOverride"/>), pair
    /// over the wire, then do the initial sync. Returns the pairing outcome.</summary>
    public async Task<PairingOutcome> JoinAsync(string invite, string name, string email, string? hostOverride = null)
    {
        if (!InviteCodec.TryParse(invite, out var payload) || payload is null) return PairingOutcome.WrongVault;
        if (!SyncDiscoveryHint.TryParse(payload.DiscoveryHint, out var host, out var pairPort, out var syncPort))
            return PairingOutcome.WrongVault;
        var dialHost = string.IsNullOrWhiteSpace(hostOverride) ? host : hostOverride!;
        if (!IPAddress.TryParse(dialHost, out var addr)) return PairingOutcome.WrongVault;

        PairingOutcome outcome;
        using (var conn = await PeerConnector.ConnectAsync(_service.Identity, payload.OwnerDeviceId, new IPEndPoint(addr, pairPort), relay: null))
            outcome = await _service.RequestPairingAsync(conn, invite, name, email);

        if (outcome == PairingOutcome.Accepted)
        {
            RememberPeer(payload.OwnerDeviceId, new IPEndPoint(addr, syncPort));
            RefreshCollaborators();
            await SyncNowAsync();   // initial sync
        }
        return outcome;
    }

    // ---- sync -----------------------------------------------------------

    public void RememberPeer(string deviceId, IPEndPoint syncEndpoint) => _peerSyncEndpoints[deviceId] = syncEndpoint;

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
        foreach (var (deviceId, ep) in _peerSyncEndpoints.ToArray())
        {
            try
            {
                using var conn = await PeerConnector.ConnectAsync(_service.Identity, deviceId, ep, relay: null);
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
