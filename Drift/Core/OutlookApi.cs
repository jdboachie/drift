using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Drift;

/// <summary>
/// Outlook on the web's own API (the one its pages call, on their own site: /api/beta/me/…), for the
/// Outlook integration (Outlook.cs): today's meetings and the inbox's unread emails, and which
/// pages are Outlook's mail and calendar.
/// </summary>
public static class OutlookApi
{
    private static readonly string[] Hosts = ["outlook.cloud.microsoft", "outlook.office.com", "outlook.office365.com"];

    public static bool IsOutlook(Uri? uri) => uri is not null && Hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);

    public static bool IsMail(Uri? uri) => IsOutlook(uri) && uri!.AbsolutePath.StartsWith("/mail", StringComparison.OrdinalIgnoreCase);

    public static bool IsCalendar(Uri? uri) => IsOutlook(uri) && uri!.AbsolutePath.StartsWith("/calendar", StringComparison.OrdinalIgnoreCase);

    /// <summary>Outlook's mail, which signs in and then calls the API, as the hidden view's page.</summary>
    public static Uri Start(string host) => new($"https://{host}/mail/");

    public static Uri CalendarView(string host, DateTimeOffset from, DateTimeOffset to) => new(
        $"https://{host}/api/beta/me/calendarview?startDateTime={Iso(from)}&endDateTime={Iso(to)}&$top=200"
        + "&$select=Subject,Start,End,IsAllDay,IsCancelled,ResponseStatus,OnlineMeeting,OnlineMeetingUrl,Location,BodyPreview,WebLink,Attendees,Organizer");

    /// <summary>The most unread emails one ask brings.</summary>
    public const int UnreadTop = 50;

    public static Uri UnreadMessages(string host) => new(
        $"https://{host}/api/beta/me/mailfolders/inbox/messages?$filter=IsRead%20eq%20false&$top={UnreadTop}"
        + "&$select=Subject,From,ReceivedDateTime,BodyPreview,WebLink,ConversationId");

    /// <summary>An email's conversation, open in the inbox's reading pane (not on a page of its own, as its WebLink is).</summary>
    public static Uri Conversation(string host, string conversationId) =>
        new($"https://{host}/mail/inbox/id/{Uri.EscapeDataString(conversationId)}");

    public static Uri NewEvent(string host) => new($"https://{host}/calendar/deeplink/compose");

    public static Uri NewMail(string host) => new($"https://{host}/mail/deeplink/compose");

    private static string Iso(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>The meetings in a calendar view, without cancelled ones or ones you've declined.</summary>
    public static List<CalendarEvent> ParseEvents(string json, TimeZoneInfo local)
    {
        var events = new List<CalendarEvent>();
        foreach (var e in Values(json))
        {
            if (Bool(e, "IsCancelled")) continue;
            if (e.TryGetProperty("ResponseStatus", out var rs) && rs.ValueKind == JsonValueKind.Object
                && Str(rs, "Response") == "Declined") continue;
            if (When(e, "Start") is not { } start || When(e, "End") is not { } end) continue;
            var allDay = Bool(e, "IsAllDay");
            DateTimeOffset s, f;
            if (allDay)
            {
                // A date, wherever you are: midnight here rather than in UTC.
                s = Local(start.Date, local);
                f = Local(end.Date, local);
            }
            else
            {
                s = new DateTimeOffset(DateTime.SpecifyKind(start, DateTimeKind.Utc));
                f = new DateTimeOffset(DateTime.SpecifyKind(end, DateTimeKind.Utc));
            }
            var join = e.TryGetProperty("OnlineMeeting", out var om) && om.ValueKind == JsonValueKind.Object ? Str(om, "JoinUrl") : null;
            join ??= Str(e, "OnlineMeetingUrl");
            join ??= CalendarFeed.FindCallLink(
                (e.TryGetProperty("Location", out var loc) && loc.ValueKind == JsonValueKind.Object ? Str(loc, "DisplayName") : null) + "\n" + Str(e, "BodyPreview"));
            events.Add(new CalendarEvent(Str(e, "Id") ?? "", Str(e, "Subject") is { Length: > 0 } t ? t : "(No title)", s, f, allDay, join)
            {
                WebLink = Str(e, "WebLink"),
                People = People(e),
            });
        }
        return events.OrderBy(e => e.Start).ToList();
    }

    /// <summary>The organizer and the people invited, by name; rooms and other resources left out.</summary>
    private static List<string> People(JsonElement e)
    {
        var people = new List<string>();
        void Add(JsonElement who)
        {
            if (who.ValueKind != JsonValueKind.Object || !who.TryGetProperty("EmailAddress", out var mail) || mail.ValueKind != JsonValueKind.Object) return;
            var name = CalendarFeed.PersonName(Str(mail, "Name"), Str(mail, "Address"));
            if (name.Length > 0 && !people.Contains(name, StringComparer.OrdinalIgnoreCase)) people.Add(name);
        }
        if (e.TryGetProperty("Organizer", out var organizer)) Add(organizer);
        if (e.TryGetProperty("Attendees", out var attendees) && attendees.ValueKind == JsonValueKind.Array)
            foreach (var a in attendees.EnumerateArray())
                if (!string.Equals(Str(a, "Type"), "Resource", StringComparison.OrdinalIgnoreCase)) Add(a);
        return people;
    }

    /// <summary>
    /// The inbox's unread emails, newest first, each opening in the inbox on <paramref name="host"/>. A conversation
    /// with several is one row, by its newest, as Outlook lists it: its senders, newest first, and how many there are.
    /// </summary>
    public static List<MailItem> ParseMessages(string json, string host) =>
        Values(json).GroupBy(m => Str(m, "ConversationId") is { Length: > 0 } c ? c : Str(m, "Id") ?? Guid.NewGuid().ToString())
        .Select(g =>
        {
            var m = g.MaxBy(Received);
            var conversation = Str(m, "ConversationId") is { Length: > 0 } c ? c : null;
            return new MailItem(
                Senders(g),
                MailItem.OneLine(Str(m, "Subject")) is { Length: > 0 } s ? s : "(no subject)",
                MailItem.OneLine(Str(m, "BodyPreview")),
                Received(m),
                Str(m, "Id"))
            {
                Link = conversation is not null ? Conversation(host, conversation).AbsoluteUri : Str(m, "WebLink"),
                Conversation = conversation,
                Count = g.Count(),
            };
        }).OrderByDescending(i => i.Received).ToList();

    /// <summary>The inbox's emails in these conversations, read or not: who's in each and how many it has (WithConversations).</summary>
    public static Uri ConversationEmails(string host, IEnumerable<string> conversations)
    {
        var filter = string.Join(" or ", conversations.Select(c => $"ConversationId eq '{c.Replace("'", "''")}'"));
        return new($"https://{host}/api/beta/me/mailfolders/inbox/messages?$filter={Uri.EscapeDataString(filter)}&$top=200"
            + "&$select=ConversationId,From,ReceivedDateTime");
    }

    /// <summary>
    /// Rows with everyone in their conversations and all their emails, read ones too, as Outlook lists them ("Abdul;
    /// Nana", 3), from the inbox's emails in them (ConversationEmails). A row stays its newest unread email; one with
    /// none in the answer keeps what it had.
    /// </summary>
    public static List<MailItem> WithConversations(List<MailItem> rows, string json)
    {
        var emails = Values(json).Where(m => Str(m, "ConversationId") is { Length: > 0 })
            .GroupBy(m => Str(m, "ConversationId")!).ToDictionary(g => g.Key, g => g.ToList());
        return rows.Select(r => r.Conversation is { } c && emails.TryGetValue(c, out var all)
            ? r with { Sender = Senders(all), Count = Math.Max(all.Count, r.Count) }
            : r).ToList();
    }

    /// <summary>Who emails are from, newest first, each once: "Abdul; Nana".</summary>
    private static string Senders(IEnumerable<JsonElement> emails) =>
        string.Join("; ", emails.OrderByDescending(Received).Select(SenderOf).Where(s => s.Length > 0).Distinct());

    private static string SenderOf(JsonElement m)
    {
        if (!m.TryGetProperty("From", out var from) || from.ValueKind != JsonValueKind.Object
            || !from.TryGetProperty("EmailAddress", out var ea) || ea.ValueKind != JsonValueKind.Object) return "";
        return MailItem.OneLine(Str(ea, "Name") is { Length: > 0 } name ? name : Str(ea, "Address"));
    }

    private static DateTimeOffset Received(JsonElement m) =>
        DateTimeOffset.TryParse(Str(m, "ReceivedDateTime"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var r)
            ? r : DateTimeOffset.MinValue;

    private static List<JsonElement> Values(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Array
                ? v.EnumerateArray().Select(x => x.Clone()).ToList()
                : [];
        }
        catch (JsonException) { return []; }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>A start or end, in UTC (asked for so).</summary>
    private static DateTime? When(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object
        && DateTime.TryParse(Str(v, "DateTime"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;

    private static DateTimeOffset Local(DateTime day, TimeZoneInfo zone)
    {
        var wall = DateTime.SpecifyKind(day, DateTimeKind.Unspecified);
        return new DateTimeOffset(wall, zone.GetUtcOffset(wall));
    }
}
