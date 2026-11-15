using System;
using System.Collections.Generic;
using System.Linq;

namespace Drift;

/// <summary>The ring around the Library button while files download: how full (null spins: a size isn't known yet), and whether it's greyed.</summary>
public readonly record struct RingState(double? Filled, bool Paused);

/// <summary>
/// One ring for every download running in the window, as Arc has it: everything received of
/// everything expected, greyed only when every one of them is paused.
/// </summary>
public static class DownloadRing
{
    /// <summary>The ring for <paramref name="running"/>; null when nothing's downloading.</summary>
    public static RingState? Of(IReadOnlyCollection<(long Received, long? Total, bool Paused)> running)
    {
        if (running.Count == 0) return null;
        var paused = running.All(d => d.Paused);
        if (running.Any(d => d.Total is not > 0)) return new RingState(null, paused);
        double received = running.Sum(d => d.Received), total = running.Sum(d => d.Total!.Value);
        return new RingState(Math.Min(1, received / total), paused);
    }
}
