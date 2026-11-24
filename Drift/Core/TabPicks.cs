using System.Collections.Generic;
using System.Linq;

namespace Drift;

/// <summary>
/// Tabs picked out in the sidebar to act on together (their right-click menu: a new folder with them), as files
/// are in File Explorer: Ctrl+click adds a tab or takes it out again, and Shift+click picks every tab from the
/// last one clicked to this one, in the sidebar's order. The tab in front counts as picked until others are.
/// No UI here, so it can be tested.
/// </summary>
public static class TabPicks
{
    /// <summary>Ctrl+click: the tab joins the picks, or leaves them.</summary>
    /// <param name="picked">The tabs picked so far; none yet stands for the tab in front.</param>
    /// <param name="front">The tab in front, if it can be picked (a row, not an Essential's tile).</param>
    public static List<T> Toggle<T>(IReadOnlyList<T> picked, T clicked, T? front) where T : class
    {
        var now = picked.Count > 0 ? picked.ToList() : front is null ? [] : [front];
        if (!now.Remove(clicked)) now.Add(clicked);
        return now;
    }

    /// <summary>Shift+click: every tab from the anchor (the last one clicked, else the tab in front) to this one.</summary>
    /// <param name="order">The tabs that can be picked, in the sidebar's order.</param>
    public static List<T> Range<T>(IReadOnlyList<T> order, T? anchor, T clicked) where T : class
    {
        var list = order.ToList();
        var to = list.IndexOf(clicked);
        if (to < 0) return [];
        var from = anchor is null ? -1 : list.IndexOf(anchor);
        if (from < 0) return [clicked];
        var (first, last) = from <= to ? (from, to) : (to, from);
        return list.GetRange(first, last - first + 1);
    }

    /// <summary>The picks in the sidebar's order, as they go into a folder.</summary>
    public static List<T> InOrder<T>(IReadOnlyList<T> picked, IReadOnlyList<T> order) where T : class =>
        order.Where(picked.Contains).ToList();
}
