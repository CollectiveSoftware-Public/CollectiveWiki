// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Time;

/// <summary>The production <see cref="IClock"/>: the host's local wall clock.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}
