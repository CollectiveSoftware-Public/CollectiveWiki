// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;

namespace Wiki.Sync;

/// <summary>The vault's symmetric content key for a given rotation <paramref name="Epoch"/> (256-bit).
/// In later plans it encrypts note content in transit/relay envelopes; here it is generated, sealed to
/// peers, and rotated. A new epoch means a brand-new random key — the basis of post-revocation secrecy.</summary>
public sealed record ContentKey(int Epoch, byte[] Key)
{
    public static ContentKey Generate(int epoch) => new(epoch, RandomNumberGenerator.GetBytes(32));
}

/// <summary>A content key sealed for exactly one recipient device: the AES-256-GCM nonce, ciphertext, and
/// tag, tagged with the epoch and recipient. Only the recipient's private key derives the unwrapping KEK.</summary>
public sealed record SealedContentKey(int Epoch, string RecipientDeviceId, byte[] Nonce, byte[] Ciphertext, byte[] Tag);

/// <summary>Seals/unseals a <see cref="ContentKey"/> for a peer using static-static ECDH → HKDF-SHA256 →
/// AES-256-GCM. Stateless (registered in DI). The epoch is bound as GCM associated data, and the KEK is
/// derived with the ordered (sender, recipient) device ids in its HKDF info, so a blob cannot be replayed
/// under a different epoch or recipient.</summary>
public sealed class ContentKeySealer
{
    private const int NonceLen = 12, TagLen = 16, KekLen = 32;

    public SealedContentKey Seal(DeviceIdentity sender, AuthorizedPeer recipient, ContentKey key)
    {
        var kek = DeriveKek(sender.DeriveSharedSecret(recipient.PublicKey), sender.DeviceId, recipient.DeviceId);
        var nonce = RandomNumberGenerator.GetBytes(NonceLen);
        var ciphertext = new byte[key.Key.Length];
        var tag = new byte[TagLen];
        using var aes = new AesGcm(kek, TagLen);
        aes.Encrypt(nonce, key.Key, ciphertext, tag, Aad(key.Epoch));
        return new SealedContentKey(key.Epoch, recipient.DeviceId, nonce, ciphertext, tag);
    }

    public ContentKey? Unseal(DeviceIdentity self, byte[] senderPublicKey, string senderDeviceId, SealedContentKey sealed_)
    {
        var kek = DeriveKek(self.DeriveSharedSecret(senderPublicKey), senderDeviceId, self.DeviceId);
        var plaintext = new byte[sealed_.Ciphertext.Length];
        try
        {
            using var aes = new AesGcm(kek, TagLen);
            aes.Decrypt(sealed_.Nonce, sealed_.Ciphertext, sealed_.Tag, plaintext, Aad(sealed_.Epoch));
        }
        catch (CryptographicException)
        {
            return null;   // wrong recipient key or tampered blob → authentication fails
        }
        return new ContentKey(sealed_.Epoch, plaintext);
    }

    private static byte[] DeriveKek(byte[] sharedSecret, string senderDeviceId, string recipientDeviceId)
    {
        var info = Encoding.UTF8.GetBytes($"cwiki-contentkey/v1\n{senderDeviceId}\n{recipientDeviceId}");
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, KekLen, salt: null, info);
    }

    private static byte[] Aad(int epoch) => BitConverter.GetBytes(epoch);
}
