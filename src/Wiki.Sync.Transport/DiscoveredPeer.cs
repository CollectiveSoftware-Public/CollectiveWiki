// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Sync.Transport;

/// <summary>A peer found on the LAN by <see cref="MdnsBrowser"/>: its pinned device id (from the service TXT
/// record), and the endpoint to dial (host from the datagram source, port from the SRV record).</summary>
public sealed record DiscoveredPeer(string DeviceId, string Host, int Port);
