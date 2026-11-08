using System;
using System.Collections.Generic;
using System.Linq;

namespace Drift;

/// <summary>
/// Ctrl+Tab's order, as in Arc: the tab you're on, then the others from the one used last back
/// (TabSwitcher.cs shows them). No UI here, so it can be tested.
/// </summary>
public static class TabCycle
{
    /// <summary>
    /// The current tab, then the rest most recently used first; tabs not visited since Drift started
    /// follow, in sidebar order. Tabs no longer in the sidebar are left out, as are those with no page
    /// loaded (an Essential or pinned tab not opened yet), as in Arc; the current tab is always in.
    /// </summary>
    public static List<T> Order<T>(IReadOnlyList<T> recent, IReadOnlyList<T> sidebar, T? current, Func<T, bool>? loaded = null) where T : class
    {
        var open = new HashSet<T>(sidebar);
        var order = new List<T>();
        if (current is not null && open.Contains(current)) order.Add(current);
        foreach (var tab in recent.Concat(sidebar))
            if (open.Contains(tab) && !order.Contains(tab) && (loaded is null || loaded(tab))) order.Add(tab);
        return order;
    }

    /// <summary>Where the first press lands: forward on the tab used last, back on the one used longest ago.</summary>
    public static int Start(int step, int count) => count <= 1 ? 0 : step > 0 ? 1 : count - 1;

    /// <summary>The next one along, round the ends.</summary>
    public static int Step(int at, int step, int count) => count <= 0 ? 0 : ((at + step) % count + count) % count;
}
