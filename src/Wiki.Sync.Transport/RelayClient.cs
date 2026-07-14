// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net.Sockets;

namespace Wiki.Sync.Transport;

/// <summary>The client side of the CollectiveRelay wire. A serving peer REGISTERs its device id and gets a
/// stream that becomes the end-to-end pipe once a dialer connects; a dialing peer CONNECTs to a target device
/// id and gets a stream to it. The returned stream carries the mutual-TLS session (the relay only forwards
/// ciphertext) — hand it to <see cref="TlsPeerConnection"/> exactly like a direct LAN socket.</summary>
public static class RelayClient
{
    public static Task<Stream> RegisterAsync(string relayHost, int relayPort, string deviceId, CancellationToken ct = default)
        => HandshakeAsync(relayHost, relayPort, RelayRole.Register, deviceId,
            "relay rejected registration (device id already registered)", ct);

    public static Task<Stream> ConnectAsync(string relayHost, int relayPort, string targetDeviceId, CancellationToken ct = default)
        => HandshakeAsync(relayHost, relayPort, RelayRole.Connect, targetDeviceId,
            $"relay has no peer registered for {targetDeviceId}", ct);

    private static async Task<Stream> HandshakeAsync(
        string host, int port, RelayRole role, string deviceId, string rejectMessage, CancellationToken ct)
    {
        var tcp = new TcpClient();
        try
        {
            await tcp.ConnectAsync(host, port, ct);
            var stream = tcp.GetStream();
            await RelayProtocol.WriteHelloAsync(stream, role, deviceId, ct);
            if (!await RelayProtocol.ReadAckAsync(stream, ct)) throw new IOException(rejectMessage);
            return new RelayStream(tcp, stream);
        }
        catch { tcp.Dispose(); throw; }
    }
}
