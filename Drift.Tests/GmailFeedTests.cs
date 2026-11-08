using Drift;
using Xunit;

namespace Drift.Tests;

public class GmailFeedTests
{
    private const string Feed = """
        <?xml version="1.0" encoding="UTF-8"?><feed version="0.3" xmlns="http://purl.org/atom/ns#"><title>Gmail - Inbox for me@gmail.com</title><tagline>New messages in your Gmail Inbox</tagline><fullcount>27</fullcount><link rel="alternate" href="https://mail.google.com/mail/u/0" type="text/html"/><modified>2026-11-07T02:02:01Z</modified>
        <entry><title>TCS Toronto Waterfront Marathon &amp; more</title><summary>Complete a short survey and win!</summary><link rel="alternate" href="https://mail.google.com/mail/u/0?account_id=me@gmail.com&amp;message_id=18c2f0a1b2c3d4e5&amp;view=conv&amp;extsrc=atom" type="text/html"/><modified>2026-11-06T03:00:00Z</modified><issued>2026-11-06T03:00:00Z</issued><id>tag:gmail.google.com,2004:1784</id><author><name>Canada Running Series</name><email>news@runningseries.ca</email></author></entry>
        <entry><title></title><summary></summary><link rel="alternate" href="https://mail.google.com/mail/u/0?account_id=me@gmail.com&amp;message_id=18c2f0a1b2c3d4e6&amp;view=conv&amp;extsrc=atom" type="text/html"/><issued>2026-11-07T01:00:00Z</issued><author><name></name><email>ali@example.com</email></author></entry>
        </feed>
        """;

    [Fact]
    public void Reads_the_unread_count_and_each_email()
    {
        var (unread, items) = GmailFeed.Parse(Feed)!.Value;
        Assert.Equal(27, unread);
        Assert.Equal(2, items.Count);
        var first = items[1];
        Assert.Equal("Canada Running Series", first.Sender);
        Assert.Equal("TCS Toronto Waterfront Marathon & more", first.Subject);
        Assert.Equal("Complete a short survey and win!", first.Snippet);
        Assert.Equal(new DateTimeOffset(2026, 11, 6, 3, 0, 0, TimeSpan.Zero), first.Received);
        Assert.Equal("18c2f0a1b2c3d4e5", first.MessageId);
    }

    [Fact]
    public void Newest_first_with_stand_ins_for_whats_missing()
    {
        var (_, items) = GmailFeed.Parse(Feed)!.Value;
        Assert.Equal("ali@example.com", items[0].Sender);
        Assert.Equal("(no subject)", items[0].Subject);
    }

    [Fact]
    public void Not_a_feed_is_nothing()
    {
        Assert.Null(GmailFeed.Parse("<html><body>Sign in</body></html>"));
        Assert.Null(GmailFeed.Parse("not xml"));
    }

    [Fact]
    public void An_empty_inbox()
    {
        var (unread, items) = GmailFeed.Parse("""<?xml version="1.0"?><feed version="0.3" xmlns="http://purl.org/atom/ns#"><fullcount>0</fullcount></feed>""")!.Value;
        Assert.Equal(0, unread);
        Assert.Empty(items);
    }

    [Theory]
    [InlineData(30, "now")]
    [InlineData(5 * 60, "5m")]
    [InlineData(22 * 3600, "22h")]
    [InlineData(30 * 3600, "1d")]
    [InlineData(6 * 86400, "6d")]
    public void Age(int secondsAgo, string text)
    {
        var now = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(text, GmailFeed.Age(now.AddSeconds(-secondsAgo), now));
    }

    [Fact]
    public void Older_than_a_week_shows_the_date()
    {
        var now = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("Oct 3", GmailFeed.Age(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero), now));
    }

    [Fact]
    public void Links_are_for_the_tab_account()
    {
        Assert.True(GmailFeed.IsGmail(new Uri("https://mail.google.com/mail/u/0/#inbox")));
        Assert.False(GmailFeed.IsGmail(new Uri("https://calendar.google.com/")));
        Assert.Equal("/mail/u/1/feed/atom/", GmailFeed.FeedPath(1));
        Assert.Equal("https://mail.google.com/mail/u/0/#inbox/18c2f0a1b2c3d4e5", GmailFeed.Message("18c2f0a1b2c3d4e5", 0).ToString());
        Assert.Equal("https://mail.google.com/mail/u/2/#inbox?compose=new", GmailFeed.Compose(2).ToString());
    }
}
