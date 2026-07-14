// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Vault;

/// <summary>Abstracts a debounced file watcher over the vault tree. The index and (later) sync subscribe
/// to <see cref="Changed"/>. Concretes: a BCL FileSystemWatcher impl (Phase 1) + FakeVaultWatcher (tests).</summary>
public interface IVaultWatcher : IDisposable
{
    event EventHandler<VaultChange>? Changed;
    void Start();
}
