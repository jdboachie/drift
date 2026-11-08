using System.Collections.Generic;
using System.Linq;

namespace Drift;

/// <summary>
/// A page's background colour, from a picture of it: what most of its edges share (corners and edge
/// middles, just inside), so the space around it can be filled to match. Colours are ARGB.
/// </summary>
public static class PageColor
{
    public static (int X, int Y)[] SamplePoints(int width, int height)
    {
        int left = 2, top = 2, right = width - 3, bottom = height - 3, middleX = (width - 1) / 2, middleY = (height - 1) / 2;
        return [(left, top), (right, top), (left, bottom), (right, bottom), (middleX, top), (middleX, bottom), (left, middleY), (right, middleY)];
    }

    public static uint? Pick(IReadOnlyList<uint> samples)
    {
        if (samples.Count == 0) return null;
        // The most shared; on a tie, the one sampled first.
        return samples.GroupBy(c => c).OrderByDescending(g => g.Count()).First().Key;
    }
}
