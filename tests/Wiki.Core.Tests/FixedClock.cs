// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Time;

namespace Wiki.Core.Tests;

/// <summary>A deterministic <see cref="IClock"/> for tests.</summary>
public sealed class FixedClock : IClock
{
    public FixedClock(DateTimeOffset now) => Now = now;
    public DateTimeOffset Now { get; set; }
}
