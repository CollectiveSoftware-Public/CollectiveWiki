// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;

namespace Wiki.Sync.Host;

/// <summary>An <see cref="IIdentityStore"/> that persists the device certificate (PFX bytes) to
/// `.cwiki/sync/identity.bin`, sealed at rest with the OS-keystore device key. Feed it to
/// <see cref="DeviceIdentityProvider.LoadOrCreate"/> — the identity is created + sealed on first run and
/// unsealed on later runs. Load/Save are synchronous (the <see cref="IIdentityStore"/> contract), so they
/// block on the async sealer; these calls are rare (startup only).</summary>
public sealed class EncryptedFileIdentityStore(ISyncStore store, AtRestSealer sealer, string name = "identity.bin") : IIdentityStore
{
    // Task.Run so the sealer's continuations never need the caller's context — blocking a dispatcher
    // thread on a context-captured await deadlocks (the share-vault freeze).
    public byte[]? Load()
    {
        var blob = store.ReadBytes(name);
        return blob is null ? null : Task.Run(() => sealer.UnsealAsync(blob)).GetAwaiter().GetResult();
    }

    public void Save(byte[] pfx) => store.WriteBytes(name, Task.Run(() => sealer.SealAsync(pfx)).GetAwaiter().GetResult());
}
