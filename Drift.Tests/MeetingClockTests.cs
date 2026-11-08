using Drift;
using Xunit;

namespace Drift.Tests;

public class MeetingClockTests
{
    private static readonly DateTimeOffset Nine = new(2026, 11, 9, 9, 0, 0, TimeSpan.Zero);

    private static CalendarEvent Meeting(string title, DateTimeOffset start, int minutes = 30, string? join = "https://meet.google.com/x", bool allDay = false) =>
        new("u-" + title, title, start, start.AddMinutes(minutes), allDay, join);

    [Theory]
    [InlineData(-12 * 60, "in 12m")]
    [InlineData(-30, "in 1m")]
    [InlineData(-90 * 60, "in 1h 30m")]
    [InlineData(-60 * 60, "in 1h")]
    [InlineData(0, "now")]
    [InlineData(59, "now")]
    [InlineData(2 * 60 + 5, "2m ago")]
    public void Countdown(int secondsFromStart, string text)
    {
        Assert.Equal(text, MeetingClock.Countdown(Meeting("a", Nine), Nine.AddSeconds(secondsFromStart)));
    }

    [Fact]
    public void Join_opens_five_minutes_before_until_the_end()
    {
        var m = Meeting("a", Nine);
        Assert.False(MeetingClock.CanJoin(m, Nine.AddMinutes(-6)));
        Assert.True(MeetingClock.CanJoin(m, Nine.AddMinutes(-5)));
        Assert.True(MeetingClock.CanJoin(m, Nine.AddMinutes(29)));
        Assert.False(MeetingClock.CanJoin(m, Nine.AddMinutes(30)));
        Assert.False(MeetingClock.CanJoin(Meeting("b", Nine, join: null), Nine));
    }

    [Fact]
    public void The_tile_counts_down_to_the_next_meeting_within_the_hour()
    {
        var m = Meeting("a", Nine);
        Assert.Null(MeetingClock.Upcoming([m], Nine.AddMinutes(-61)));
        Assert.Same(m, MeetingClock.Upcoming([m], Nine.AddMinutes(-60)));
    }

    [Fact]
    public void A_started_meeting_stays_for_ten_minutes()
    {
        var m = Meeting("a", Nine, minutes: 60);
        Assert.Same(m, MeetingClock.Upcoming([m], Nine.AddMinutes(9)));
        Assert.Null(MeetingClock.Upcoming([m], Nine.AddMinutes(10)));
        var short_ = Meeting("b", Nine, minutes: 5);
        Assert.Null(MeetingClock.Upcoming([short_], Nine.AddMinutes(5)));
    }

    [Fact]
    public void One_about_to_start_beats_one_already_going()
    {
        var going = Meeting("going", Nine);
        var next = Meeting("next", Nine.AddMinutes(10));
        Assert.Same(going, MeetingClock.Upcoming([going, next], Nine.AddMinutes(2)));
        Assert.Same(next, MeetingClock.Upcoming([going, next], Nine.AddMinutes(6)));
    }

    [Fact]
    public void All_day_events_dont_count()
    {
        Assert.Null(MeetingClock.Upcoming([Meeting("hol", Nine, 24 * 60, allDay: true)], Nine));
    }

    [Fact]
    public void Reminders_come_a_minute_before_and_once()
    {
        var m = Meeting("a", Nine);
        var shown = new HashSet<string>();
        Assert.Empty(MeetingClock.DueReminders([m], Nine.AddMinutes(-2), shown));
        var due = Assert.Single(MeetingClock.DueReminders([m], Nine.AddMinutes(-1), shown));
        shown.Add(MeetingClock.Key(due));
        Assert.Empty(MeetingClock.DueReminders([m], Nine.AddSeconds(-30), shown));
    }

    [Fact]
    public void No_reminder_for_one_well_underway()
    {
        Assert.Empty(MeetingClock.DueReminders([Meeting("a", Nine)], Nine.AddMinutes(5), new HashSet<string>()));
        Assert.Single(MeetingClock.DueReminders([Meeting("a", Nine)], Nine.AddMinutes(4), new HashSet<string>()));
    }

    [Fact]
    public void Overlapping_meetings_sit_side_by_side()
    {
        var a = Meeting("a", Nine, 60);
        var b = Meeting("b", Nine.AddMinutes(30), 60);
        var c = Meeting("c", Nine.AddMinutes(120), 30);
        var lanes = MeetingClock.Lanes([a, b, c]);
        Assert.Equal((0, 2), lanes[a]);
        Assert.Equal((1, 2), lanes[b]);
        Assert.Equal((0, 1), lanes[c]);
    }

    [Fact]
    public void Back_to_back_meetings_dont_overlap()
    {
        var a = Meeting("a", Nine, 30);
        var b = Meeting("b", Nine.AddMinutes(30), 30);
        var lanes = MeetingClock.Lanes([a, b]);
        Assert.Equal((0, 1), lanes[a]);
        Assert.Equal((0, 1), lanes[b]);
    }
}
