// SPDX-License-Identifier: GPL-3.0-or-later
using Collective.Platform.Secrets;
using Wiki.Core.Vault;
using Wiki.Sync;
using Wiki.Sync.Host;

namespace Wiki.Desktop.Sync;

/// <summary>Composes a <see cref="VaultSyncService"/> for an open vault the way the head needs it: a physical
/// vault fs + the `.cwiki/sync/` sidecar store + the stateless reconciler/sealer (mirroring AddWikiSync) + the
/// OS keystore. The head passes the OS-appropriate <see cref="SecretStores.CreateDefault"/> store (DPAPI on
/// Windows, an owner-only file store elsewhere); tests pass an in-memory one.</summary>
public static class WikiSyncHostFactory
{
    public static VaultSyncService ForVault(string vaultRoot, ISecretStore secrets)
    {
        var vault = new PhysicalVaultFileSystem(vaultRoot);
        var syncStore = new FileSyncStore(Path.Combine(vaultRoot, ".cwiki", "sync"));
        var reconciler = new AuthenticatingReconciler(new Reconciler(new Diff3MergeAdapter()), new ChangeVerifier());
        return VaultSyncHost.Open(vault, syncStore, secrets, new ContentKeySealer(), reconciler);
    }
}
