// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Concurrent;

namespace Wiki.Sync.Host.Tests;

internal sealed class InMemorySyncStore : ISyncStore
{
    private readonly ConcurrentDictionary<string, byte[]> _map = new();
    public bool Exists(string name) => _map.ContainsKey(name);
    public byte[]? ReadBytes(string name) => _map.TryGetValue(name, out var v) ? v : null;
    public void WriteBytes(string name, byte[] data) => _map[name] = data;
    public void Delete(string name) => _map.TryRemove(name, out _);
}
