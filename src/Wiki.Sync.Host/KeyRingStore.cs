// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using Wiki.Sync;

namespace Wiki.Sync.Host;

/// <summary>Persists the vault content-key ring (current epoch + key) to `.cwiki/sync/keyring.bin`, sealed at
/// rest with the device key — the content key is a durable secret. Reconstructs a <see cref="VaultKeyRing"/>
/// bound to the DI <see cref="ContentKeySealer"/> used for peer fan-out.</summary>
public sealed class KeyRingStore(ISyncStore store, AtRestSealer sealer, string name = "keyring.bin")
{
    private sealed record Dto(int Epoch, string Key);

    public void Save(VaultKeyRing ring)
    {
        var k = ring.Current;
        var json = JsonSerializer.SerializeToUtf8Bytes(new Dto(k.Epoch, Convert.ToBase64String(k.Key)));
        // Task.Run so the sealer's continuations never need the caller's context — the desktop calls
        // this on the UI thread (Share dialog), and blocking there on a context-captured await froze the app.
        store.WriteBytes(name, Task.Run(() => sealer.SealAsync(json)).GetAwaiter().GetResult());
    }

    public VaultKeyRing? Load(ContentKeySealer contentSealer)
    {
        var blob = store.ReadBytes(name);
        if (blob is null) return null;
        var json = Task.Run(() => sealer.UnsealAsync(blob)).GetAwaiter().GetResult();
        var dto = JsonSerializer.Deserialize<Dto>(json)!;
        return new VaultKeyRing(new ContentKey(dto.Epoch, Convert.FromBase64String(dto.Key)), contentSealer);
    }
}
