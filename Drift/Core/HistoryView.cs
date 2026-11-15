using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Drift;

/// <summary>When it was visited, in the history filter: any time, or since the start of today, the week or the month.</summary>
public enum HistoryRange { All, Today, Week, Month }

/// <summary>
/// How the history list is sorted: a group per day, or per site (the latest visited first); or one
/// list, the most visited first, or the latest.
/// </summary>
public enum HistorySort { Date, Site, MostVisited, LastVisited }

/// <summary>What the history list looks through: the pages visited, or the tabs closed.</summary>
public enum HistorySource { History, ClosedTabs }

/// <summary>The history filter, as Zen has it. The default is every page, by day.</summary>
public readonly record struct HistoryFilter(
    HistoryRange Range = HistoryRange.All, HistorySort Sort = HistorySort.Date, HistorySource Source = HistorySource.History)
{
    public bool IsDefault => this == default;

    /// <summary>Where a range starts (local time), or null for any time.</summary>
    public static DateTime? Since(HistoryRange range, DateTime now, DayOfWeek firstDayOfWeek) => range switch
    {
        HistoryRange.Today => now.Date,
        HistoryRange.Week => now.Date.AddDays(-(((int)now.DayOfWeek - (int)firstDayOfWeek + 7) % 7)),
        HistoryRange.Month => new DateTime(now.Year, now.Month, 1),
        _ => null,
    };
}

/// <summary>
/// The history list: the pages matching a search, newest first, a group per day, so many at a time.
/// No UI here, so it can be tested.
/// </summary>
public static class HistoryView
{
    /// <summary>A group of the list, under its heading; a list with no groups is one with no heading (null).</summary>
    public sealed record Day<T>(string? Name, IReadOnlyList<T> Entries);

    public sealed record Result<T>(IReadOnlyList<Day<T>> Days, bool HasMore);

    /// <summary>One line of the list: a day's heading (Day) or a page (Entry).</summary>
    public sealed record Line<T>(string? Day, T? Entry, bool FirstDay = false);

    /// <summary>The days as one list, heading then pages, for a list that only builds what's on screen.</summary>
    public static IReadOnlyList<Line<T>> Lines<T>(Result<T> found)
    {
        var lines = new List<Line<T>>();
        foreach (var day in found.Days)
        {
            if (day.Name is { } name) lines.Add(new Line<T>(name, default, FirstDay: lines.Count == 0));
            foreach (var entry in day.Entries) lines.Add(new Line<T>(null, entry));
        }
        return lines;
    }

    /// <param name="visited">When each was last visited, in local time.</param>
    /// <param name="query">Words that must each be in the title or the address.</param>
    /// <param name="since">Only those visited since then (HistoryFilter.Since).</param>
    /// <param name="visits">How many times each was visited, for the most visited first.</param>
    public static Result<T> Find<T>(IEnumerable<T> entries, Func<T, string> title, Func<T, string> url,
        Func<T, DateTime> visited, string query, int limit, DateTime today,
        HistorySort sort = HistorySort.Date, DateTime? since = null, Func<T, int>? visits = null)
    {
        var words = query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matching = entries
            .Where(e => since is not { } start || visited(e) >= start)
            .Where(e => words.All(w => title(e).Contains(w, StringComparison.OrdinalIgnoreCase)
                                    || url(e).Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(visited)
            .ToList();
        List<T> ordered = sort switch
        {
            // Each site where its latest visit puts it, its pages together.
            HistorySort.Site => matching.GroupBy(e => Site(url(e))).SelectMany(g => g).ToList(),
            HistorySort.MostVisited => matching.OrderByDescending(e => visits?.Invoke(e) ?? 0).ToList(),
            _ => matching,
        };
        var shown = ordered.Take(limit).ToList();
        var days = sort switch
        {
            HistorySort.Date => Groups(shown, e => DayName(visited(e).Date, today)),
            HistorySort.Site => Groups(shown, e => Site(url(e))),
            _ => shown.Count == 0 ? [] : [new Day<T>(null, shown)],
        };
        return new Result<T>(days, ordered.Count > limit);
    }

    /// <summary>Runs of entries with the same heading, in order.</summary>
    private static List<Day<T>> Groups<T>(List<T> entries, Func<T, string> heading)
    {
        var days = new List<Day<T>>();
        string? name = null;
        List<T>? group = null;
        foreach (var entry in entries)
        {
            var its = heading(entry);
            if (group is null || its != name)
            {
                name = its;
                group = [];
                days.Add(new Day<T>(its, group));
            }
            group.Add(entry);
        }
        return days;
    }

    /// <summary>The site a page is on, as the list shows it: its host, without www.</summary>
    public static string Site(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Host.Length == 0) return url;
        return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
    }

    public static string DayName(DateTime day, DateTime today)
    {
        // A closed tab from before closing times were kept.
        if (day.Date == DateTime.MinValue.Date) return "Earlier";
        if (day.Date == today.Date) return "Today";
        if (day.Date == today.Date.AddDays(-1)) return "Yesterday";
        return day.ToString(day.Year == today.Year ? "dddd d MMMM" : "dddd d MMMM yyyy", CultureInfo.CurrentCulture);
    }
}
