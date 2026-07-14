// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync.Transport;

namespace Wiki.Sync.Transport.Tests;

public class RelayClientTests
{
    [Fact]
    public async Task Hello_encodes_the_exact_wire_bytes() // golden — pins compat with the CollectiveRelay repo
    {
        using var ms = new MemoryStream();
        await RelayProtocol.WriteHelloAsync(ms, RelayRole.Connect, "AB", default);
        Assert.Equal(new byte[] { 0x02, 0x00, 0x02, 0x41, 0x42 }, ms.ToArray());
    }

    [Fact]
    public async Task Register_and_connect_rendezvous_and_forward_bytes()
    {
        using var relay = new TestRelay();
        var ep = relay.Start();

        var regStream = await RelayClient.RegisterAsync(ep.Address.ToString(), ep.Port, "peer-1");
        await relay.WaitForRegistrationAsync("peer-1");
        var conStream = await RelayClient.ConnectAsync(ep.Address.ToString(), ep.Port, "peer-1");

        using (regStream)
        using (conStream)
        {
            await conStream.WriteAsync(new byte[] { 7, 8, 9 });
            await conStream.FlushAsync();
            var got = new byte[3];
            await ReadExactAsync(regStream, got);
            Assert.Equal(new byte[] { 7, 8, 9 }, got);
        }
    }

    [Fact]
    public async Task Connecting_to_an_unregistered_peer_throws()
    {
        using var relay = new TestRelay();
        var ep = relay.Start();
        await Assert.ThrowsAsync<IOException>(async () =>
            await RelayClient.ConnectAsync(ep.Address.ToString(), ep.Port, "nobody"));
    }

    private static async Task ReadExactAsync(Stream s, byte[] buf)
    {
        int off = 0;
        while (off < buf.Length)
        {
            int n = await s.ReadAsync(buf.AsMemory(off));
            if (n == 0) throw new EndOfStreamException();
            off += n;
        }
    }
}
