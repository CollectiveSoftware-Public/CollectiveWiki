// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Vault;

public enum VaultChangeKind { Added, Modified, Deleted, Renamed }

/// <summary>A single vault file-system event. For <see cref="VaultChangeKind.Renamed"/>, <paramref name="Path"/>
/// is the new path and <paramref name="OldPath"/> the previous one.</summary>
public sealed record VaultChange(VaultChangeKind Kind, string Path, string? OldPath);
