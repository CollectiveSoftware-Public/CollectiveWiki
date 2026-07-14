// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Sync;

/// <summary>Owner-side holder of the vault's current content key and its rotation. After a revocation the
/// head calls <see cref="Rotate"/> then <see cref="SealCurrentFor"/> over the surviving roster — the
/// revoked device is simply never in that list, so it never receives the new key (post-revocation
/// secrecy, spec §7). Stateful/identity-driven, so it is head-constructed (not registered in DI).</summary>
public sealed class VaultKeyRing
{
    private readonly ContentKeySealer _sealer;

    public VaultKeyRing(ContentKey initial, ContentKeySealer? sealer = null)
    {
        Current = initial;
        _sealer = sealer ?? new ContentKeySealer();
    }

    /// <summary>Start a new key ring at epoch 0 with a fresh random content key.</summary>
    public static VaultKeyRing Start(ContentKeySealer? sealer = null) => new(ContentKey.Generate(0), sealer);

    public ContentKey Current { get; private set; }

    public ContentKey Rotate()
    {
        Current = ContentKey.Generate(Current.Epoch + 1);
        return Current;
    }

    /// <summary>Seal the current content key for every recipient except the sealing owner itself
    /// (the owner already holds the key).</summary>
    public IReadOnlyList<SealedContentKey> SealCurrentFor(DeviceIdentity owner, IEnumerable<AuthorizedPeer> recipients)
        => recipients
            .Where(p => p.DeviceId != owner.DeviceId)
            .Select(p => _sealer.Seal(owner, p, Current))
            .ToList();
}
