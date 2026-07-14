// SPDX-License-Identifier: GPL-3.0-or-later
using CommunityToolkit.Mvvm.ComponentModel;
using Wiki.Sync;

namespace Wiki.Desktop.Sync;

/// <summary>One collaborator in the sharing UI: identity + role (from the signed roster) plus live
/// presence/last-synced (updated by the view model as syncs succeed or fail).</summary>
public partial class CollaboratorRow : ObservableObject
{
    public required string DeviceId { get; init; }
    public required string Name { get; init; }
    public required string Email { get; init; }
    public required PeerRole Role { get; init; }

    [ObservableProperty] private bool _online;
    [ObservableProperty] private DateTimeOffset? _lastSynced;
}
