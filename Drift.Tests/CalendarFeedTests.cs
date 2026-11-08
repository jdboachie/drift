using Drift;
using Xunit;

namespace Drift.Tests;

public class CalendarFeedTests
{
    private static readonly TimeZoneInfo Accra = TimeZoneInfo.Utc;
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private static string Ics(params string[] events) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nX-WR-CALNAME:me@example.com\r\n"
        + string.Concat(events.Select(e => "BEGIN:VEVENT\r\n" + e.Trim().Replace("\n", "\r\n") + "\r\nEND:VEVENT\r\n"))
        + "END:VCALENDAR\r\n";

    private static DateTimeOffset Utc(int y, int mo, int d, int h = 0, int mi = 0) => new(y, mo, d, h, mi, 0, TimeSpan.Zero);

    private static List<CalendarEvent> Day(string ics, int y, int mo, int d, TimeZoneInfo? zone = null) =>
        CalendarFeed.Between(ics, Utc(y, mo, d), Utc(y, mo, d).AddDays(1), zone ?? Accra);

    [Fact]
    public void A_one_off_meeting_that_day()
    {
        var ics = Ics("""
            UID:a@google.com
            SUMMARY:Standup
            DTSTART:20261109T090000Z
            DTEND:20261109T091500Z
            """);
        var e = Assert.Single(Day(ics, 2026, 11, 9));
        Assert.Equal("Standup", e.Title);
        Assert.Equal(Utc(2026, 11, 9, 9), e.Start);
        Assert.Equal(Utc(2026, 11, 9, 9, 15), e.End);
        Assert.False(e.AllDay);
        Assert.Empty(Day(ics, 2026, 11, 10));
    }

    [Fact]
    public void Folded_lines_and_escapes_are_read()
    {
        var ics = Ics("UID:a\nSUMMARY:Plan\\, review \\; ship\n  it\nDTSTART:20261109T090000Z\nDTEND:20261109T100000Z");
        Assert.Equal("Plan, review ; ship it", Assert.Single(Day(ics, 2026, 11, 9)).Title);
    }

    [Fact]
    public void Times_in_a_named_zone()
    {
        var ics = Ics("""
            UID:a
            SUMMARY:Call
            DTSTART;TZID=America/New_York:20261109T090000
            DTEND;TZID=America/New_York:20261109T093000
            """);
        Assert.Equal(Utc(2026, 11, 9, 14), Assert.Single(Day(ics, 2026, 11, 9)).Start);
    }

    [Fact]
    public void All_day_events_cover_the_local_day()
    {
        var ics = Ics("""
            UID:a
            SUMMARY:Holiday
            DTSTART;VALUE=DATE:20261109
            DTEND;VALUE=DATE:20261110
            """);
        var e = Assert.Single(CalendarFeed.Between(ics, Utc(2026, 11, 9, 12), Utc(2026, 11, 9, 13), NewYork));
        Assert.True(e.AllDay);
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.FromHours(-5)), e.Start);
    }

    [Fact]
    public void A_missing_end_is_worked_out()
    {
        var ics = Ics("UID:a\nSUMMARY:Quick\nDTSTART:20261109T090000Z\nDURATION:PT1H30M");
        Assert.Equal(Utc(2026, 11, 9, 10, 30), Assert.Single(Day(ics, 2026, 11, 9)).End);
    }

    [Fact]
    public void Weekly_on_given_days_until_a_date()
    {
        var ics = Ics("""
            UID:w
            SUMMARY:Sync
            DTSTART:20261102T090000Z
            DTEND:20261102T093000Z
            RRULE:FREQ=WEEKLY;WKST=MO;UNTIL=20261120T000000Z;INTERVAL=1;BYDAY=MO,WE
            """);
        Assert.Single(Day(ics, 2026, 11, 2));   // Monday
        Assert.Single(Day(ics, 2026, 11, 4));   // Wednesday
        Assert.Empty(Day(ics, 2026, 11, 5));    // Thursday
        Assert.Equal(Utc(2026, 11, 16, 9), Assert.Single(Day(ics, 2026, 11, 16)).Start);
        Assert.Empty(Day(ics, 2026, 11, 23));   // after UNTIL
    }

    [Fact]
    public void Every_other_week()
    {
        var ics = Ics("UID:w\nSUMMARY:1:1\nDTSTART:20261102T090000Z\nDTEND:20261102T093000Z\nRRULE:FREQ=WEEKLY;INTERVAL=2;BYDAY=MO");
        Assert.Empty(Day(ics, 2026, 11, 9));
        Assert.Single(Day(ics, 2026, 11, 16));
    }

    [Fact]
    public void Daily_for_a_count()
    {
        var ics = Ics("UID:d\nSUMMARY:Sprint\nDTSTART:20261101T080000Z\nDTEND:20261101T081500Z\nRRULE:FREQ=DAILY;COUNT=3");
        Assert.Single(Day(ics, 2026, 11, 3));
        Assert.Empty(Day(ics, 2026, 11, 4));
    }

    [Fact]
    public void Monthly_on_the_same_date_and_on_the_nth_weekday()
    {
        var sameDate = Ics("UID:m\nSUMMARY:Rent\nDTSTART:20260115T100000Z\nDTEND:20260115T101000Z\nRRULE:FREQ=MONTHLY");
        Assert.Single(Day(sameDate, 2026, 11, 15));
        var firstFriday = Ics("UID:f\nSUMMARY:Demo\nDTSTART:20260102T150000Z\nDTEND:20260102T160000Z\nRRULE:FREQ=MONTHLY;BYDAY=1FR");
        Assert.Single(Day(firstFriday, 2026, 11, 6));
        Assert.Empty(Day(firstFriday, 2026, 11, 13));
        var lastMonday = Ics("UID:l\nSUMMARY:Retro\nDTSTART:20260126T150000Z\nDTEND:20260126T160000Z\nRRULE:FREQ=MONTHLY;BYDAY=-1MO");
        Assert.Single(Day(lastMonday, 2026, 11, 30));
    }

    [Fact]
    public void Yearly()
    {
        var ics = Ics("UID:y\nSUMMARY:Birthday\nDTSTART;VALUE=DATE:20000312\nDTEND;VALUE=DATE:20000313\nRRULE:FREQ=YEARLY");
        Assert.Single(Day(ics, 2027, 3, 12));
    }

    [Fact]
    public void A_repeat_keeps_its_local_time_across_daylight_saving()
    {
        var ics = Ics("""
            UID:w
            SUMMARY:Sync
            DTSTART;TZID=America/New_York:20261026T090000
            DTEND;TZID=America/New_York:20261026T093000
            RRULE:FREQ=WEEKLY;BYDAY=MO
            """);
        Assert.Equal(Utc(2026, 10, 26, 13), Assert.Single(Day(ics, 2026, 10, 26)).Start);
        Assert.Equal(Utc(2026, 11, 2, 14), Assert.Single(Day(ics, 2026, 11, 2)).Start);
    }

    [Fact]
    public void Skipped_and_moved_occurrences()
    {
        var ics = Ics(
            """
            UID:w
            SUMMARY:Sync
            DTSTART:20261102T090000Z
            DTEND:20261102T093000Z
            RRULE:FREQ=DAILY
            EXDATE:20261103T090000Z,20261104T090000Z
            """,
            """
            UID:w
            RECURRENCE-ID:20261105T090000Z
            SUMMARY:Sync (moved)
            DTSTART:20261105T130000Z
            DTEND:20261105T133000Z
            """,
            """
            UID:w
            RECURRENCE-ID:20261106T090000Z
            STATUS:CANCELLED
            SUMMARY:Sync
            DTSTART:20261106T090000Z
            DTEND:20261106T093000Z
            """);
        Assert.Empty(Day(ics, 2026, 11, 3));
        Assert.Empty(Day(ics, 2026, 11, 4));
        var moved = Assert.Single(Day(ics, 2026, 11, 5));
        Assert.Equal("Sync (moved)", moved.Title);
        Assert.Equal(Utc(2026, 11, 5, 13), moved.Start);
        Assert.Empty(Day(ics, 2026, 11, 6));
        Assert.Single(Day(ics, 2026, 11, 7));
    }

    [Fact]
    public void Meetings_you_declined_are_left_out()
    {
        var ics = Ics("""
            UID:a
            SUMMARY:Optional
            DTSTART:20261109T090000Z
            DTEND:20261109T100000Z
            ATTENDEE;CUTYPE=INDIVIDUAL;ROLE=REQ-PARTICIPANT;PARTSTAT=DECLINED;CN=me@example.com;X-NUM-GUESTS=0:mailto:me@example.com
            """);
        Assert.Empty(Day(ics, 2026, 11, 9));
    }

    [Fact]
    public void The_call_link_comes_from_the_conference_or_the_text()
    {
        var meet = Ics("UID:a\nSUMMARY:A\nDTSTART:20261109T090000Z\nDTEND:20261109T100000Z\nX-GOOGLE-CONFERENCE:https://meet.google.com/abc-defg-hij");
        Assert.Equal("https://meet.google.com/abc-defg-hij", Assert.Single(Day(meet, 2026, 11, 9)).JoinUrl);
        var zoom = Ics("UID:a\nSUMMARY:A\nDTSTART:20261109T090000Z\nDTEND:20261109T100000Z\nDESCRIPTION:Join here:\\nhttps://us02web.zoom.us/j/123?pwd=x\\nThanks");
        Assert.Equal("https://us02web.zoom.us/j/123?pwd=x", Assert.Single(Day(zoom, 2026, 11, 9)).JoinUrl);
        var teams = Ics("UID:a\nSUMMARY:A\nDTSTART:20261109T090000Z\nDTEND:20261109T100000Z\nLOCATION:Microsoft Teams Meeting\nDESCRIPTION:<a href=\"https://teams.microsoft.com/l/meetup-join/19%3a\">Join</a>");
        Assert.Equal("https://teams.microsoft.com/l/meetup-join/19%3a", Assert.Single(Day(teams, 2026, 11, 9)).JoinUrl);
        var none = Ics("UID:a\nSUMMARY:Lunch\nDTSTART:20261109T090000Z\nDTEND:20261109T100000Z\nDESCRIPTION:see https://example.com/menu");
        Assert.Null(Assert.Single(Day(none, 2026, 11, 9)).JoinUrl);
    }

    [Fact]
    public void Several_calendars_sorted_by_start()
    {
        var a = Ics("UID:b\nSUMMARY:Second\nDTSTART:20261109T110000Z\nDTEND:20261109T120000Z");
        var b = Ics("UID:a\nSUMMARY:First\nDTSTART:20261109T090000Z\nDTEND:20261109T100000Z");
        var day = CalendarFeed.Between([a, b], Utc(2026, 11, 9), Utc(2026, 11, 10), Accra);
        Assert.Equal(["First", "Second"], day.Select(e => e.Title));
    }

    [Fact]
    public void Each_event_links_to_its_page_in_google_calendar()
    {
        var ics = Ics(
            "UID:abc@google.com\nSUMMARY:One\nDTSTART:20261109T090000Z\nDTEND:20261109T100000Z",
            "UID:rep@google.com\nSUMMARY:Rep\nDTSTART:20261102T110000Z\nDTEND:20261102T120000Z\nRRULE:FREQ=WEEKLY");
        var day = Day(ics, 2026, 11, 9);
        string Eid(string s) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(Eid("abc me@example.com"), day[0].GoogleEid);
        Assert.Equal(Eid("rep_20261109T110000Z me@example.com"), day[1].GoogleEid);
    }
}
