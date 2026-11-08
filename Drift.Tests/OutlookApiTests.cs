using Drift;
using Xunit;

namespace Drift.Tests;

public class OutlookApiTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    [Fact]
    public void Knows_outlook_mail_and_calendar_pages()
    {
        Assert.True(OutlookApi.IsMail(new Uri("https://outlook.cloud.microsoft/mail/")));
        Assert.True(OutlookApi.IsMail(new Uri("https://outlook.office.com/mail/inbox")));
        Assert.True(OutlookApi.IsCalendar(new Uri("https://outlook.cloud.microsoft/calendar/view/week")));
        Assert.False(OutlookApi.IsCalendar(new Uri("https://outlook.cloud.microsoft/mail/")));
        Assert.False(OutlookApi.IsMail(new Uri("https://mail.google.com/mail/u/0/")));
        Assert.False(OutlookApi.IsMail(null));
    }

    [Fact]
    public void Asks_for_a_stretch_of_the_calendar_in_utc()
    {
        var url = OutlookApi.CalendarView("outlook.cloud.microsoft",
            new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 11, 0, 0, 0, TimeSpan.Zero));
        Assert.StartsWith("https://outlook.cloud.microsoft/api/beta/me/calendarview?startDateTime=2026-11-09T00:00:00Z&endDateTime=2026-11-11T00:00:00Z", url.ToString());
    }

    [Fact]
    public void Reads_meetings()
    {
        var json = """
            {"value":[
              {"Id":"AAA","Subject":"Standup","IsAllDay":false,"IsCancelled":false,
               "Start":{"DateTime":"2026-11-09T09:00:00.0000000","TimeZone":"UTC"},"End":{"DateTime":"2026-11-09T09:15:00.0000000","TimeZone":"UTC"},
               "ResponseStatus":{"Response":"Accepted"},"OnlineMeeting":{"JoinUrl":"https://teams.microsoft.com/l/meetup-join/abc"},
               "WebLink":"https://outlook.office365.com/owa/?itemid=AAA&exvsurl=1&path=/calendar/item"},
              {"Id":"BBB","Subject":"Holiday","IsAllDay":true,"IsCancelled":false,
               "Start":{"DateTime":"2026-11-09T00:00:00.0000000","TimeZone":"UTC"},"End":{"DateTime":"2026-11-10T00:00:00.0000000","TimeZone":"UTC"},
               "ResponseStatus":{"Response":"None"}}
            ]}
            """;
        var events = OutlookApi.ParseEvents(json, TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));
        Assert.Equal(2, events.Count);
        var standup = events.Single(e => e.Title == "Standup");
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 9, 0, 0, TimeSpan.Zero), standup.Start);
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 9, 15, 0, TimeSpan.Zero), standup.End);
        Assert.Equal("https://teams.microsoft.com/l/meetup-join/abc", standup.JoinUrl);
        Assert.Equal("https://outlook.office365.com/owa/?itemid=AAA&exvsurl=1&path=/calendar/item", standup.WebLink);
        var holiday = events.Single(e => e.Title == "Holiday");
        Assert.True(holiday.AllDay);
        // An all-day event is the day where you are, not midnight in UTC.
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.FromHours(-5)), holiday.Start);
    }

    [Fact]
    public void Cancelled_and_declined_meetings_are_left_out()
    {
        var json = """
            {"value":[
              {"Id":"A","Subject":"Gone","IsCancelled":true,"Start":{"DateTime":"2026-11-09T09:00:00","TimeZone":"UTC"},"End":{"DateTime":"2026-11-09T10:00:00","TimeZone":"UTC"}},
              {"Id":"B","Subject":"No","ResponseStatus":{"Response":"Declined"},"Start":{"DateTime":"2026-11-09T09:00:00","TimeZone":"UTC"},"End":{"DateTime":"2026-11-09T10:00:00","TimeZone":"UTC"}}
            ]}
            """;
        Assert.Empty(OutlookApi.ParseEvents(json, Utc));
    }

    [Fact]
    public void The_call_link_falls_back_to_the_text()
    {
        var json = """
            {"value":[{"Id":"A","Subject":"Sync","Start":{"DateTime":"2026-11-09T09:00:00","TimeZone":"UTC"},"End":{"DateTime":"2026-11-09T10:00:00","TimeZone":"UTC"},
              "OnlineMeeting":null,"Location":{"DisplayName":"Microsoft Teams Meeting"},
              "BodyPreview":"Join on your computer\r\nhttps://teams.microsoft.com/l/meetup-join/19%3ameeting_x\r\nMeeting ID: 1"}]}
            """;
        Assert.Equal("https://teams.microsoft.com/l/meetup-join/19%3ameeting_x", Assert.Single(OutlookApi.ParseEvents(json, Utc)).JoinUrl);
    }

    [Fact]
    public void Reads_unread_emails_newest_first()
    {
        var json = """
            {"value":[
              {"Id":"1","Subject":"Older","From":{"EmailAddress":{"Name":"Bilal","Address":"b@x.com"}},"ReceivedDateTime":"2026-11-06T08:00:00Z","BodyPreview":"hi","WebLink":"https://outlook.office365.com/owa/?ItemID=1"},
              {"Id":"2","Subject":"","From":{"EmailAddress":{"Name":"","Address":"noreply@x.com"}},"ReceivedDateTime":"2026-11-07T08:00:00Z","BodyPreview":""}
            ]}
            """;
        var items = OutlookApi.ParseMessages(json);
        Assert.Equal(2, items.Count);
        Assert.Equal("noreply@x.com", items[0].Sender);
        Assert.Equal("(no subject)", items[0].Subject);
        Assert.Equal("Bilal", items[1].Sender);
        Assert.Equal("hi", items[1].Snippet);
        Assert.Equal(new DateTimeOffset(2026, 11, 6, 8, 0, 0, TimeSpan.Zero), items[1].Received);
        Assert.Equal("https://outlook.office365.com/owa/?ItemID=1", items[1].Link);
    }

    [Fact]
    public void Reads_the_unread_count()
    {
        Assert.Equal(12, OutlookApi.ParseUnread("""{"Id":"x","UnreadItemCount":12}"""));
        Assert.Equal(0, OutlookApi.ParseUnread("""{"error":{}}"""));
    }

    [Fact]
    public void Not_json_is_nothing()
    {
        Assert.Empty(OutlookApi.ParseEvents("<html>", Utc));
        Assert.Empty(OutlookApi.ParseMessages("<html>"));
    }
}
