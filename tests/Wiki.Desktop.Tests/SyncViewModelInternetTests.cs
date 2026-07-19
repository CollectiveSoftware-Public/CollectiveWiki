// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net;
using Wiki.Desktop.Sync;
using Wiki.Sync;
using Wiki.Sync.Transport;
using Xunit;

namespace Wiki.Desktop.Tests;

/// <summary>End-to-end head coverage for Task 8: a join succeeds when the invite carries a reachable candidate
/// and the head dials only the candidate ladder (no relay). Mirrors <see cref="SyncViewModelSyncGateTests"/>'s
/// temp-vault harness but drives the reworked dual-stack <c>StartServing</c> + candidate-ladder <c>JoinAsync</c>.
/// Owner invites advertise a loopback candidate (via an injected <see cref="PublicEndpointProvider"/>) so the
/// advertised address is deterministically reachable through the IPv6Any dual-stack listener.</summary>
public class SyncViewModelInternetTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
    private readonly List<string> _dirs = new();

    private string NewVault(params (string name, string body)[] notes)
    {
        var dir = Path.Combine(Path.GetTempPath(), "cwiki-net-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var (n, b) in notes) File.WriteAllText(Path.Combine(dir, n), b);
        _dirs.Add(dir);
        return dir;
    }
    public void Dispose() { foreach (var d in _dirs) { try { Directory.Delete(d, true); } catch { } } }

    // A provider that advertises a single loopback candidate: the real LocalIPv4 may be firewalled/unreachable,
    // but 127.0.0.1 always reaches our own IPv6Any dual-stack listener (via an IPv4-mapped connect).
    private static PublicEndpointProvider LoopbackEndpoints() =>
        new(new NoopPortMapper(), () => Array.Empty<IPAddress>(), () => IPAddress.Loopback.ToString());

    private sealed class NoopPortMapper : IPortMapper
    {
        public Task<PortMapping?> TryMapAsync(int internalPort, TimeSpan timeout, CancellationToken ct)
            => Task.FromResult<PortMapping?>(null);
        public Task TryUnmapAsync(int internalPort, int externalPort, TimeSpan timeout, CancellationToken ct)
            => Task.CompletedTask;
    }

    // Grants every internal port a same-number mapping on a public IP and counts the unmaps back.
    private sealed class GrantingMapper : IPortMapper
    {
        public int UnmapCalls;
        public Task<PortMapping?> TryMapAsync(int internalPort, TimeSpan timeout, CancellationToken ct)
            => Task.FromResult<PortMapping?>(new PortMapping("203.0.113.9", internalPort));
        public Task TryUnmapAsync(int internalPort, int externalPort, TimeSpan timeout, CancellationToken ct)
        {
            Interlocked.Increment(ref UnmapCalls);
            return Task.CompletedTask;
        }
    }

    // A mapper whose map result is released by the test — holds the background gather open so the test controls
    // exactly when the (by then stale) candidate set tries to land.
    private sealed class GatedMapper : IPortMapper
    {
        public readonly TaskCompletionSource<PortMapping?> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<PortMapping?> TryMapAsync(int internalPort, TimeSpan timeout, CancellationToken ct) => Result.Task;
        public Task TryUnmapAsync(int internalPort, int externalPort, TimeSpan timeout, CancellationToken ct)
            => Task.CompletedTask;
    }

    // Records how the head drove the firewall opener without touching the real OS firewall (the production
    // NetshFirewallOpener would shell out to an elevated netsh and prompt for UAC).
    private sealed class RecordingFirewall : IFirewallOpener
    {
        public int EnsureCalls;
        public int RemoveCalls;
        public int LastPairingPort;
        public int LastSyncPort;
        public readonly TaskCompletionSource Ensured = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> EnsureInboundAllowedAsync(int pairingPort, int syncPort, CancellationToken ct)
        {
            Interlocked.Increment(ref EnsureCalls);
            LastPairingPort = pairingPort;
            LastSyncPort = syncPort;
            Ensured.TrySetResult();
            return Task.FromResult(true);
        }

        public Task<bool> RemoveAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref RemoveCalls);
            return Task.FromResult(true);
        }
    }

    // A firewall whose ADD blocks until the test releases it — models the elevated netsh (or its UAC prompt)
    // still being in flight when the user toggles internet sync back off.
    private sealed class SlowEnsureFirewall : IFirewallOpener
    {
        public readonly TaskCompletionSource ReleaseAdd = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly List<string> Calls = new();

        public async Task<bool> EnsureInboundAllowedAsync(int pairingPort, int syncPort, CancellationToken ct)
        {
            await ReleaseAdd.Task;
            lock (Calls) Calls.Add("add");
            return true;
        }

        public Task<bool> RemoveAsync(CancellationToken ct)
        {
            lock (Calls) Calls.Add("remove");
            return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task Enabling_internet_sync_opens_the_host_firewall_for_the_bound_ports()
    {
        var dir = NewVault();
        var fw = new RecordingFirewall();
        using var vm = new SyncViewModel(
            WikiSyncHostFactory.ForVault(dir, new InMemorySecretStore()),
            () => Now, endpoints: LoopbackEndpoints(), firewall: fw);
        await vm.ShareVaultAsync("Ada", "ada@x");

        var ep = vm.StartServing(pairingPort: 0, syncPort: 0, internetEnabled: true);
        await fw.Ensured.Task;   // the background reachability work invoked the opener

        Assert.Equal(1, fw.EnsureCalls);
        Assert.Equal(vm.PairingEndpoint!.Port, fw.LastPairingPort);   // scoped to the ports we actually bound
        Assert.Equal(ep.Port, fw.LastSyncPort);
    }

    [Fact]
    public async Task Sharing_lan_only_never_touches_the_firewall()
    {
        var dir = NewVault();
        var fw = new RecordingFirewall();
        using var vm = new SyncViewModel(
            WikiSyncHostFactory.ForVault(dir, new InMemorySecretStore()),
            () => Now, endpoints: LoopbackEndpoints(), firewall: fw);
        await vm.ShareVaultAsync("Ada", "ada@x");

        vm.StartServing(pairingPort: 0, syncPort: 0, internetEnabled: false);

        // internetEnabled:false skips the whole reachability block — no background task is even spawned, so this
        // is a deterministic negative that needs no await.
        Assert.Equal(0, fw.EnsureCalls);
    }

    [Fact]
    public async Task Removing_internet_reachability_delegates_to_the_opener()
    {
        var dir = NewVault();
        var fw = new RecordingFirewall();
        using var vm = new SyncViewModel(
            WikiSyncHostFactory.ForVault(dir, new InMemorySecretStore()),
            () => Now, endpoints: LoopbackEndpoints(), firewall: fw);

        Assert.True(await vm.RemoveInternetReachabilityAsync());

        Assert.Equal(1, fw.RemoveCalls);
    }

    [Fact]
    public async Task Removing_internet_reachability_releases_the_router_mappings()
    {
        var dir = NewVault();
        var mapper = new GrantingMapper();
        var fw = new RecordingFirewall();
        using var vm = new SyncViewModel(
            WikiSyncHostFactory.ForVault(dir, new InMemorySecretStore()), () => Now,
            endpoints: new PublicEndpointProvider(mapper, () => Array.Empty<IPAddress>(), () => IPAddress.Loopback.ToString()),
            firewall: fw);
        await vm.ShareVaultAsync("Ada", "ada@x");
        vm.StartServing(pairingPort: 0, syncPort: 0, internetEnabled: true);

        // The remove first DRAINS the background gather, so both granted mappings are known — and released.
        Assert.True(await vm.RemoveInternetReachabilityAsync());

        Assert.Equal(2, mapper.UnmapCalls);   // pairing + sync mappings deleted at the router
        Assert.Equal(1, fw.RemoveCalls);
    }

    [Fact]
    public async Task Toggling_off_waits_for_an_inflight_firewall_add_before_removing()
    {
        var dir = NewVault();
        var fw = new SlowEnsureFirewall();
        using var vm = new SyncViewModel(
            WikiSyncHostFactory.ForVault(dir, new InMemorySecretStore()),
            () => Now, endpoints: LoopbackEndpoints(), firewall: fw);
        await vm.ShareVaultAsync("Ada", "ada@x");
        vm.StartServing(pairingPort: 0, syncPort: 0, internetEnabled: true);

        // Mid-session toggle-off while the elevated add is still in flight: the remove must NOT overtake it —
        // an add landing after the remove would re-open the admission the user just closed.
        vm.StopServing();
        var remove = vm.RemoveInternetReachabilityAsync();
        Assert.False(remove.IsCompleted);   // blocked draining the in-flight add
        fw.ReleaseAdd.TrySetResult();
        Assert.True(await remove);

        lock (fw.Calls) Assert.Equal(new[] { "add", "remove" }, fw.Calls);
    }

    [Fact]
    public async Task A_stale_gather_from_before_the_toggle_cannot_reintroduce_global_candidates()
    {
        var dir = NewVault();
        var mapper = new GatedMapper();
        var fw = new RecordingFirewall();
        var staleDispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var vm = new SyncViewModel(
            WikiSyncHostFactory.ForVault(dir, new InMemorySecretStore()), () => Now,
            a => { a(); staleDispatched.TrySetResult(); },
            new PublicEndpointProvider(mapper, () => new[] { IPAddress.Parse("2001:db8::1") }, () => IPAddress.Loopback.ToString()),
            fw);
        await vm.ShareVaultAsync("Ada", "ada@x");

        vm.StartServing(pairingPort: 0, syncPort: 0, internetEnabled: true);   // gather blocks on the mapper
        vm.StopServing();                                                       // user toggles internet sync off…
        vm.StartServing(pairingPort: 0, syncPort: 0, internetEnabled: false);   // …and LAN-only serving resumes

        // NOW the pre-toggle gather completes: its candidate set (LAN + global IPv6 + mapped IPv4) is stale —
        // the current bind is IPv4-LAN-only, so swapping it in would advertise endpoints nothing listens on.
        mapper.Result.TrySetResult(new PortMapping("203.0.113.9", 40000));
        await staleDispatched.Task;   // the stale swap has been dispatched (and must have been suppressed)

        var invite = vm.AddCollaborator(PeerRole.ReadWrite, TimeSpan.FromHours(1));
        Assert.True(InviteCodec.TryParse(invite, out var payload));
        Assert.True(SyncDiscoveryHint.TryParseMany(payload!.DiscoveryHint, out var cands));
        var only = Assert.Single(cands);
        Assert.Equal(IPAddress.Loopback.ToString(), only.Host);   // LAN only — no stale 2001:db8::1 / 203.0.113.9
    }

    [Fact]
    public async Task Remote_join_over_a_candidate_ladder_pairs_and_syncs_both_ways()
    {
        var ownerDir = NewVault(("Owner.md", "from owner"));
        var joinerDir = NewVault();

        // The owner's candidate gather runs off-thread; signal when its result has been applied so the invite
        // deterministically advertises the loopback candidate (not the fallback real-LAN address).
        var gathered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var owner = new SyncViewModel(
            WikiSyncHostFactory.ForVault(ownerDir, new InMemorySecretStore()),
            () => Now, a => { a(); gathered.TrySetResult(); }, LoopbackEndpoints(), new NoOpFirewallOpener());
        using var joiner = new SyncViewModel(
            WikiSyncHostFactory.ForVault(joinerDir, new InMemorySecretStore()),
            () => Now, endpoints: LoopbackEndpoints());

        await owner.ShareVaultAsync("Ada", "ada@x");
        // internetEnabled: true so the owner binds dual-stack (IPv6Any) AND runs the background UPnP gather whose
        // dispatch signals `gathered`. GatherLocal already lands the loopback candidate synchronously, so the
        // invite is reachable the moment StartServing returns; the await just makes the background gather
        // deterministic. The joiner only needs to be dialable back over loopback, so it stays IPv4-only.
        owner.StartServing(pairingPort: 0, syncPort: 0, internetEnabled: true);
        await gathered.Task;   // owner's invite advertises the reachable loopback candidate
        var joinerSync = joiner.StartServing(pairingPort: 0, syncPort: 0, internetEnabled: false);

        var invite = owner.AddCollaborator(PeerRole.ReadWrite, TimeSpan.FromHours(1));

        // No host override — the join must succeed purely from the advertised candidate ladder, no relay.
        var outcome = await joiner.JoinAsync(invite, "Bob", "bob@x");
        Assert.Equal(PairingOutcome.Accepted, outcome);

        // The initial sync (driven inside JoinAsync, over the candidate ladder) landed the owner's note on the
        // joiner, and the joiner now lists the owner as a collaborator.
        Assert.Equal("from owner", File.ReadAllText(Path.Combine(joinerDir, "Owner.md")));
        Assert.Contains(joiner.Collaborators, c => c.DeviceId == owner.DeviceId);

        // Loopback binds ephemeral ports that differ from the owner's own sync port, so hand the owner the
        // joiner's actual sync endpoint (in production both peers serve the shared default sync port).
        owner.RememberPeer(joiner.DeviceId, new IPEndPoint(IPAddress.Loopback, joinerSync.Port));

        File.WriteAllText(Path.Combine(ownerDir, "Owner2.md"), "owner second");
        File.WriteAllText(Path.Combine(joinerDir, "Joiner.md"), "from joiner");

        await owner.SyncNowAsync();    // refresh owner replica (Owner2) + pull joiner
        await joiner.SyncNowAsync();   // refresh joiner replica (Joiner) + pull owner → Owner2 lands
        await owner.SyncNowAsync();    // pull joiner again → Joiner lands (order-independent)

        Assert.Equal("owner second", File.ReadAllText(Path.Combine(joinerDir, "Owner2.md")));
        Assert.Equal("from joiner", File.ReadAllText(Path.Combine(ownerDir, "Joiner.md")));
    }

    [Fact]
    public async Task Join_with_only_a_dead_private_lan_candidate_returns_NoRoute()
    {
        var joinerDir = NewVault();
        using var joiner = new SyncViewModel(
            WikiSyncHostFactory.ForVault(joinerDir, new InMemorySecretStore()), () => Now, endpoints: LoopbackEndpoints());
        joiner.StartServing(pairingPort: 0, syncPort: 0, internetEnabled: false);

        // A hand-built invite whose only candidate is an unreachable PRIVATE LAN address — the owner never
        // enabled internet sync, so nothing globally reachable was advertised. The ladder dial fails → NoRoute.
        var hint = SyncDiscoveryHint.FormatMany(new[] { new SyncCandidate("192.168.199.199", 8768, 8767) });
        var invite = InviteCodec.Encode(new InvitePayload(
            "OWNERDEVICEIDNOTONNET", Guid.NewGuid(), PeerRole.ReadWrite,
            new byte[] { 1, 2, 3, 4 }, Now.AddHours(1), hint));

        var outcome = await joiner.JoinAsync(invite, "Bob", "bob@x");
        Assert.Equal(PairingOutcome.NoRoute, outcome);
    }

    [Fact]
    public async Task Join_with_only_an_unreachable_global_candidate_returns_OwnerUnreachable()
    {
        var joinerDir = NewVault();
        using var joiner = new SyncViewModel(
            WikiSyncHostFactory.ForVault(joinerDir, new InMemorySecretStore()), () => Now, endpoints: LoopbackEndpoints());
        joiner.StartServing(pairingPort: 0, syncPort: 0, internetEnabled: false);

        // The owner advertised a GLOBAL candidate (TEST-NET-3, RFC 5737 — globally scoped but never routed), so a
        // failed dial means "reached-for but unreachable": OwnerUnreachable ("they may be offline / your networks
        // can't connect"), NOT NoRoute (which is for a LAN-only owner who advertised nothing global).
        var hint = SyncDiscoveryHint.FormatMany(new[] { new SyncCandidate("203.0.113.1", 8768, 8767) });
        var invite = InviteCodec.Encode(new InvitePayload(
            "OWNERDEVICEIDNOTONNET", Guid.NewGuid(), PeerRole.ReadWrite,
            new byte[] { 1, 2, 3, 4 }, Now.AddHours(1), hint));

        var outcome = await joiner.JoinAsync(invite, "Bob", "bob@x");
        Assert.Equal(PairingOutcome.OwnerUnreachable, outcome);
    }

    [Fact]
    public async Task Join_that_connects_but_fails_the_exchange_returns_ExchangeFailed_not_unreachable()
    {
        var joinerDir = NewVault();
        using var joiner = new SyncViewModel(
            WikiSyncHostFactory.ForVault(joinerDir, new InMemorySecretStore()), () => Now, endpoints: LoopbackEndpoints());

        // A real owner listener whose pairing handler reads one byte of the JoinRequest and then closes without
        // ever answering: the mutual-TLS handshake succeeds (the owner WAS reached), then the exchange dies.
        // That must report ExchangeFailed — never OwnerUnreachable/NoRoute, which are verdicts about the dial.
        using var owner = DeviceIdentity.Create();
        using var listener = new PairingListener(owner,
            async (conn, ct) => { var b = new byte[1]; await conn.Stream.ReadExactlyAsync(b, ct); });
        var ep = listener.Start(new IPEndPoint(IPAddress.Loopback, 0));

        var hint = SyncDiscoveryHint.FormatMany(new[] { new SyncCandidate("127.0.0.1", ep.Port, ep.Port) });
        var invite = InviteCodec.Encode(new InvitePayload(
            owner.DeviceId, Guid.NewGuid(), PeerRole.ReadWrite,
            new byte[] { 1, 2, 3, 4 }, Now.AddHours(1), hint));

        var outcome = await joiner.JoinAsync(invite, "Bob", "bob@x");
        Assert.Equal(PairingOutcome.ExchangeFailed, outcome);
    }
}
