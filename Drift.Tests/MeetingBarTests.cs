using System.Globalization;
using Drift;
using Xunit;

namespace Drift.Tests;

public class MeetingBarTests
{
    private static readonly DateTimeOffset Two = new(2026, 11, 26, 14, 0, 0, TimeSpan.Zero);

    private static CalendarEvent Meeting(DateTimeOffset start, int minutes = 30, string? join = "https://meet.google.com/abc-defg-hij", params string[] people) =>
        new("u1", "Weekly design sync", start, start.AddMinutes(minutes), false, join) { People = people };

    [Theory]
    [InlineData(-8 * 60, "In 8 min", "8m")]
    [InlineData(-30, "In 1 min", "1m")]
    [InlineData(-65 * 60, "In 1 hr 5 min", "1h 5m")]
    [InlineData(-60 * 60, "In 1 hr", "1h")]
    [InlineData(0, "Starting now", "now")]
    [InlineData(3 * 60 + 5, "Started 3 min ago", "3m ago")]
    public void Counts_down_on_the_card_and_on_the_row(int secondsFromStart, string card, string row)
    {
        var now = Two.AddSeconds(secondsFromStart);
        Assert.Equal(card, MeetingBar.Soon(Meeting(Two), now));
        Assert.Equal(row, MeetingBar.Short(Meeting(Two), now));
    }

    [Fact]
    public void The_hours_drop_the_start_time_s_AM_or_PM_when_the_end_shares_it()
    {
        var us = CultureInfo.GetCultureInfo("en-US");
        Assert.Equal("2:00 – 2:30 PM", MeetingBar.Hours(Meeting(Two), TimeZoneInfo.Utc, us));
        Assert.Equal("11:30 AM – 12:15 PM", MeetingBar.Hours(Meeting(Two.AddMinutes(-150), 45), TimeZoneInfo.Utc, us));
        // A 24-hour clock has nothing to drop.
        Assert.Equal("14:00 – 14:30", MeetingBar.Hours(Meeting(Two), TimeZoneInfo.Utc, CultureInfo.GetCultureInfo("en-GB")));
    }

    [Fact]
    public void Opens_itself_ten_minutes_before_until_it_ends()
    {
        var m = Meeting(Two);
        Assert.False(MeetingBar.OpensItself(m, Two.AddMinutes(-11)));
        Assert.True(MeetingBar.OpensItself(m, Two.AddMinutes(-10)));
        Assert.True(MeetingBar.OpensItself(m, Two.AddMinutes(29)));
        Assert.False(MeetingBar.OpensItself(m, Two.AddMinutes(30)));
    }

    [Fact]
    public void Collapsed_or_closed_before_Join_it_comes_back_when_Join_does()
    {
        var m = Meeting(Two);
        var away = new MeetingBar.Away(MeetingClock.Key(m), Closed: true, At: Two.AddMinutes(-8));
        Assert.True(MeetingBar.StillAway(away, m, Two.AddMinutes(-6)));
        Assert.False(MeetingBar.StillAway(away, m, Two.AddMinutes(-5)));
    }

    [Fact]
    public void Collapsed_or_closed_once_Join_shows_it_stays_away()
    {
        var m = Meeting(Two);
        var away = new MeetingBar.Away(MeetingClock.Key(m), Closed: false, At: Two.AddMinutes(-4));
        Assert.True(MeetingBar.StillAway(away, m, Two.AddMinutes(-1)));
        Assert.True(MeetingBar.StillAway(away, m, Two.AddMinutes(10)));
    }

    [Fact]
    public void Putting_one_meeting_away_leaves_the_next_alone()
    {
        var away = new MeetingBar.Away(MeetingClock.Key(Meeting(Two)), Closed: true, At: Two.AddMinutes(-8));
        Assert.False(MeetingBar.StillAway(away, Meeting(Two.AddHours(1)) with { Uid = "u2" }, Two.AddMinutes(-6)));
        Assert.False(MeetingBar.StillAway(null, Meeting(Two), Two));
    }

    [Theory]
    [InlineData("https://meet.google.com/abc-defg-hij", "Google Meet")]
    [InlineData("https://us02web.zoom.us/j/123", "Zoom")]
    [InlineData("https://teams.microsoft.com/l/meetup-join/x", "Microsoft Teams")]
    [InlineData("https://www.example.com/call", "example.com")]
    [InlineData(null, null)]
    public void Names_the_call_by_its_link(string? url, string? name)
    {
        Assert.Equal(name, MeetingBar.CallName(url));
    }

    [Fact]
    public void Shows_three_faces_and_a_count_for_the_rest()
    {
        var faces = MeetingBar.FacesOf(["Ama Owusu", "Kofi", "esi", "Yaw", "Abena", "Kwame"]);
        Assert.Equal(["A", "K", "E", "+3"], faces.Select(f => f.Text));
        Assert.Equal(-1, faces[^1].Color);
        // Four show all four, rather than three and "+1".
        Assert.Equal(4, MeetingBar.FacesOf(["A", "B", "C", "D"]).Count(f => f.Color >= 0));
    }

    [Fact]
    public void A_person_keeps_their_colour()
    {
        Assert.Equal(MeetingBar.ColorOf("Ama Owusu"), MeetingBar.ColorOf(" ama owusu "));
        Assert.InRange(MeetingBar.ColorOf("Kofi"), 0, MeetingBar.FaceColors - 1);
    }

    [Theory]
    [InlineData("Ama Owusu", "ama@example.com", "Ama Owusu")]
    [InlineData(null, "kofi.mensah@example.com", "kofi.mensah")]
    [InlineData("esi@example.com", "esi@example.com", "esi")]
    public void A_person_is_their_name_or_the_start_of_their_address(string? name, string? address, string shown)
    {
        Assert.Equal(shown, CalendarFeed.PersonName(name, address));
    }

    [Fact]
    public void Reads_who_is_invited_from_google_calendar_without_rooms()
    {
        var ics = "BEGIN:VCALENDAR\r\nX-WR-CALNAME:me@example.com\r\nBEGIN:VEVENT\r\nUID:a@google.com\r\nSUMMARY:Sync\r\n"
            + "DTSTART:20261126T140000Z\r\nDTEND:20261126T143000Z\r\n"
            + "ATTENDEE;CN=Ama Owusu;PARTSTAT=ACCEPTED:mailto:ama@example.com\r\n"
            + "ATTENDEE;CN=me@example.com;PARTSTAT=ACCEPTED:mailto:me@example.com\r\n"
            + "ATTENDEE;CUTYPE=RESOURCE;CN=Room 4:mailto:room4@resource.calendar.google.com\r\n"
            + "END:VEVENT\r\nEND:VCALENDAR\r\n";
        var e = Assert.Single(CalendarFeed.Between(ics, Two.AddHours(-2), Two.AddHours(2), TimeZoneInfo.Utc));
        Assert.Equal(["Ama Owusu", "me"], e.People);
    }

    [Fact]
    public void Reads_who_is_invited_from_outlook_with_the_organizer_and_without_rooms()
    {
        var json = """
            { "value": [ {
              "Id": "x", "Subject": "Sync", "IsAllDay": false, "IsCancelled": false,
              "Start": { "DateTime": "2026-11-26T14:00:00.0000000", "TimeZone": "UTC" },
              "End": { "DateTime": "2026-11-26T14:30:00.0000000", "TimeZone": "UTC" },
              "Organizer": { "EmailAddress": { "Name": "Kofi Mensah", "Address": "kofi@example.com" } },
              "Attendees": [
                { "Type": "Required", "EmailAddress": { "Name": "Kofi Mensah", "Address": "kofi@example.com" } },
                { "Type": "Optional", "EmailAddress": { "Name": "", "Address": "esi@example.com" } },
                { "Type": "Resource", "EmailAddress": { "Name": "Room 4", "Address": "room4@example.com" } }
              ]
            } ] }
            """;
        var e = Assert.Single(OutlookApi.ParseEvents(json, TimeZoneInfo.Utc));
        Assert.Equal(["Kofi Mensah", "esi"], e.People);
    }
}
