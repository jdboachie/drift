using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Drift;

/// <summary>
/// The downloads that show when you hover the Library button, as in Arc: the newest few you can still
/// open or drag somewhere (finished, and the file's still there), or that are on their way.
/// </summary>
public static class DownloadPeek
{
    public const int Count = 3;

    /// <summary>The newest <see cref="Count"/> of <paramref name="newestFirst"/> worth showing.</summary>
    public static IReadOnlyList<T> Pick<T>(IEnumerable<T> newestFirst, Func<T, (bool Running, bool Saved, bool OnDisk)> state) =>
        Pick(newestFirst, state, _ => true);

    /// <summary>The same, of only those <paramref name="shows"/> lets through (the kinds of file you chose).</summary>
    public static IReadOnlyList<T> Pick<T>(IEnumerable<T> newestFirst, Func<T, (bool Running, bool Saved, bool OnDisk)> state, Func<T, bool> shows) =>
        newestFirst.Where(item => state(item) is var (running, saved, onDisk) && (running || saved && onDisk) && shows(item)).Take(Count).ToList();

    /// <summary>When a download finished, as a row says it: "3 minutes ago", "Yesterday", "6 Nov".</summary>
    public static string Ago(DateTime when, DateTime now)
    {
        var ago = now - when;
        if (ago.TotalMinutes < 1) return "Just now";
        if (ago.TotalHours < 1) return Plural((int)ago.TotalMinutes, "minute") + " ago";
        // Late last night is still "2 hours ago" just after midnight, but "Yesterday" by morning.
        if (ago.TotalHours < 6 || when.Date == now.Date) return Plural((int)ago.TotalHours, "hour") + " ago";
        var days = (now.Date - when.Date).Days;
        if (days == 1) return "Yesterday";
        if (days < 7) return $"{days} days ago";
        return when.ToString(when.Year == now.Year ? "d MMM" : "d MMM yyyy", CultureInfo.InvariantCulture);
    }

    private static string Plural(int n, string unit) => n == 1 ? $"1 {unit}" : $"{n} {unit}s";
}
