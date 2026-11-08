using System;
using System.Collections.Generic;
using System.Linq;

namespace Drift;

/// <summary>
/// When meetings count down, can be joined and are reminded of, and how a day's meetings sit
/// side by side, for the Google Calendar integration (GoogleCalendar.cs).
/// </summary>
public static class MeetingClock
{
    /// <summary>How long before the start Join shows.</summary>
    public static readonly TimeSpan JoinEarly = TimeSpan.FromMinutes(5);

    /// <summary>How far ahead the calendar tile counts down.</summary>
    public static readonly TimeSpan Horizon = TimeSpan.FromHours(1);

    /// <summary>How long after its start a meeting stays on the tile ("2m ago").</summary>
    public static readonly TimeSpan Late = TimeSpan.FromMinutes(10);

    /// <summary>How long before the start the reminder comes.</summary>
    public static readonly TimeSpan RemindEarly = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The meeting the calendar tile is about: one starting within the hour, or started in the last
    /// ten minutes. Of those, the latest to start by five minutes from now, so one about to start
    /// takes over from one already going; else the soonest.
    /// </summary>
    public static CalendarEvent? Upcoming(IEnumerable<CalendarEvent> events, DateTimeOffset now)
    {
        var near = events.Where(e => !e.AllDay && now < e.End && now < e.Start + Late && e.Start - now <= Horizon).ToList();
        return near.Where(e => e.Start <= now + JoinEarly).OrderByDescending(e => e.Start).FirstOrDefault()
            ?? near.OrderBy(e => e.Start).FirstOrDefault();
    }

    /// <summary>"in 12m", "in 1h 30m", "now" (its first minute), "2m ago".</summary>
    public static string Countdown(CalendarEvent e, DateTimeOffset now)
    {
        var left = e.Start - now;
        if (left > TimeSpan.Zero)
        {
            var minutes = (int)Math.Ceiling(left.TotalMinutes);
            if (minutes < 60) return $"in {minutes}m";
            return minutes % 60 == 0 ? $"in {minutes / 60}h" : $"in {minutes / 60}h {minutes % 60}m";
        }
        var ago = (int)Math.Floor(-left.TotalMinutes);
        return ago == 0 ? "now" : $"{ago}m ago";
    }

    /// <summary>It has a call, from five minutes before it starts until it ends.</summary>
    public static bool CanJoin(CalendarEvent e, DateTimeOffset now) =>
        e.JoinUrl is not null && now >= e.Start - JoinEarly && now < e.End;

    /// <summary>Same meeting, same time: reminded once.</summary>
    public static string Key(CalendarEvent e) => $"{e.Uid}|{e.Start.UtcTicks}";

    /// <summary>Meetings to remind of now: from a minute before they start until five minutes in, if not already.</summary>
    public static List<CalendarEvent> DueReminders(IEnumerable<CalendarEvent> events, DateTimeOffset now, ISet<string> shown) =>
        events.Where(e => !e.AllDay && now >= e.Start - RemindEarly && now < e.Start + JoinEarly && now < e.End && !shown.Contains(Key(e)))
            .OrderBy(e => e.Start).ToList();

    /// <summary>
    /// For a day's timeline: meetings that overlap share the width, each in its own lane; Lanes is
    /// how many the group it's in needs.
    /// </summary>
    public static Dictionary<CalendarEvent, (int Lane, int Lanes)> Lanes(IEnumerable<CalendarEvent> events)
    {
        var result = new Dictionary<CalendarEvent, (int, int)>(ReferenceEqualityComparer.Instance);
        var group = new List<(CalendarEvent E, int Lane)>();
        var laneEnds = new List<DateTimeOffset>();
        var groupEnd = DateTimeOffset.MinValue;

        void Close()
        {
            foreach (var (e, lane) in group) result[e] = (lane, laneEnds.Count);
            group.Clear();
            laneEnds.Clear();
        }

        foreach (var e in events.OrderBy(e => e.Start).ThenByDescending(e => e.End))
        {
            if (group.Count > 0 && e.Start >= groupEnd) Close();
            var lane = laneEnds.FindIndex(end => end <= e.Start);
            if (lane < 0) { lane = laneEnds.Count; laneEnds.Add(e.End); }
            else laneEnds[lane] = e.End;
            group.Add((e, lane));
            if (group.Count == 1 || e.End > groupEnd) groupEnd = e.End;
        }
        Close();
        return result;
    }
}
