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
        + "&$select=Subject,Start,End,IsAllDay,IsCancelled,ResponseStatus,OnlineMeeting,OnlineMeetingUrl,Location,BodyPreview,WebLink");

    public static Uri UnreadMessages(string host) => new(
        $"https://{host}/api/beta/me/mailfolders/inbox/messages?$filter=IsRead%20eq%20false&$top=20"
        + "&$select=Subject,From,ReceivedDateTime,BodyPreview,WebLink");

    public static Uri Inbox(string host) => new($"https://{host}/api/beta/me/mailfolders/inbox?$select=UnreadItemCount");

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
            });
        }
        return events.OrderBy(e => e.Start).ToList();
    }

    /// <summary>The inbox's unread emails, newest first.</summary>
    public static List<MailItem> ParseMessages(string json) =>
        Values(json).Select(m =>
        {
            string? name = null, address = null;
            if (m.TryGetProperty("From", out var from) && from.ValueKind == JsonValueKind.Object
                && from.TryGetProperty("EmailAddress", out var ea) && ea.ValueKind == JsonValueKind.Object)
            {
                name = Str(ea, "Name");
                address = Str(ea, "Address");
            }
            var received = DateTimeOffset.TryParse(Str(m, "ReceivedDateTime"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var r) ? r : DateTimeOffset.MinValue;
            return new MailItem(
                name is { Length: > 0 } ? name : address ?? "",
                Str(m, "Subject") is { Length: > 0 } s ? s : "(no subject)",
                Str(m, "BodyPreview")?.Trim() ?? "",
                received,
                Str(m, "Id"))
            { Link = Str(m, "WebLink") };
        }).OrderByDescending(i => i.Received).ToList();

    public static int ParseUnread(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("UnreadItemCount", out var n) && n.TryGetInt32(out var count) ? count : 0;
        }
        catch (JsonException) { return 0; }
    }

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
