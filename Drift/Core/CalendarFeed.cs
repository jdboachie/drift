using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Drift;

/// <summary>One meeting or event, at one time (a repeating one is one of these per time).</summary>
/// <param name="JoinUrl">Its video call (Meet, Zoom, Teams…), if it has one.</param>
/// <param name="GoogleEid">What Google Calendar calls it in its links (calendar/event?eid=…).</param>
public sealed record CalendarEvent(
    string Uid, string Title, DateTimeOffset Start, DateTimeOffset End, bool AllDay, string? JoinUrl, string? GoogleEid = null)
{
    /// <summary>Its page, where the calendar gives one (Outlook).</summary>
    public string? WebLink { get; init; }
}

/// <summary>
/// Reads iCalendar text (Google Calendar's export) into the events that take place in a stretch of
/// time, repeating ones worked out: daily, weekly, monthly and yearly repeats, with skipped
/// (EXDATE), moved and cancelled (RECURRENCE-ID) times. Events you've declined are left out.
/// </summary>
public static class CalendarFeed
{
    public static List<CalendarEvent> Between(string ics, DateTimeOffset from, DateTimeOffset to, TimeZoneInfo local) =>
        Between([ics], from, to, local);

    public static List<CalendarEvent> Between(IEnumerable<string> calendars, DateTimeOffset from, DateTimeOffset to, TimeZoneInfo local)
    {
        var found = new List<CalendarEvent>();
        foreach (var ics in calendars) found.AddRange(OneCalendar(ics, from, to, local));
        return found.OrderBy(e => e.Start).ThenBy(e => e.End).ToList();
    }

    private sealed class Prop
    {
        public required string Name;
        public required Dictionary<string, string> Params;
        public required string Value;
        public string? Param(string name) => Params.TryGetValue(name, out var v) ? v : null;
    }

    private static IEnumerable<CalendarEvent> OneCalendar(string ics, DateTimeOffset from, DateTimeOffset to, TimeZoneInfo local)
    {
        var lines = Unfold(ics);
        string? owner = null;
        var events = new List<List<Prop>>();
        List<Prop>? current = null;
        var depth = 0;
        foreach (var line in lines)
        {
            if (Parse(line) is not { } p) continue;
            if (p.Name == "BEGIN")
            {
                if (p.Value == "VEVENT" && depth == 0) current = [];
                else if (current is not null) depth++;
                continue;
            }
            if (p.Name == "END")
            {
                if (p.Value == "VEVENT" && depth == 0 && current is not null) { events.Add(current); current = null; }
                else if (depth > 0) depth--;
                continue;
            }
            if (current is null) { if (p.Name == "X-WR-CALNAME" && p.Value.Contains('@')) owner = p.Value; continue; }
            if (depth == 0) current.Add(p);
        }

        // Moved or cancelled times of a repeating event, by its id and the time they replace.
        var overrides = new Dictionary<(string, DateTimeOffset), List<Prop>>();
        foreach (var e in events)
        {
            if (Get(e, "RECURRENCE-ID") is { } rid && Get(e, "UID") is { } uid && When(rid, local) is { } at)
                overrides[(uid.Value, at.Time)] = e;
        }

        foreach (var e in events)
        {
            if (Get(e, "DTSTART") is not { } startProp || When(startProp, local) is not { } start) continue;
            var uid = Get(e, "UID")?.Value ?? "";
            var duration = Length(e, start, local);
            var isOverride = Get(e, "RECURRENCE-ID") is not null;
            if (Cancelled(e) || Declined(e, owner))
            {
                continue;
            }
            if (isOverride || Get(e, "RRULE") is not { } rule)
            {
                var one = Make(e, uid, start.Time, duration, start.AllDay, owner, instance: isOverride ? When(Get(e, "RECURRENCE-ID")!, local)?.Time : null);
                if (Overlaps(one, from, to)) yield return one;
                continue;
            }
            var skipped = new HashSet<DateTimeOffset>();
            foreach (var ex in e.Where(p => p.Name == "EXDATE"))
                foreach (var part in ex.Value.Split(','))
                    if (When(new Prop { Name = ex.Name, Params = ex.Params, Value = part }, local) is { } t) skipped.Add(t.Time);
            foreach (var at in Repeat(rule.Value, start, to, local))
            {
                if (skipped.Contains(at) || overrides.ContainsKey((uid, at))) continue;
                var one = Make(e, uid, at, duration, start.AllDay, owner, instance: at);
                if (Overlaps(one, from, to)) yield return one;
            }
        }
    }

    private static bool Overlaps(CalendarEvent e, DateTimeOffset from, DateTimeOffset to) =>
        e.End > e.Start ? e.End > from && e.Start < to : e.Start >= from && e.Start < to;

    private static CalendarEvent Make(List<Prop> e, string uid, DateTimeOffset start, TimeSpan duration, bool allDay, string? owner, DateTimeOffset? instance)
    {
        var title = Get(e, "SUMMARY")?.Value is { Length: > 0 } s ? Unescape(s) : "(No title)";
        return new CalendarEvent(uid, title, start, start + duration, allDay, JoinLink(e), Eid(uid, instance, allDay, owner, Get(e, "RRULE") is not null || Get(e, "RECURRENCE-ID") is not null));
    }

    private static string? Eid(string uid, DateTimeOffset? instance, bool allDay, string? owner, bool repeating)
    {
        if (owner is null || uid.Length == 0) return null;
        var id = uid.EndsWith("@google.com", StringComparison.OrdinalIgnoreCase) ? uid[..^"@google.com".Length] : uid;
        if (repeating && instance is { } at)
            id += "_" + (allDay ? at.ToString("yyyyMMdd", CultureInfo.InvariantCulture) : at.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(id + " " + owner)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static readonly Regex CallLink = new(
        @"https://[^\s""'<>\\]*(?:meet\.google\.com|zoom\.us|teams\.microsoft\.com|teams\.live\.com|webex\.com|whereby\.com|meet\.jit\.si|chime\.aws|gotomeeting\.com)/[^\s""'<>\\]*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string? JoinLink(List<Prop> e)
    {
        if (Get(e, "X-GOOGLE-CONFERENCE")?.Value is { Length: > 0 } conference) return conference.Trim();
        foreach (var name in new[] { "LOCATION", "DESCRIPTION", "URL" })
            if (Get(e, name)?.Value is { } text && FindCallLink(Unescape(text)) is { } link) return link;
        return null;
    }

    /// <summary>The first video call link (Meet, Zoom, Teams…) in some text.</summary>
    public static string? FindCallLink(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var m = CallLink.Match(text);
        return m.Success ? m.Value.TrimEnd('.', ',', ')', ';') : null;
    }

    private static bool Cancelled(List<Prop> e) => string.Equals(Get(e, "STATUS")?.Value, "CANCELLED", StringComparison.OrdinalIgnoreCase);

    private static bool Declined(List<Prop> e, string? owner)
    {
        if (owner is null) return false;
        return e.Any(p => p.Name == "ATTENDEE"
            && string.Equals(p.Param("PARTSTAT"), "DECLINED", StringComparison.OrdinalIgnoreCase)
            && (string.Equals(p.Value.Replace("mailto:", "", StringComparison.OrdinalIgnoreCase), owner, StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.Param("CN"), owner, StringComparison.OrdinalIgnoreCase)));
    }

    private static TimeSpan Length(List<Prop> e, Stamp start, TimeZoneInfo local)
    {
        if (Get(e, "DTEND") is { } endProp && When(endProp, local) is { } end) return end.Time - start.Time;
        if (Get(e, "DURATION")?.Value is { } d && Duration(d) is { } span) return span;
        return start.AllDay ? TimeSpan.FromDays(1) : TimeSpan.Zero;
    }

    private static TimeSpan? Duration(string text)
    {
        var m = Regex.Match(text.Trim(), @"^([+-])?P(?:(\d+)W)?(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?$");
        if (!m.Success) return null;
        int N(int g) => m.Groups[g].Success ? int.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture) : 0;
        var span = new TimeSpan(N(2) * 7 + N(3), N(4), N(5), N(6));
        return m.Groups[1].Value == "-" ? -span : span;
    }

    // ---- Times ----

    /// <summary>A start or end: the instant, its zone (for repeating it at the same local time), and whether it's a date.</summary>
    private readonly record struct Stamp(DateTimeOffset Time, DateTime Wall, TimeZoneInfo Zone, bool AllDay);

    private static Stamp? When(Prop p, TimeZoneInfo local)
    {
        var v = p.Value.Trim();
        var isDate = p.Param("VALUE") == "DATE" || v.Length == 8;
        if (isDate)
        {
            if (!DateTime.TryParseExact(v, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) return null;
            return new Stamp(At(day, local), day, local, true);
        }
        var utc = v.EndsWith('Z');
        if (!DateTime.TryParseExact(utc ? v[..^1] : v, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var wall)) return null;
        if (utc) return new Stamp(new DateTimeOffset(wall, TimeSpan.Zero), wall, TimeZoneInfo.Utc, false);
        var zone = p.Param("TZID") is { } id && Zone(id) is { } z ? z : local;
        return new Stamp(At(wall, zone), wall, zone, false);
    }

    private static TimeZoneInfo? Zone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id.Trim('"')); }
        catch { return null; }
    }

    /// <summary>A wall-clock time in a zone, as an instant (a time skipped by the clocks going forward moves on an hour).</summary>
    private static DateTimeOffset At(DateTime wall, TimeZoneInfo zone)
    {
        wall = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(wall)) wall = wall.AddHours(1);
        return new DateTimeOffset(wall, zone.GetUtcOffset(wall));
    }

    // ---- Repeats ----

    private static readonly string[] DayCodes = ["SU", "MO", "TU", "WE", "TH", "FR", "SA"];

    /// <summary>Each time a repeating event starts, from its first, until past <paramref name="until"/>.</summary>
    private static IEnumerable<DateTimeOffset> Repeat(string rule, Stamp start, DateTimeOffset until, TimeZoneInfo local)
    {
        var parts = rule.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Split('=', 2)).Where(x => x.Length == 2)
            .ToDictionary(x => x[0].ToUpperInvariant(), x => x[1]);
        var freq = parts.GetValueOrDefault("FREQ", "");
        var interval = int.TryParse(parts.GetValueOrDefault("INTERVAL"), out var i) && i > 0 ? i : 1;
        int? count = int.TryParse(parts.GetValueOrDefault("COUNT"), out var c) ? c : null;
        DateTimeOffset? end = parts.TryGetValue("UNTIL", out var u) && When(new Prop { Name = "UNTIL", Params = [], Value = u }, local) is { } ut
            ? (ut.AllDay ? ut.Time.AddDays(1).AddTicks(-1) : ut.Time) : null;
        var byDay = parts.TryGetValue("BYDAY", out var bd)
            ? bd.Split(',').Select(ParseDay).Where(x => x.Day >= 0).ToList() : [];
        var byMonthDay = parts.TryGetValue("BYMONTHDAY", out var bmd)
            ? bmd.Split(',').Select(x => int.TryParse(x, out var n) ? n : 0).Where(n => n != 0).ToList() : [];
        var weekStart = Array.IndexOf(DayCodes, parts.GetValueOrDefault("WKST", "MO"));
        if (weekStart < 0) weekStart = 1;

        var wall = start.Wall;
        var time = wall.TimeOfDay;
        var emitted = 0;
        for (var period = 0; period < 200_000; period++)
        {
            IEnumerable<DateTime> days = freq switch
            {
                "DAILY" => [wall.Date.AddDays((double)period * interval)],
                "WEEKLY" => WeekDays(wall.Date, period * interval, weekStart, byDay.Count > 0 ? byDay.Select(x => x.Day) : [(int)wall.DayOfWeek]),
                "MONTHLY" => MonthDays(wall.Date.AddMonths(period * interval), wall.Day, byDay, byMonthDay),
                "YEARLY" => YearDay(wall.Date, period * interval),
                _ => [],
            };
            var any = false;
            foreach (var day in days.OrderBy(d => d))
            {
                any = true;
                var w = day + time;
                if (w < wall) continue;
                var at = start.AllDay ? At(w, local) : At(w, start.Zone);
                if (end is { } e && at > e) yield break;
                if (count is { } n && emitted >= n) yield break;
                if (at > until) yield break;
                emitted++;
                yield return at;
            }
            if (!any && freq is not ("MONTHLY" or "YEARLY")) yield break;
        }
    }

    private static (int Nth, int Day) ParseDay(string code)
    {
        code = code.Trim().ToUpperInvariant();
        if (code.Length < 2) return (0, -1);
        var day = Array.IndexOf(DayCodes, code[^2..]);
        var nth = code.Length > 2 && int.TryParse(code[..^2], out var n) ? n : 0;
        return (nth, day);
    }

    private static IEnumerable<DateTime> WeekDays(DateTime first, int weeks, int weekStart, IEnumerable<int> days)
    {
        var offset = ((int)first.DayOfWeek - weekStart + 7) % 7;
        var week = first.AddDays(-offset + 7.0 * weeks);
        foreach (var d in days.Distinct()) yield return week.AddDays((d - weekStart + 7) % 7);
    }

    private static IEnumerable<DateTime> MonthDays(DateTime month, int startDay, List<(int Nth, int Day)> byDay, List<int> byMonthDay)
    {
        var first = new DateTime(month.Year, month.Month, 1);
        var length = DateTime.DaysInMonth(month.Year, month.Month);
        if (byDay.Count > 0)
        {
            foreach (var (nth, day) in byDay)
            {
                var all = Enumerable.Range(0, length).Select(k => first.AddDays(k)).Where(d => (int)d.DayOfWeek == day).ToList();
                if (nth == 0) foreach (var d in all) yield return d;
                else if (nth > 0 && nth <= all.Count) yield return all[nth - 1];
                else if (nth < 0 && -nth <= all.Count) yield return all[all.Count + nth];
            }
            yield break;
        }
        foreach (var n in byMonthDay.Count > 0 ? byMonthDay : [startDay])
        {
            var d = n > 0 ? n : length + n + 1;
            if (d >= 1 && d <= length) yield return first.AddDays(d - 1);
        }
    }

    private static IEnumerable<DateTime> YearDay(DateTime first, int years)
    {
        var year = first.Year + years;
        if (first.Month == 2 && first.Day == 29 && !DateTime.IsLeapYear(year)) yield break;
        yield return new DateTime(year, first.Month, first.Day);
    }

    // ---- Text ----

    private static List<string> Unfold(string ics)
    {
        var lines = new List<string>();
        foreach (var raw in ics.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t') && lines.Count > 0) lines[^1] += raw[1..];
            else lines.Add(raw.TrimEnd('\r'));
        }
        return lines;
    }

    private static Prop? Parse(string line)
    {
        // NAME;PARAM=a;PARAM="b:c":VALUE: the first colon outside quotes ends the name and parameters.
        var quoted = false;
        var colon = -1;
        for (var k = 0; k < line.Length; k++)
        {
            if (line[k] == '"') quoted = !quoted;
            else if (line[k] == ':' && !quoted) { colon = k; break; }
        }
        if (colon <= 0) return null;
        var head = line[..colon].Split(';');
        var ps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var x in head.Skip(1))
        {
            var eq = x.IndexOf('=');
            if (eq > 0) ps[x[..eq]] = x[(eq + 1)..].Trim('"');
        }
        return new Prop { Name = head[0].ToUpperInvariant(), Params = ps, Value = line[(colon + 1)..] };
    }

    private static Prop? Get(List<Prop> e, string name) => e.FirstOrDefault(p => p.Name == name);

    private static string Unescape(string s) =>
        s.Replace("\\n", "\n").Replace("\\N", "\n").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\");
}
