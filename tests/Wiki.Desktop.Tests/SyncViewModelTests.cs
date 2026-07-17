// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Sync;
using Wiki.Desktop.Sync;
using Wiki.Sync;
using Xunit;

namespace Wiki.Desktop.Tests;

public class SyncViewModelTests : IDisposable
{
    private readonly List<string> _dirs = new();
    private string NewVault()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cwiki-f2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _dirs.Add(dir);
        return dir;
    }
    public void Dispose() { foreach (var d in _dirs) { try { Directory.Delete(d, true); } catch { } } }

    private SyncViewModel Open(string dir) => new(WikiSyncHostFactory.ForVault(dir, new InMemorySecretStore()));

    [Fact]
    public void Factory_persists_identity_across_reopens()
    {
        var dir = NewVault();
        var secrets = new InMemorySecretStore();
        var id1 = WikiSyncHostFactory.ForVault(dir, secrets).DeviceId;
        var id2 = WikiSyncHostFactory.ForVault(dir, secrets).DeviceId;
        Assert.Equal(id1, id2);   // at-rest identity persisted under .cwiki/sync + reloaded
    }

    [Fact]
    public async Task Sharing_makes_us_the_owner_with_no_collaborators_yet()
    {
        using var vm = Open(NewVault());
        Assert.False(vm.IsOwner);
        await vm.ShareVaultAsync("Ada", "ada@x");
        Assert.True(vm.IsOwner);
        Assert.True(vm.HasRoster);
        Assert.Empty(vm.Collaborators);          // only ourselves are on the roster
        Assert.Equal(SyncStatus.Idle, vm.Status);
    }

    // Regression for the frozen share flow: auto-pull ran SyncNowAsync on a thread-pool thread, so its
    // observable mutations (Status, the bound Collaborators list) hit Avalonia's thread check and every
    // tick died before pulling. Ticks must route through the injected dispatcher instead.
    [Fact]
    public async Task Auto_pull_ticks_route_through_the_injected_dispatcher()
    {
        var dir = NewVault();
        int dispatched = 0;
        using var vm = new SyncViewModel(
            WikiSyncHostFactory.ForVault(dir, new InMemorySecretStore()),
            dispatch: a => { Interlocked.Increment(ref dispatched); a(); });

        await vm.ShareVaultAsync("Ada", "ada@x");
        vm.StartServing(autoPullEvery: TimeSpan.FromMilliseconds(30));

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Volatile.Read(ref dispatched) < 2 && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        vm.StopServing();

        Assert.True(Volatile.Read(ref dispatched) >= 2, "auto-pull ticks must be marshaled through the dispatcher");
    }

    [Fact]
    public void A_fresh_unshared_vault_has_no_roster()
    {
        using var vm = Open(NewVault());
        Assert.False(vm.IsOwner);
        Assert.False(vm.HasRoster);
    }

    [Fact]
    public async Task AddCollaborator_requires_serving_first()
    {
        using var vm = Open(NewVault());
        await vm.ShareVaultAsync("Ada", "ada@x");
        Assert.Throws<InvalidOperationException>(() => vm.AddCollaborator(PeerRole.ReadWrite, TimeSpan.FromHours(1)));
    }
}
