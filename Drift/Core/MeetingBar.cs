using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Drift;

/// <summary>
/// The meeting card at the foot of the sidebar (MeetingCard.cs): a row for the next meeting from up to
/// an hour before (MeetingClock.Upcoming), which grows into a card with when, the call and who's
/// coming. It opens itself ten minutes before. Collapsed back to the row, or closed, it stays that way
/// until five minutes before, when it's back, open, with Join.
/// </summary>
public static class MeetingBar
{
    /// <summary>How long before the start the card opens itself.</summary>
    public static readonly TimeSpan OpenEarly = TimeSpan.FromMinutes(10);

    /// <summary>How many faces show before the rest are a count ("+3").</summary>
    public const int Faces = 3;

    /// <summary>The number of face colours (DriftFace0Brush…).</summary>
    public const int FaceColors = 6;

    /// <summary>Collapsed (to the row) or closed (gone), for one meeting, at a time.</summary>
    public sealed record Away(string Key, bool Closed, DateTimeOffset At);

    /// <summary>From ten minutes before until it ends, the card opens itself.</summary>
    public static bool OpensItself(CalendarEvent e, DateTimeOffset now) => now >= e.Start - OpenEarly && now < e.End;

    /// <summary>
    /// Still collapsed or closed: put away for this meeting, and not yet back. Put away before Join
    /// shows, it's back when Join does; put away after, it stays away.
    /// </summary>
    public static bool StillAway(Away? away, CalendarEvent e, DateTimeOffset now)
    {
        if (away is null || away.Key != MeetingClock.Key(e)) return false;
        var joinFrom = e.Start - MeetingClock.JoinEarly;
        return !(away.At < joinFrom && now >= joinFrom);
    }

    /// <summary>The row's countdown: "8m", "1h 5m", "now", "2m ago".</summary>
    public static string Short(CalendarEvent e, DateTimeOffset now)
    {
        var text = MeetingClock.Countdown(e, now);
        return text.StartsWith("in ", StringComparison.Ordinal) ? text[3..] : text;
    }

    /// <summary>The card's countdown: "In 8 min", "In 1 hr 5 min", "Starting now", "Started 2 min ago".</summary>
    public static string Soon(CalendarEvent e, DateTimeOffset now)
    {
        var left = e.Start - now;
        if (left > TimeSpan.Zero)
        {
            var minutes = (int)Math.Ceiling(left.TotalMinutes);
            if (minutes < 60) return $"In {minutes} min";
            return minutes % 60 == 0 ? $"In {minutes / 60} hr" : $"In {minutes / 60} hr {minutes % 60} min";
        }
        var ago = (int)Math.Floor(-left.TotalMinutes);
        return ago == 0 ? "Starting now" : $"Started {ago} min ago";
    }

    /// <summary>"2:00 – 2:30 PM": the start without its AM or PM when the end has the same one.</summary>
    public static string Hours(CalendarEvent e, TimeZoneInfo zone, CultureInfo culture)
    {
        var start = TimeZoneInfo.ConvertTime(e.Start, zone).DateTime;
        var end = TimeZoneInfo.ConvertTime(e.End, zone).DateTime;
        var pattern = culture.DateTimeFormat.ShortTimePattern;
        var from = start.ToString(pattern, culture);
        if (pattern.Contains("tt", StringComparison.Ordinal)
            && start.ToString("tt", culture) == end.ToString("tt", culture)
            && start.ToString("tt", culture).Length > 0)
            from = start.ToString(pattern.Replace("tt", "").Trim(), culture);
        return $"{from} – {end.ToString(pattern, culture)}";
    }

    /// <summary>What the call's on, by its link: "Google Meet", "Zoom", "Microsoft Teams"…, or its site.</summary>
    public static string? CallName(string? joinUrl)
    {
        if (!Uri.TryCreate(joinUrl, UriKind.Absolute, out var url)) return null;
        var host = url.Host.ToLowerInvariant();
        bool On(string site) => host == site || host.EndsWith("." + site, StringComparison.Ordinal);
        if (On("meet.google.com")) return "Google Meet";
        if (On("zoom.us")) return "Zoom";
        if (On("teams.microsoft.com") || On("teams.live.com")) return "Microsoft Teams";
        if (On("webex.com")) return "Webex";
        if (On("whereby.com")) return "Whereby";
        if (On("meet.jit.si")) return "Jitsi Meet";
        if (On("chime.aws")) return "Amazon Chime";
        if (On("gotomeeting.com")) return "GoTo Meeting";
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    /// <summary>"6 people". Shown only with someone besides you, so never fewer than two.</summary>
    public static string PeopleText(int count) => $"{count} people";

    /// <summary>
    /// The faces in a row: each person's initial and colour, up to three, then "+N" for the rest
    /// (four people show all four, rather than three and "+1").
    /// </summary>
    public static List<(string Text, int Color)> FacesOf(IReadOnlyList<string> people)
    {
        var shown = people.Count > Faces + 1 ? people.Take(Faces) : people;
        var faces = shown.Select(p => (Initial(p), ColorOf(p))).ToList();
        if (people.Count > Faces + 1) faces.Add(($"+{people.Count - Faces}", -1));
        return faces;
    }

    /// <summary>The first letter of a name, as a capital.</summary>
    public static string Initial(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return "?";
        var first = StringInfo.GetNextTextElementLength(trimmed);
        return trimmed[..first].ToUpper(CultureInfo.CurrentCulture);
    }

    /// <summary>A person's colour, the same every time for the same name.</summary>
    public static int ColorOf(string name)
    {
        var hash = 0;
        foreach (var c in name.Trim().ToLowerInvariant()) hash = unchecked(hash * 31 + c);
        return (int)((uint)hash % FaceColors);
    }
}
