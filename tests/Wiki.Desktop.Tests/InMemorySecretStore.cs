// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Concurrent;
using Collective.Platform.Secrets;

namespace Wiki.Desktop.Tests;

/// <summary>An in-memory <see cref="ISecretStore"/> so sync tests run cross-platform (the real
/// DpapiSecretStore is Windows-only). Mirrors the sibling repos' test-support stores.</summary>
internal sealed class InMemorySecretStore : ISecretStore
{
    private readonly ConcurrentDictionary<string, string> _secrets = new();

    public Task SetAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        _secrets[key] = secret;
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(_secrets.TryGetValue(key, out var v) ? v : null);

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _secrets.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
