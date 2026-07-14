// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Concurrent;
using Collective.Platform.Secrets;

namespace Wiki.Sync.Host.Tests;

/// <summary>An in-memory ISecretStore so at-rest tests run cross-platform (the real DpapiSecretStore is
/// Windows-only). Reusing one instance across sealer constructions models a persistent OS keystore.</summary>
internal sealed class FakeSecretStore : ISecretStore
{
    private readonly ConcurrentDictionary<string, string> _map = new();
    public Task SetAsync(string key, string secret, CancellationToken ct = default) { _map[key] = secret; return Task.CompletedTask; }
    public Task<string?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult(_map.TryGetValue(key, out var v) ? v : null);
    public Task RemoveAsync(string key, CancellationToken ct = default) { _map.TryRemove(key, out _); return Task.CompletedTask; }
}
