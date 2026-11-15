using System;
using System.Linq;
using Drift;
using Xunit;

public class DownloadPeekTests
{
    private record File(string Name, bool Running, bool Saved, bool OnDisk);

    private static (bool, bool, bool) StateOf(File f) => (f.Running, f.Saved, f.OnDisk);

    [Fact]
    public void Picks_the_newest_three_that_can_be_opened_or_are_coming()
    {
        var files = new[]
        {
            new File("a", Running: true, Saved: false, OnDisk: false),
            new File("cancelled", false, false, false),
            new File("b", false, true, true),
            new File("deleted", false, true, false),
            new File("c", false, true, true),
            new File("d", false, true, true),
        };
        Assert.Equal(["a", "b", "c"], DownloadPeek.Pick(files, StateOf).Select(f => f.Name));
    }

    [Fact]
    public void Picks_fewer_when_there_are_fewer()
    {
        var files = new[] { new File("a", false, true, true), new File("failed", false, false, false) };
        Assert.Equal(["a"], DownloadPeek.Pick(files, StateOf).Select(f => f.Name));
        Assert.Empty(DownloadPeek.Pick(Array.Empty<File>(), StateOf));
    }

    [Theory]
    [InlineData(0, "Just now")]
    [InlineData(59, "Just now")]
    [InlineData(60, "1 minute ago")]
    [InlineData(3 * 60 + 20, "3 minutes ago")]
    [InlineData(59 * 60 + 59, "59 minutes ago")]
    [InlineData(60 * 60, "1 hour ago")]
    [InlineData(5 * 3600 + 1800, "5 hours ago")]
    public void Says_how_long_ago_today(int secondsAgo, string expected)
    {
        var now = new DateTime(2026, 11, 13, 23, 30, 0);
        Assert.Equal(expected, DownloadPeek.Ago(now.AddSeconds(-secondsAgo), now));
    }

    [Fact]
    public void Says_yesterday_days_then_the_date()
    {
        var now = new DateTime(2026, 11, 13, 9, 0, 0);
        Assert.Equal("Yesterday", DownloadPeek.Ago(new DateTime(2026, 11, 12, 23, 0, 0), now));
        Assert.Equal("Yesterday", DownloadPeek.Ago(new DateTime(2026, 11, 12, 1, 0, 0), now));
        Assert.Equal("3 days ago", DownloadPeek.Ago(new DateTime(2026, 11, 10, 18, 0, 0), now));
        Assert.Equal("6 days ago", DownloadPeek.Ago(new DateTime(2026, 11, 7, 18, 0, 0), now));
        Assert.Equal("6 Nov", DownloadPeek.Ago(new DateTime(2026, 11, 6, 18, 0, 0), now));
        Assert.Equal("6 Nov 2025", DownloadPeek.Ago(new DateTime(2025, 11, 6, 18, 0, 0), now));
    }

    [Fact]
    public void An_hour_ago_across_midnight_is_still_in_hours()
    {
        var now = new DateTime(2026, 11, 13, 0, 30, 0);
        Assert.Equal("2 hours ago", DownloadPeek.Ago(new DateTime(2026, 11, 12, 22, 20, 0), now));
    }

    [Fact]
    public void A_clock_that_went_back_says_just_now()
    {
        var now = new DateTime(2026, 11, 13, 9, 0, 0);
        Assert.Equal("Just now", DownloadPeek.Ago(now.AddMinutes(5), now));
    }
}
