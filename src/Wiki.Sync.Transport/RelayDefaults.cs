// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Sync.Transport;

/// <summary>Defaults for the WAN relay. Per the §11.1 decision resolved at the start of Plan E, the project
/// operates NO public default relay: <see cref="Host"/> is null (WAN relay is off until the user sets a
/// self-hosted address in settings), while LAN sync needs no configuration. Only the well-known
/// <see cref="Port"/> is baked in so a self-hosted relay and its clients agree by default. A public community
/// relay can be introduced later as a pure ops decision — the endpoint is already user-configurable.</summary>
public static class RelayDefaults
{
    public const int Port = 9430;
    public const string? Host = null;
}
