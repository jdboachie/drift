using System;
using System.Text.RegularExpressions;

namespace Drift;

/// <summary>Google Calendar's addresses, for the account a pinned calendar tab is signed into (…/calendar/u/1/…).</summary>
public static class GoogleCalendarLinks
{
    public const string Host = "calendar.google.com";

    /// <summary>A Google Calendar page.</summary>
    public static bool IsCalendar(Uri? uri) => uri is not null && string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase);

    /// <summary>Which of the signed-in Google accounts the page is for: the n in /calendar/u/n/, else the first.</summary>
    public static int Account(Uri? uri) =>
        uri is not null && Regex.Match(uri.AbsolutePath, @"/u/(\d+)(/|$)") is { Success: true } m && int.TryParse(m.Groups[1].Value, out var n) ? n : 0;

    /// <summary>The account's calendars, all of them, as iCalendar files in a zip (Settings › Export).</summary>
    public static string ExportPath(int account) => $"/calendar/u/{account}/exporticalzip";

    /// <summary>Somewhere tiny on the calendar's site, where its export can be asked for as the signed-in page would.</summary>
    public static Uri Base => new($"https://{Host}/robots.txt");

    public static Uri Event(string eid, int account) => new($"https://{Host}/calendar/u/{account}/r/eventedit/{eid}");

    public static Uri NewEvent(int account) => new($"https://{Host}/calendar/u/{account}/r/eventedit");
}
