// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Vault;

namespace Wiki.Core.Tests;

public class CwikiPathsTests
{
    [Fact]
    public void Sidecar_paths_compose_under_the_vault_root()
    {
        string root = Path.Combine("x", "vault");
        Assert.Equal(Path.Combine(root, ".cwiki"), CwikiPaths.SidecarDir(root));
        Assert.Equal(Path.Combine(root, ".cwiki", "index.db"), CwikiPaths.IndexDbPath(root));
    }
}
