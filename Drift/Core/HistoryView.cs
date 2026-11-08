using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Drift;

/// <summary>
/// The history list: the pages matching a search, newest first, a group per day, so many at a time.
/// No UI here, so it can be tested.
/// </summary>
public static class HistoryView
{
    public sealed record Day<T>(string Name, IReadOnlyList<T> Entries);

    public sealed record Result<T>(IReadOnlyList<Day<T>> Days, bool HasMore);

    /// <summary>One line of the list: a day's heading (Day) or a page (Entry).</summary>
    public sealed record Line<T>(string? Day, T? Entry, bool FirstDay = false);

    /// <summary>The days as one list, heading then pages, for a list that only builds what's on screen.</summary>
    public static IReadOnlyList<Line<T>> Lines<T>(Result<T> found)
    {
        var lines = new List<Line<T>>();
        foreach (var day in found.Days)
        {
            lines.Add(new Line<T>(day.Name, default, FirstDay: lines.Count == 0));
            foreach (var entry in day.Entries) lines.Add(new Line<T>(null, entry));
        }
        return lines;
    }

    /// <param name="visited">When each was last visited, in local time.</param>
    /// <param name="query">Words that must each be in the title or the address.</param>
    public static Result<T> Find<T>(IEnumerable<T> entries, Func<T, string> title, Func<T, string> url,
        Func<T, DateTime> visited, string query, int limit, DateTime today)
    {
        var words = query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matching = entries
            .Where(e => words.All(w => title(e).Contains(w, StringComparison.OrdinalIgnoreCase)
                                    || url(e).Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(visited)
            .ToList();
        var days = matching.Take(limit)
            .GroupBy(e => visited(e).Date)
            .Select(g => new Day<T>(DayName(g.Key, today), g.ToList()))
            .ToList();
        return new Result<T>(days, matching.Count > limit);
    }

    public static string DayName(DateTime day, DateTime today)
    {
        if (day.Date == today.Date) return "Today";
        if (day.Date == today.Date.AddDays(-1)) return "Yesterday";
        return day.ToString(day.Year == today.Year ? "dddd d MMMM" : "dddd d MMMM yyyy", CultureInfo.CurrentCulture);
    }
}
