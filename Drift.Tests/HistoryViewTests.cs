namespace Drift.Tests;

public class HistoryViewTests
{
    private sealed record Visit(string Title, string Url, DateTime When);

    private static readonly DateTime Today = new(2026, 11, 4);

    private static HistoryView.Result<Visit> Find(IEnumerable<Visit> visits, string query = "", int limit = 100) =>
        HistoryView.Find(visits, v => v.Title, v => v.Url, v => v.When, query, limit, Today);

    private static readonly Visit[] Visits =
    {
        new("Hacker News", "https://news.ycombinator.com/", Today.AddHours(9)),
        new("Rust book", "https://doc.rust-lang.org/book/", Today.AddHours(11)),
        new("Gmail", "https://mail.google.com/", Today.AddDays(-1).AddHours(20)),
        new("Old news", "https://example.org/news", new DateTime(2026, 10, 2, 8, 0, 0)),
    };

    [Fact]
    public void Newest_first_a_group_per_day_named_as_you_would_say_it()
    {
        var days = Find(Visits).Days;

        Assert.Equal(new[] { "Today", "Yesterday", "Friday 2 October" }, days.Select(d => d.Name));
        Assert.Equal(new[] { "Rust book", "Hacker News" }, days[0].Entries.Select(v => v.Title));
    }

    [Fact]
    public void A_search_matches_every_word_in_the_title_or_address_in_any_case()
    {
        var found = Find(Visits, "NEWS ycomb").Days.SelectMany(d => d.Entries);

        Assert.Equal(new[] { "Hacker News" }, found.Select(v => v.Title));
    }

    [Fact]
    public void Only_so_many_are_shown_at_first_and_it_says_there_are_more()
    {
        var result = Find(Visits, limit: 2);

        Assert.Equal(2, result.Days.Sum(d => d.Entries.Count));
        Assert.True(result.HasMore);
        Assert.False(Find(Visits).HasMore);
    }

    [Fact]
    public void Another_year_says_which()
    {
        Assert.Equal("Tuesday 4 November 2025", HistoryView.DayName(new DateTime(2025, 11, 4), Today));
    }
}
