// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Vault;

/// <summary>Coalesces bursts of file-system events. FileSystemWatcher fires many events for a single
/// save; this keeps the latest change per path and only releases it once that path has been quiet for a
/// configurable interval. Pure and deterministic — driven by caller-supplied logical ticks so it is
/// unit-testable without real time.</summary>
public sealed class WatchDebouncer
{
    private readonly Dictionary<string, (VaultChange Change, long LastTick)> _pending = new(StringComparer.Ordinal);

    public void Observe(VaultChange change, long tick) => _pending[change.Path] = (change, tick);

    /// <summary>Returns (and removes) every pending change whose path has been quiet for at least
    /// <paramref name="quietTicks"/> as of <paramref name="nowTick"/>.</summary>
    public IReadOnlyList<VaultChange> Drain(long nowTick, long quietTicks)
    {
        var ready = _pending.Where(kv => nowTick - kv.Value.LastTick >= quietTicks)
                            .Select(kv => kv.Key).ToList();
        var result = new List<VaultChange>(ready.Count);
        foreach (var path in ready)
        {
            result.Add(_pending[path].Change);
            _pending.Remove(path);
        }
        return result;
    }
}
