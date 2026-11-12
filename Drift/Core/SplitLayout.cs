namespace Drift;

public enum SplitSide { Left, Right }

/// <summary>Split view's geometry: which side a dropped tab goes, and where the divider sits.</summary>
public static class SplitLayout
{
    /// <summary>The half of the page a dragged tab is over.</summary>
    public static SplitSide Side(double x, double width) => x < width / 2 ? SplitSide.Left : SplitSide.Right;

    /// <summary>Each page's width in an even split, with the gap between them.</summary>
    public static double HalfWidth(double width, double gap) => System.Math.Max(0, (width - gap) / 2);

    /// <summary>Where a half starts: the left at the edge, the right just after the gap.</summary>
    public static double HalfOffset(SplitSide side, double width, double gap) =>
        side == SplitSide.Left ? 0 : HalfWidth(width, gap) + gap;

    /// <summary>
    /// Splits saved with the session: a tab marked as split with the one before it is that one's
    /// right-hand page. Returns (left, right) index pairs; a mark with no free tab before it is ignored.
    /// </summary>
    public static System.Collections.Generic.List<(int Left, int Right)> Pairs(System.Collections.Generic.IReadOnlyList<bool> splitWithPrevious)
    {
        var pairs = new System.Collections.Generic.List<(int, int)>();
        var lastPaired = -1;
        for (var i = 1; i < splitWithPrevious.Count; i++)
        {
            if (!splitWithPrevious[i] || i - 1 == lastPaired) continue;
            pairs.Add((i - 1, i));
            lastPaired = i;
        }
        return pairs;
    }

    /// <summary>
    /// While a tab is dragged over a half of the page (as in Arc), the page there makes room: it takes
    /// a half's width and moves to the other half, leaving the one under the pointer empty for the tab.
    /// Null is not over the page: the whole width.
    /// </summary>
    public static double PreviewWidth(SplitSide? side, double width, double gap) =>
        side is null ? width : HalfWidth(width, gap);

    /// <summary>How far right the page sits meanwhile: over to the right half, or not at all.</summary>
    public static double PreviewOffset(SplitSide? side, double width, double gap) =>
        side == SplitSide.Left ? HalfOffset(SplitSide.Right, width, gap) : 0;

    /// <summary>
    /// Where a new split's row goes in the tab list: in place of the higher of its pages' rows there
    /// (rows taken out first), or, when neither page has one (Essentials, pinned and folder tabs keep
    /// their own), at the end.
    /// </summary>
    public static int RowIndex(int? leftAt, int? rightAt, int count) =>
        leftAt is { } l && rightAt is { } r ? System.Math.Min(l, r) : leftAt ?? rightAt ?? count;

    /// <summary>
    /// Saved splits (tab indices and the split's row in the tab list) to put back, top row first so
    /// each lands where it was; any whose tabs are missing, the same, or already in a split are dropped.
    /// </summary>
    public static System.Collections.Generic.List<(int Left, int Right, int Row)> Restorable(
        System.Collections.Generic.IReadOnlyList<(int Left, int Right, int Row)> saved, int tabCount)
    {
        var used = new System.Collections.Generic.HashSet<int>();
        var splits = new System.Collections.Generic.List<(int, int, int)>();
        foreach (var (left, right, row) in System.Linq.Enumerable.OrderBy(saved, s => s.Row))
        {
            if (left == right || left < 0 || right < 0 || left >= tabCount || right >= tabCount) continue;
            if (used.Contains(left) || used.Contains(right)) continue;
            used.Add(left);
            used.Add(right);
            splits.Add((left, right, row));
        }
        return splits;
    }

    /// <summary>
    /// The left page's share of the width with the divider at x, keeping both pages at least
    /// minPane wide; evenly shared when there isn't room for that.
    /// </summary>
    public static double Ratio(double x, double width, double minPane)
    {
        if (width < minPane * 2) return 0.5;
        return System.Math.Clamp(x, minPane, width - minPane) / width;
    }

    /// <summary>How far past a page's minimum the divider can be pulled, at most, in pixels.</summary>
    public const double Stretch = 24;

    /// <summary>
    /// While the divider's dragged: as Ratio, but pulled past a page's minimum it gives a little,
    /// ever less the further it's pulled (approaching Stretch), instead of stopping dead. On letting
    /// go it springs back to Ratio.
    /// </summary>
    public static double Stretched(double x, double width, double minPane)
    {
        if (width < minPane * 2) return 0.5;
        var edge = System.Math.Clamp(x, minPane, width - minPane);
        var past = System.Math.Abs(x - edge);
        return (edge + System.Math.Sign(x - edge) * Stretch * (1 - 1 / (1 + past / Stretch))) / width;
    }
}
