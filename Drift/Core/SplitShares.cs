using System;
using System.Collections.Generic;
using System.Linq;

namespace Drift;

/// <summary>
/// A split's pages side by side, two to four of them (as Zen and Arc allow), each with its share of the
/// width (shares add up to 1, the gaps between pages left out). A page joining takes an even share,
/// the others giving up theirs in proportion; one leaving gives its share back the same way (Zen's
/// rule). A divider shares the width of only the two pages beside it.
/// </summary>
public static class SplitShares
{
    public const int MaxPages = 4;

    public static double[] Even(int pages) => Enumerable.Repeat(1.0 / pages, pages).ToArray();

    /// <summary>A page joins at <paramref name="at"/>, with 1/(n+1) of the width; the others keep their proportions.</summary>
    public static double[] Insert(IReadOnlyList<double> shares, int at)
    {
        var n = shares.Count;
        var keep = n / (n + 1.0);
        var result = shares.Select(s => s * keep).ToList();
        result.Insert(Math.Clamp(at, 0, n), 1.0 / (n + 1));
        return result.ToArray();
    }

    /// <summary>The page at <paramref name="at"/> leaves; the others grow in proportion to fill its place.</summary>
    public static double[] Remove(IReadOnlyList<double> shares, int at)
    {
        var rest = shares.Where((_, i) => i != at).ToArray();
        var total = rest.Sum();
        return total > 0 ? rest.Select(s => s / total).ToArray() : Even(rest.Length);
    }

    /// <summary>The item at <paramref name="from"/> moved to <paramref name="to"/>, the rest keeping their order.</summary>
    public static T[] Moved<T>(IReadOnlyList<T> items, int from, int to)
    {
        var list = items.ToList();
        var item = list[from];
        list.RemoveAt(from);
        list.Insert(Math.Clamp(to, 0, list.Count), item);
        return list.ToArray();
    }

    /// <summary>Each page's width in pixels, for the whole width with a gap between each two.</summary>
    public static double[] Widths(IReadOnlyList<double> shares, double width, double gap)
    {
        var room = Math.Max(0, width - gap * (shares.Count - 1));
        return shares.Select(s => s * room).ToArray();
    }

    /// <summary>Where each page starts, for those widths.</summary>
    public static double[] Lefts(IReadOnlyList<double> widths, double gap)
    {
        var lefts = new double[widths.Count];
        for (var i = 1; i < widths.Count; i++) lefts[i] = lefts[i - 1] + widths[i - 1] + gap;
        return lefts;
    }

    /// <summary>
    /// With divider <paramref name="divider"/> (between page divider and divider+1) dragged to x in the
    /// page area: those two pages share their width afresh, each at least minPane wide (stretched, it
    /// gives a little past that with resistance, as SplitLayout.Stretched); the others are untouched.
    /// </summary>
    public static double[] Resized(IReadOnlyList<double> shares, int divider, double x, double width, double gap, double minPane, bool stretch)
    {
        var widths = Widths(shares, width, gap);
        var start = Lefts(widths, gap)[divider];
        var pair = widths[divider] + widths[divider + 1];
        var local = x - start - gap / 2;
        var ratio = stretch ? SplitLayout.Stretched(local, pair, minPane) : SplitLayout.Ratio(local, pair, minPane);
        var both = shares[divider] + shares[divider + 1];
        var result = shares.ToArray();
        result[divider] = both * ratio;
        result[divider + 1] = both * (1 - ratio);
        return result;
    }

    /// <summary>Every page at least minPane wide again, after being let go stretched: as Resized, unstretched, at each divider.</summary>
    public static double[] Inside(IReadOnlyList<double> shares, double width, double gap, double minPane)
    {
        var result = shares.ToArray();
        for (var d = 0; d < result.Length - 1; d++)
        {
            var widths = Widths(result, width, gap);
            var divider = Lefts(widths, gap)[d] + widths[d] + gap / 2;
            result = Resized(result, d, divider, width, gap, minPane, stretch: false);
        }
        return result;
    }

    /// <summary>The two pages beside a divider, half and half of their width (double-click the divider).</summary>
    public static double[] Evened(IReadOnlyList<double> shares, int divider)
    {
        var result = shares.ToArray();
        result[divider] = result[divider + 1] = (shares[divider] + shares[divider + 1]) / 2;
        return result;
    }

    /// <summary>Shares as saved, made usable: one per page, none negative, adding up to 1; even otherwise.</summary>
    public static double[] Checked(IReadOnlyList<double>? saved, int pages)
    {
        if (saved is null || saved.Count != pages || saved.Any(s => !(s > 0)) ) return Even(pages);
        var total = saved.Sum();
        return saved.Select(s => s / total).ToArray();
    }

    /// <summary>
    /// Saved splits (tab indices, left to right, and the split's row in the tab list) to put back, top
    /// row first so each lands where it was. A tab that's missing or already in a split is left out; a
    /// split left with fewer than two pages is dropped, and one with more than four keeps the first four.
    /// </summary>
    /// <returns>Each with its place in <paramref name="saved"/>, and the pages kept.</returns>
    public static List<(int Index, int[] Pages, int Row)> Restorable(IReadOnlyList<(int[] Pages, int Row)> saved, int tabCount)
    {
        var used = new HashSet<int>();
        var splits = new List<(int, int[], int)>();
        foreach (var (index, (pages, row)) in saved.Select((s, i) => (i, s)).OrderBy(s => s.s.Row))
        {
            var kept = pages.Where(p => p >= 0 && p < tabCount).Distinct().Where(p => !used.Contains(p)).Take(MaxPages).ToArray();
            if (kept.Length < 2) continue;
            used.UnionWith(kept);
            splits.Add((index, kept, row));
        }
        return splits;
    }
}
