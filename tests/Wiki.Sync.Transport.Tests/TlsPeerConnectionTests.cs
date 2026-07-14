// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;
using Wiki.Sync.Transport;

namespace Wiki.Sync.Transport.Tests;

public class TlsPeerConnectionTests
{
    [Fact]
    public async Task Mutual_tls_authenticates_and_pins_both_device_ids()
    {
        using var server = DeviceIdentity.Create();
        using var client = DeviceIdentity.Create();
        var (s, c) = Loopback.TcpPair();

        var serverTask = TlsPeerConnection.AuthenticateServerAsync(s, server, id => id == client.DeviceId);
        var clientTask = TlsPeerConnection.AuthenticateClientAsync(c, client, server.DeviceId);
        await Task.WhenAll(serverTask, clientTask);

        using var sc = await serverTask;
        using var cc = await clientTask;
        Assert.Equal(client.DeviceId, sc.RemoteDeviceId);
        Assert.Equal(server.DeviceId, cc.RemoteDeviceId);
    }

    [Fact]
    public async Task A_client_pinning_the_wrong_server_id_is_rejected()
    {
        using var server = DeviceIdentity.Create();
        using var client = DeviceIdentity.Create();
        using var stranger = DeviceIdentity.Create();
        var (s, c) = Loopback.TcpPair();

        var serverTask = TlsPeerConnection.AuthenticateServerAsync(s, server, _ => true);
        var clientTask = TlsPeerConnection.AuthenticateClientAsync(c, client, stranger.DeviceId);

        await Assert.ThrowsAnyAsync<Exception>(async () => await clientTask);
        try { (await serverTask).Dispose(); } catch { /* server side faults too */ }
    }

    [Fact]
    public async Task A_server_refusing_an_unlisted_peer_rejects_the_handshake()
    {
        using var server = DeviceIdentity.Create();
        using var client = DeviceIdentity.Create();
        var (s, c) = Loopback.TcpPair();

        var serverTask = TlsPeerConnection.AuthenticateServerAsync(s, server, _ => false); // no peer allowed
        var clientTask = TlsPeerConnection.AuthenticateClientAsync(c, client, server.DeviceId);

        await Assert.ThrowsAnyAsync<Exception>(async () => await serverTask);
        try { (await clientTask).Dispose(); } catch { }
    }
}
