// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net;
using System.Net.Sockets;
using Wiki.Sync;

namespace Wiki.Sync.Transport;

/// <summary>A relay's dialable address.</summary>
public sealed record RelayEndpoint(string Host, int Port);

/// <summary>The connect-tier ladder (spec §5/§8): to reach a peer, try a direct LAN connection first (when a
/// mDNS-discovered endpoint is known) and fall back to the relay. Returns an authenticated, device-id-pinned
/// <see cref="TlsPeerConnection"/> to the target — identical whichever tier carried it, so the sync client
/// above is transport-agnostic. Hole-punched WAN-direct is a later tier (deferred with spec §8 tier 2).</summary>
public static class PeerConnector
{
    public static async Task<TlsPeerConnection> ConnectAsync(
        DeviceIdentity self, string targetDeviceId,
        IPEndPoint? lanEndpoint, RelayEndpoint? relay, CancellationToken ct = default)
    {
        if (lanEndpoint is not null)
        {
            try { return await DialLanAsync(lanEndpoint, self, targetDeviceId, ct); }
            catch when (relay is not null) { /* LAN unreachable — fall back to the relay */ }
        }
        if (relay is not null)
        {
            var stream = await RelayClient.ConnectAsync(relay.Host, relay.Port, targetDeviceId, ct);
            try { return await TlsPeerConnection.AuthenticateClientAsync(stream, self, targetDeviceId, ct); }
            catch { await stream.DisposeAsync(); throw; }
        }
        throw new InvalidOperationException(
            $"no route to peer {targetDeviceId}: no LAN endpoint and no relay configured");
    }

    private static async Task<TlsPeerConnection> DialLanAsync(
        IPEndPoint ep, DeviceIdentity self, string expected, CancellationToken ct)
    {
        var tcp = new TcpClient();
        try
        {
            await tcp.ConnectAsync(ep, ct);
            return await TlsPeerConnection.AuthenticateClientAsync(tcp.GetStream(), self, expected, ct);
        }
        catch { tcp.Dispose(); throw; }
    }
}
