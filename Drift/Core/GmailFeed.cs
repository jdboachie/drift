using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Drift;

/// <summary>An unread email, as Gmail's feed has it.</summary>
public sealed record MailItem(string Sender, string Subject, string Snippet, DateTimeOffset Received, string? MessageId)
{
    /// <summary>Its page, where the mail service gives one (Outlook).</summary>
    public string? Link { get; init; }
}

/// <summary>
/// Gmail's unread feed (/mail/u/n/feed/atom/: the inbox's unread count and its latest twenty unread
/// emails), and Gmail's addresses, for the Gmail integration (Gmail.cs).
/// </summary>
public static class GmailFeed
{
    public const string Host = "mail.google.com";
    private static readonly XNamespace Atom = "http://purl.org/atom/ns#";

    public static bool IsGmail(Uri? uri) => uri is not null && string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase);

    public static string FeedPath(int account) => $"/mail/u/{account}/feed/atom/";

    /// <summary>Somewhere tiny on Gmail's site, where its feed can be asked for as the signed-in page would.</summary>
    public static Uri Base => new($"https://{Host}/robots.txt");

    /// <summary>Gmail itself, which signs the site in for the feed when its sign-in has lapsed.</summary>
    public static Uri Inbox(int account) => new($"https://{Host}/mail/u/{account}/");

    public static Uri Message(string id, int account) => new($"https://{Host}/mail/u/{account}/#inbox/{id}");

    public static Uri Compose(int account) => new($"https://{Host}/mail/u/{account}/#inbox?compose=new");

    /// <summary>The unread count and emails, newest first; null if it isn't the feed (a sign-in page, say).</summary>
    public static (int Unread, List<MailItem> Items)? Parse(string xml)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch { return null; }
        if (doc.Root is not { } feed || feed.Name != Atom + "feed") return null;
        var unread = int.TryParse(feed.Element(Atom + "fullcount")?.Value, out var n) ? n : 0;
        var items = feed.Elements(Atom + "entry").Select(e =>
        {
            var author = e.Element(Atom + "author");
            var sender = Text(author?.Element(Atom + "name")) ?? Text(author?.Element(Atom + "email")) ?? "";
            var subject = Text(e.Element(Atom + "title")) ?? "(no subject)";
            var snippet = Text(e.Element(Atom + "summary")) ?? "";
            var when = DateTimeOffset.TryParse(Text(e.Element(Atom + "issued")) ?? Text(e.Element(Atom + "modified")),
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : DateTimeOffset.MinValue;
            var link = e.Elements(Atom + "link").FirstOrDefault()?.Attribute("href")?.Value ?? "";
            var id = Regex.Match(link, @"[?&]message_id=([0-9a-fA-F]+)") is { Success: true } m ? m.Groups[1].Value : null;
            return new MailItem(sender, subject, snippet, when, id);
        }).OrderByDescending(i => i.Received).ToList();
        return (unread, items);
    }

    private static string? Text(XElement? e) => e?.Value.Trim() is { Length: > 0 } s ? s : null;

    /// <summary>"now", "5m", "22h", "3d", then the date.</summary>
    public static string Age(DateTimeOffset received, DateTimeOffset now)
    {
        var ago = now - received;
        if (ago < TimeSpan.FromMinutes(1)) return "now";
        if (ago < TimeSpan.FromHours(1)) return $"{(int)ago.TotalMinutes}m";
        if (ago < TimeSpan.FromDays(1)) return $"{(int)ago.TotalHours}h";
        if (ago < TimeSpan.FromDays(7)) return $"{(int)ago.TotalDays}d";
        return received.ToString("MMM d", CultureInfo.InvariantCulture);
    }
}
