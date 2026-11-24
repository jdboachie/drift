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
        var items = OutlookApi.ParseMessages(json, "outlook.cloud.microsoft");
        Assert.Equal(2, items.Count);
        Assert.Equal("noreply@x.com", items[0].Sender);
        Assert.Equal("(no subject)", items[0].Subject);
        Assert.Equal("Bilal", items[1].Sender);
        Assert.Equal("hi", items[1].Snippet);
        Assert.Equal(new DateTimeOffset(2026, 11, 6, 8, 0, 0, TimeSpan.Zero), items[1].Received);
        Assert.Equal("https://outlook.office365.com/owa/?ItemID=1", items[1].Link);
    }

    [Fact]
    public void An_email_opens_in_the_inbox_by_its_conversation()
    {
        var json = """
            {"value":[
              {"Id":"AAMkA1","ConversationId":"AAQkAGU5/+a=","Subject":"With","ReceivedDateTime":"2026-11-07T08:00:00Z","WebLink":"https://outlook.office365.com/owa/?ItemID=1"},
              {"Id":"AAMkA2","Subject":"Without","ReceivedDateTime":"2026-11-06T08:00:00Z","WebLink":"https://outlook.office365.com/owa/?ItemID=2"}
            ]}
            """;
        var items = OutlookApi.ParseMessages(json, "outlook.cloud.microsoft");
        Assert.Equal("https://outlook.cloud.microsoft/mail/inbox/id/AAQkAGU5%2F%2Ba%3D", items[0].Link);
        Assert.Equal("https://outlook.office365.com/owa/?ItemID=2", items[1].Link);
        Assert.Contains("ConversationId", OutlookApi.UnreadMessages("outlook.cloud.microsoft").Query);
    }

    [Fact]
    public void Unread_emails_in_one_conversation_are_one_row_as_outlook_lists_them()
    {
        // Two approvals on one pull request: Outlook shows the conversation once, by its newest email.
        var json = """
            {"value":[
              {"Id":"A1","ConversationId":"PR","Subject":"Re: fix(users)","From":{"EmailAddress":{"Name":"Abdul"}},"ReceivedDateTime":"2026-11-23T11:56:00Z"},
              {"Id":"J1","ConversationId":"RETRO","Subject":"2026 in retro","From":{"EmailAddress":{"Name":"Judith"}},"ReceivedDateTime":"2026-11-23T11:44:00Z"},
              {"Id":"N1","ConversationId":"PR","Subject":"Re: fix(users)","From":{"EmailAddress":{"Name":"Nana"}},"ReceivedDateTime":"2026-11-23T11:58:00Z"}
            ]}
            """;
        var items = OutlookApi.ParseMessages(json, "outlook.cloud.microsoft");
        // Everyone in it, newest first, and how many: "Nana; Abdul", 2.
        Assert.Equal(["Nana; Abdul", "Judith"], items.Select(i => i.Sender));
        Assert.Equal([2, 1], items.Select(i => i.Count));
        Assert.Equal(["PR", "RETRO"], items.Select(i => i.Conversation));
    }

    [Fact]
    public void A_conversation_shows_everyone_in_it_and_all_its_emails_as_outlook_does()
    {
        var unread = """
            {"value":[
              {"Id":"N1","ConversationId":"PR","Subject":"Re: fix(users)","From":{"EmailAddress":{"Name":"Nana"}},"ReceivedDateTime":"2026-11-23T11:58:00Z"},
              {"Id":"J1","ConversationId":"RETRO","Subject":"2026 in retro","From":{"EmailAddress":{"Name":"Judith"}},"ReceivedDateTime":"2026-11-23T11:44:00Z"}
            ]}
            """;
        // The inbox's emails in those conversations, read ones too: Abdul's newest (already read), then Nana, then Abdul.
        var inbox = """
            {"value":[
              {"Id":"A1","ConversationId":"PR","From":{"EmailAddress":{"Name":"Abdul"}},"ReceivedDateTime":"2026-11-23T11:56:00Z"},
              {"Id":"N1","ConversationId":"PR","From":{"EmailAddress":{"Name":"Nana"}},"ReceivedDateTime":"2026-11-23T11:58:00Z"},
              {"Id":"A2","ConversationId":"PR","From":{"EmailAddress":{"Address":"abdul@x.com"}},"ReceivedDateTime":"2026-11-23T12:10:00Z"},
              {"Id":"A3","ConversationId":"PR","From":{"EmailAddress":{"Name":"Abdul"}},"ReceivedDateTime":"2026-11-23T12:20:00Z"}
            ]}
            """;
        var rows = OutlookApi.WithConversations(OutlookApi.ParseMessages(unread, "outlook.cloud.microsoft"), inbox);
        Assert.Equal(["Abdul; abdul@x.com; Nana", "Judith"], rows.Select(r => r.Sender));
        Assert.Equal([4, 1], rows.Select(r => r.Count));
        // The row is still its newest unread email.
        Assert.Equal("N1", rows[0].MessageId);
    }

    [Fact]
    public void Asks_for_the_emails_in_the_conversations_shown()
    {
        var query = Uri.UnescapeDataString(OutlookApi.ConversationEmails("outlook.cloud.microsoft", ["AAQk+a/b=", "it's"]).Query);
        Assert.Contains("ConversationId eq 'AAQk+a/b=' or ConversationId eq 'it''s'", query);
        Assert.Contains("$select=ConversationId,From,ReceivedDateTime", query);
    }

    [Fact]
    public void Asks_for_up_to_fifty_unread_emails() =>
        Assert.Contains("$top=50", Uri.UnescapeDataString(OutlookApi.UnreadMessages("outlook.cloud.microsoft").Query));

    [Fact]
    public void An_emails_text_is_one_line()
    {
        var json = """
            {"value":[{"Id":"1","Subject":"Meeting\r\nbackgrounds","From":{"EmailAddress":{"Name":"Katrin\nHecker","Address":"k@x.com"}},"ReceivedDateTime":"2026-11-06T08:00:00Z",
              "BodyPreview":"Dear all,\r\n\r\nWe are excited to share\t the new ones.\r\n\r\nWe have"}]}
            """;
        var item = OutlookApi.ParseMessages(json, "outlook.cloud.microsoft")[0];
        Assert.Equal("Dear all, We are excited to share the new ones. We have", item.Snippet);
        Assert.Equal("Meeting backgrounds", item.Subject);
        Assert.Equal("Katrin Hecker", item.Sender);
    }

    [Fact]
    public void Not_json_is_nothing()
    {
        Assert.Empty(OutlookApi.ParseEvents("<html>", Utc));
        Assert.Empty(OutlookApi.ParseMessages("<html>", "outlook.cloud.microsoft"));
    }
}
