using System;
using Drift;
using Xunit;

public class HistoryListTests
{
    private static readonly HistoryListState Shown = new(Version: 7, Query: "", Rows: 150, Today: new DateTime(2026, 11, 4), Dark: false);

    [Fact]
    public void Opening_the_library_again_with_nothing_new_keeps_the_list_it_has()
    {
        Assert.False(Shown.NeedsRebuild(Shown with { }));
    }

    [Fact]
    public void A_visit_a_search_more_rows_a_new_day_or_a_theme_change_rebuild_it()
    {
        Assert.True(Shown.NeedsRebuild(Shown with { Version = 8 }));
        Assert.True(Shown.NeedsRebuild(Shown with { Query = "arc" }));
        Assert.True(Shown.NeedsRebuild(Shown with { Rows = 300 }));
        Assert.True(Shown.NeedsRebuild(Shown with { Today = new DateTime(2026, 11, 5) }));
        Assert.True(Shown.NeedsRebuild(Shown with { Dark = true }));
    }

    [Fact]
    public void Nothing_shown_yet_is_always_built()
    {
        Assert.True(HistoryListState.Rebuild(null, Shown));
    }
}
