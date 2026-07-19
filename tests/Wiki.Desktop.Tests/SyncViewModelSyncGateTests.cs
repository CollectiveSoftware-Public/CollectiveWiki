// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net;
using Wiki.Desktop.Sync;
using Wiki.Sync;
using Xunit;

namespace Wiki.Desktop.Tests;

public class SyncViewModelSyncGateTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
    private readonly List<string> _dirs = new();

    private string NewVault(params (string name, string body)[] notes)
    {
        var dir = Path.Combine(Path.GetTempPath(), "cwiki-f2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var (n, b) in notes) File.WriteAllText(Path.Combine(dir, n), b);
        _dirs.Add(dir);
        return dir;
    }
    public void Dispose() { foreach (var d in _dirs) { try { Directory.Delete(d, true); } catch { } } }

    private SyncViewModel Open(string dir) => new(WikiSyncHostFactory.ForVault(dir, new InMemorySecretStore()), () => Now);

    [Fact]
    public async Task Owner_and_joiner_pair_via_invite_then_converge_both_ways()
    {
        var ownerDir = NewVault(("Owner.md", "from owner"));
        var joinerDir = NewVault();
        using var owner = Open(ownerDir);
        using var joiner = Open(joinerDir);

        await owner.ShareVaultAsync("Ada", "ada@x");
        owner.StartServing();
        var joinerSync = joiner.StartServing();

        var invite = owner.AddCollaborator(PeerRole.ReadWrite, TimeSpan.FromHours(1));
        var outcome = await joiner.JoinAsync(invite, "Bob", "bob@x", hostOverride: "127.0.0.1");
        Assert.Equal(PairingOutcome.Accepted, outcome);

        // Initial sync landed the owner's note on the joiner; the joiner lists the owner as a collaborator.
        Assert.Equal("from owner", File.ReadAllText(Path.Combine(joinerDir, "Owner.md")));
        Assert.Contains(joiner.Collaborators, c => c.DeviceId == owner.DeviceId);

        // The head learns a collaborator's return address from the observed connection; loopback uses ephemeral
        // ports (and the dual-stack bind reports its address as ::), so give the owner the joiner's actual sync
        // endpoint explicitly as a dialable loopback address.
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
    public async Task A_malformed_invite_is_rejected()
    {
        using var joiner = Open(NewVault());
        joiner.StartServing();
        Assert.Equal(PairingOutcome.WrongVault, await joiner.JoinAsync("not-an-invite", "Bob", "bob@x"));
    }
}
