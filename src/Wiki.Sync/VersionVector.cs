// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Sync;

/// <summary>How two version vectors relate (causal ordering).</summary>
public enum VectorOrdering { Equal, Dominates, DominatedBy, Concurrent }

/// <summary>An immutable per-file version vector: device id → monotonically increasing counter.
/// Missing devices read as 0. Two vectors are <see cref="VectorOrdering.Concurrent"/> when each is
/// ahead of the other on some device — the signal that an edit conflict must be reconciled.</summary>
public sealed class VersionVector
{
    private readonly IReadOnlyDictionary<string, long> _counters;

    public VersionVector(IReadOnlyDictionary<string, long> counters)
        => _counters = new Dictionary<string, long>(counters);

    public static VersionVector Empty { get; } = new(new Dictionary<string, long>());

    public long this[string deviceId] => _counters.TryGetValue(deviceId, out var v) ? v : 0;

    public IEnumerable<string> Devices => _counters.Keys;

    public VersionVector Increment(string deviceId)
    {
        var next = new Dictionary<string, long>(_counters) { [deviceId] = this[deviceId] + 1 };
        return new VersionVector(next);
    }

    public VersionVector Merge(VersionVector other)
    {
        var next = new Dictionary<string, long>(_counters);
        foreach (var dev in other.Devices)
            next[dev] = Math.Max(this[dev], other[dev]);
        return new VersionVector(next);
    }

    public VectorOrdering CompareTo(VersionVector other)
    {
        bool selfAhead = false, otherAhead = false;
        var all = new HashSet<string>(Devices);
        all.UnionWith(other.Devices);
        foreach (var dev in all)
        {
            if (this[dev] > other[dev]) selfAhead = true;
            else if (this[dev] < other[dev]) otherAhead = true;
        }
        if (selfAhead && otherAhead) return VectorOrdering.Concurrent;
        if (selfAhead) return VectorOrdering.Dominates;
        if (otherAhead) return VectorOrdering.DominatedBy;
        return VectorOrdering.Equal;
    }
}
