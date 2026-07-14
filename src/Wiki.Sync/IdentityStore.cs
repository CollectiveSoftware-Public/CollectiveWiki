// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Sync;

/// <summary>Persists a device's exported identity (PKCS#12 bytes). The concrete file-backed,
/// at-rest-encrypted store lives in the desktop head (Plan F) over the platform file system; the core
/// only knows this seam so identity provisioning is headless-testable.</summary>
public interface IIdentityStore
{
    byte[]? Load();
    void Save(byte[] pfx);
}

/// <summary>A volatile store for tests and ephemeral sessions.</summary>
public sealed class InMemoryIdentityStore : IIdentityStore
{
    private byte[]? _pfx;
    public byte[]? Load() => _pfx;
    public void Save(byte[] pfx) => _pfx = pfx;
}

/// <summary>Loads the persisted device identity, generating and saving one on first use — so a device
/// keeps a stable identity across restarts.</summary>
public static class DeviceIdentityProvider
{
    public static DeviceIdentity LoadOrCreate(IIdentityStore store, string? password = null)
    {
        var existing = store.Load();
        if (existing is not null) return DeviceIdentity.Import(existing, password);

        var identity = DeviceIdentity.Create();
        store.Save(identity.Export(password));
        return identity;
    }
}
