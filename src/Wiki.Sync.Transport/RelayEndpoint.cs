// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
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

    /// <summary>Dial a peer by trying an ordered list of candidate endpoints (LAN first, then IPv6, then
    /// public IPv4), each bounded by <paramref name="perCandidateTimeout"/> so a dead candidate can't stall
    /// the ladder. The relay tier stays available but is unused in v1 (callers pass null). Throws
    /// <see cref="InvalidOperationException"/> when there is nothing to try, otherwise the last dial failure.</summary>
    public static async Task<TlsPeerConnection> ConnectAsync(
        DeviceIdentity self, string targetDeviceId, IReadOnlyList<IPEndPoint> candidates,
        RelayEndpoint? relay = null, TimeSpan? perCandidateTimeout = null, CancellationToken ct = default)
    {
        var timeout = perCandidateTimeout ?? TimeSpan.FromSeconds(3);
        Exception? last = null;
        foreach (var ep in candidates)
        {
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attempt.CancelAfter(timeout);
            try { return await DialLanAsync(ep, self, targetDeviceId, attempt.Token); }
            catch (Exception ex) { last = ex; }   // try the next candidate
        }
        if (relay is not null)   // dormant in v1 — no caller passes a relay
        {
            var stream = await RelayClient.ConnectAsync(relay.Host, relay.Port, targetDeviceId, ct);
            try { return await TlsPeerConnection.AuthenticateClientAsync(stream, self, targetDeviceId, ct); }
            catch { await stream.DisposeAsync(); throw; }
        }
        if (last is not null) ExceptionDispatchInfo.Capture(last).Throw();   // rethrow the real dial failure, stack intact
        throw new InvalidOperationException($"no route to peer {targetDeviceId}: no candidate endpoints");
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
