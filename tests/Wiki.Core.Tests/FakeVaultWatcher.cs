// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Vault;

namespace Wiki.Core.Tests;

/// <summary>A hand-driven <see cref="IVaultWatcher"/>: tests call Emit(...) to simulate FS events.</summary>
public sealed class FakeVaultWatcher : IVaultWatcher
{
    public event EventHandler<VaultChange>? Changed;
    public void Start() { }
    public void Emit(VaultChange change) => Changed?.Invoke(this, change);
    public void Dispose() { }
}
