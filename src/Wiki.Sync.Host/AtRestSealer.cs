// SPDX-License-Identifier: GPL-3.0-or-later
using Collective.Platform.Secrets;

namespace Wiki.Sync.Host;

/// <summary>Seals/unseals sync secrets at rest with a 32-byte device key kept in the OS keystore
/// (<see cref="ISecretStore"/>). The device key is created on first use and stored Base64 (via the
/// shared <see cref="DeviceKeyProvider"/>). The caller supplies the store — DPAPI on Windows, an
/// owner-only file store elsewhere (<see cref="SecretStores.CreateDefault"/>); tests inject an
/// in-memory fake. The AES-GCM cipher is the shared <see cref="DeviceKeyCipher"/> (magic
/// <c>CWK1</c>) — unchanged blob layout, so every existing sealed sidecar stays readable.</summary>
public sealed class AtRestSealer(ISecretStore secrets, string keyName = "collectivewiki.sync.devicekey")
{
    private readonly DeviceKeyProvider _deviceKey = new(secrets, keyName);
    private static readonly DeviceKeyCipher Cipher = new("CWK1"u8);

    // ConfigureAwait(false) matters: the sync stores bridge these synchronously, sometimes on the UI
    // thread — a context-captured continuation would deadlock against the blocked dispatcher.
    public async Task<byte[]> SealAsync(byte[] plaintext, CancellationToken ct = default)
    {
        var key = await _deviceKey.GetOrCreateAsync(ct).ConfigureAwait(false);   // await first — a span temporary can't cross the await boundary
        return Cipher.Encrypt(plaintext, key);
    }

    public async Task<byte[]> UnsealAsync(byte[] blob, CancellationToken ct = default)
    {
        var key = await _deviceKey.GetOrCreateAsync(ct).ConfigureAwait(false);
        return Cipher.Decrypt(blob, key);
    }
}
