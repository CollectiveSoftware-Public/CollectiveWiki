// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Time;

/// <summary>The wall-clock seam. Pure logic (templates, daily notes) takes time through this so it is
/// deterministically testable; only <see cref="SystemClock"/> reads the real clock.</summary>
public interface IClock
{
    DateTimeOffset Now { get; }
}
