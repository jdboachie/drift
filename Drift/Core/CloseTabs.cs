using System;
using System.Collections.Generic;
using System.Linq;

namespace Drift;

/// <summary>Which rows of the tab list a tab's Close tabs › closes (a split's row is one row).</summary>
public static class CloseTabs
{
    public static IReadOnlyList<int> Below(int count, int index) =>
        Enumerable.Range(index + 1, Math.Max(0, count - index - 1)).ToList();

    public static IReadOnlyList<int> Others(int count, int index) =>
        Enumerable.Range(0, count).Where(i => i != index).ToList();

    /// <summary>
    /// Rows whose page is open already: earlier in the list, or among <paramref name="kept"/> (pinned
    /// tabs' pages, split pages), which stay. The tab you're on (<paramref name="front"/>) stays, and
    /// an earlier twin of it goes instead.
    /// </summary>
    /// <param name="rows">Each row's page, compared as given; null for a row without one of its own.</param>
    public static IReadOnlyList<int> Duplicates(IReadOnlyList<string?> rows, IReadOnlyCollection<string> kept, int? front)
    {
        var open = new HashSet<string>(kept, StringComparer.OrdinalIgnoreCase);
        if (front is { } f && f >= 0 && f < rows.Count && rows[f] is { } frontPage) open.Add(frontPage);
        var close = new List<int>();
        for (var i = 0; i < rows.Count; i++)
        {
            if (i == front || rows[i] is not { } page) continue;
            if (!open.Add(page)) close.Add(i);
        }
        return close;
    }
}
