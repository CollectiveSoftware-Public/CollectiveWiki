// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;

namespace Wiki.Sync.Host.Tests;

public class AtRestSealerTests
{
    [Fact]
    public async Task Seal_unseal_round_trips_through_a_keystore_generated_device_key()
    {
        var secrets = new FakeSecretStore();
        var sealer = new AtRestSealer(secrets);
        var data = Encoding.UTF8.GetBytes("roster + keys");
        var blob = await sealer.SealAsync(data);
        Assert.Equal(data, await sealer.UnsealAsync(blob));
    }

    [Fact]
    public async Task The_device_key_persists_so_a_new_sealer_can_unseal()
    {
        var secrets = new FakeSecretStore();               // one keystore, reused
        var blob = await new AtRestSealer(secrets).SealAsync(Encoding.UTF8.GetBytes("y"));
        Assert.Equal("y", Encoding.UTF8.GetString(await new AtRestSealer(secrets).UnsealAsync(blob)));
    }
}
