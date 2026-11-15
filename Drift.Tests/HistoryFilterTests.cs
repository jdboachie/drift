namespace Drift.Tests;

public class HistoryFilterTests
{
    private sealed record Visit(string Title, string Url, DateTime When, int Visits = 1);

    // A Friday.
    private static readonly DateTime Today = new(2026, 11, 13);

    private static readonly Visit[] Visits =
    {
        new("Hacker News", "https://news.ycombinator.com/", Today.AddHours(9), 40),
        new("Rust book", "https://doc.rust-lang.org/book/", Today.AddHours(11), 2),
        new("Gmail", "https://mail.google.com/", Today.AddDays(-1).AddHours(20), 90),
        new("HN thread", "https://news.ycombinator.com/item?id=1", Today.AddDays(-2).AddHours(8), 1),
        new("Old news", "https://www.example.org/news", new DateTime(2026, 10, 2, 8, 0, 0), 5),
    };

    private static HistoryView.Result<Visit> Find(HistorySort sort = HistorySort.Date, HistoryRange range = HistoryRange.All, int limit = 100) =>
        HistoryView.Find(Visits, v => v.Title, v => v.Url, v => v.When, "", limit, Today,
            sort, HistoryFilter.Since(range, Today.AddHours(22), DayOfWeek.Monday), v => v.Visits);

    private static string[] Titles(HistoryView.Result<Visit> found) =>
        found.Days.SelectMany(d => d.Entries).Select(v => v.Title).ToArray();

    [Fact]
    public void Today_starts_at_midnight() =>
        Assert.Equal(Today, HistoryFilter.Since(HistoryRange.Today, Today.AddHours(22), DayOfWeek.Monday));

    [Fact]
    public void This_week_starts_on_the_first_day_of_the_week()
    {
        Assert.Equal(new DateTime(2026, 11, 9), HistoryFilter.Since(HistoryRange.Week, Today.AddHours(22), DayOfWeek.Monday));
        Assert.Equal(new DateTime(2026, 11, 8), HistoryFilter.Since(HistoryRange.Week, Today.AddHours(22), DayOfWeek.Sunday));
        // On the first day itself, that day.
        Assert.Equal(new DateTime(2026, 11, 9), HistoryFilter.Since(HistoryRange.Week, new DateTime(2026, 11, 9, 7, 0, 0), DayOfWeek.Monday));
    }

    [Fact]
    public void This_month_starts_on_the_first() =>
        Assert.Equal(new DateTime(2026, 11, 1), HistoryFilter.Since(HistoryRange.Month, Today.AddHours(22), DayOfWeek.Monday));

    [Fact]
    public void Any_time_has_no_start() =>
        Assert.Null(HistoryFilter.Since(HistoryRange.All, Today, DayOfWeek.Monday));

    [Fact]
    public void A_time_range_leaves_out_older_pages()
    {
        Assert.Equal(new[] { "Rust book", "Hacker News" }, Titles(Find(range: HistoryRange.Today)));
        Assert.Equal(4, Titles(Find(range: HistoryRange.Week)).Length);
        Assert.DoesNotContain("Old news", Titles(Find(range: HistoryRange.Month)));
    }

    [Fact]
    public void By_site_a_group_per_site_the_latest_visited_first()
    {
        var days = Find(HistorySort.Site).Days;

        Assert.Equal(new[] { "doc.rust-lang.org", "news.ycombinator.com", "mail.google.com", "example.org" }, days.Select(d => d.Name));
        Assert.Equal(new[] { "Hacker News", "HN thread" }, days[1].Entries.Select(v => v.Title));
    }

    [Fact]
    public void By_most_visited_one_list_with_no_headings()
    {
        var found = Find(HistorySort.MostVisited);

        Assert.Equal(new[] { "Gmail", "Hacker News", "Old news", "Rust book", "HN thread" }, Titles(found));
        Assert.All(found.Days, d => Assert.Null(d.Name));
    }

    [Fact]
    public void By_last_visited_one_list_newest_first()
    {
        var found = Find(HistorySort.LastVisited);

        Assert.Equal(new[] { "Rust book", "Hacker News", "Gmail", "HN thread", "Old news" }, Titles(found));
        Assert.All(found.Days, d => Assert.Null(d.Name));
    }

    [Fact]
    public void A_list_with_no_headings_has_none_in_its_lines()
    {
        var lines = HistoryView.Lines(Find(HistorySort.LastVisited));

        Assert.All(lines, l => Assert.Null(l.Day));
        Assert.Equal(5, lines.Count);
    }

    [Fact]
    public void Only_so_many_however_it_is_sorted()
    {
        var found = Find(HistorySort.Site, limit: 2);

        Assert.Equal(new[] { "Rust book", "Hacker News" }, Titles(found));
        Assert.True(found.HasMore);
    }

    [Fact]
    public void A_closed_tab_from_before_closing_times_were_kept_is_under_earlier() =>
        Assert.Equal("Earlier", HistoryView.DayName(DateTime.MinValue, Today));

    [Fact]
    public void A_site_is_its_host_without_www()
    {
        Assert.Equal("example.org", HistoryView.Site("https://www.example.org/news"));
        Assert.Equal("not a url", HistoryView.Site("not a url"));
    }

    [Fact]
    public void Only_a_changed_filter_shows_as_on()
    {
        Assert.True(default(HistoryFilter).IsDefault);
        Assert.False(new HistoryFilter(Sort: HistorySort.Site).IsDefault);
        Assert.False(new HistoryFilter(Source: HistorySource.ClosedTabs).IsDefault);
    }

    [Fact]
    public void A_new_filter_rebuilds_the_list()
    {
        var shown = new HistoryListState(1, "", 150, Today, false);
        Assert.True(shown.NeedsRebuild(shown with { Filter = new HistoryFilter(Range: HistoryRange.Today) }));
    }
}
