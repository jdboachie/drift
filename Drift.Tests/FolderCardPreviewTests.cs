using System.Collections.Generic;
using System.Linq;
using Drift;
using Xunit;

namespace Drift.Tests;

/// <summary>Mid-drag, the folders' cards fit the rows as they'll be once it's dropped.</summary>
public class FolderCardPreviewTests
{
    private static (object Key, int Depth, bool IsFolder, bool Open) Folder(string key, int depth) => (key, depth, true, true);
    private static (object Key, int Depth, bool IsFolder, bool Open) Tab(string key, int depth) => (key, depth, false, false);

    private static readonly (object Key, int Depth, bool IsFolder, bool Open)[] Work =
        [Folder("Work", 0), Tab("a", 1), Tab("b", 1), Tab("c", 1)];

    private static string Shown(IEnumerable<(object? Key, int Depth, CardRow Cards)> rows) =>
        string.Join(" | ", rows.Select(r => $"{r.Key ?? "new"}@{r.Depth} {string.Join(" ", r.Cards.Pieces.Select(p => $"{p.Level}:{p.Part}"))}"));

    [Fact]
    public void A_tab_moved_to_the_end_of_its_folder_takes_the_cards_end()
    {
        // b lifted: Work, a, c are slots 0–2; slot 3 is after c.
        var rows = FolderCard.Preview(Work, new HashSet<object> { "b" }, slot: 3, landingDepth: 1, arriving: false);
        Assert.Equal("Work@0 0:Top | a@1 0:Middle | c@1 0:Middle | b@1 0:Bottom", Shown(rows));
    }

    [Fact]
    public void A_tab_lifted_out_leaves_the_card_closed_up()
    {
        var rows = FolderCard.Preview(Work, new HashSet<object> { "c" }, slot: null, landingDepth: 0, arriving: false);
        Assert.Equal("Work@0 0:Top | a@1 0:Middle | b@1 0:Bottom", Shown(rows));
    }

    [Fact]
    public void A_tab_from_another_list_is_a_row_with_no_key_in_the_card()
    {
        var rows = FolderCard.Preview(Work, new HashSet<object>(), slot: 2, landingDepth: 1, arriving: true);
        Assert.Equal("Work@0 0:Top | a@1 0:Middle | new@1 0:Middle | b@1 0:Middle | c@1 0:Bottom", Shown(rows));
    }

    [Fact]
    public void A_tab_moved_to_another_folder_leaves_one_card_and_joins_the_other()
    {
        (object, int, bool, bool)[] two = [Folder("Work", 0), Tab("a", 1), Folder("Home", 0), Tab("h", 1)];
        var rows = FolderCard.Preview(two, new HashSet<object> { "a" }, slot: 3, landingDepth: 1, arriving: false);
        Assert.Equal("Work@0 0:Only | Home@0 0:Top | h@1 0:Middle | a@1 0:Bottom", Shown(rows));
    }

    // Rows 38 high, 44 apart; a card's bottom has 4 more room under it.
    private static List<(object? Key, double Top, double Height, CardRow Cards)> At(IReadOnlyList<(object? Key, int Depth, CardRow Cards)> rows) =>
        rows.Select((r, i) => (r.Key, i * 44.0, 38.0, r.Cards)).ToList();

    [Fact]
    public void A_card_runs_from_its_headers_top_to_its_last_rows_bottom_and_room()
    {
        var rows = FolderCard.Preview(Work, new HashSet<object>(), slot: null, landingDepth: 0, arriving: false);
        Assert.Equal([new CardSpan("Work", 0, 0, 3 * 44 + 38 + 4)], FolderCard.Spans(At(rows), room: 4));
    }

    [Fact]
    public void A_tab_moved_within_its_folder_leaves_the_card_where_it_was()
    {
        var still = FolderCard.Spans(At(FolderCard.Preview(Work, new HashSet<object>(), null, 0, false)), room: 4);
        var moved = FolderCard.Spans(At(FolderCard.Preview(Work, new HashSet<object> { "a" }, slot: 3, landingDepth: 1, arriving: false)), room: 4);
        Assert.Equal(still, moved);
    }

    [Fact]
    public void A_folder_in_a_folder_is_a_card_on_its_parents_with_a_step_more_room_under_the_outer()
    {
        (object, int, bool, bool)[] nested = [Folder("Work", 0), Folder("Docs", 1), Tab("d", 2)];
        var spans = FolderCard.Spans(At(FolderCard.Preview(nested, new HashSet<object>(), null, 0, false)), room: 4);
        // Outer cards first, as they're drawn (each over the one it's in).
        Assert.Equal([new CardSpan("Work", 0, 0, 88 + 38 + 8), new CardSpan("Docs", 1, 44, 88 + 38 + 4)], spans);
    }

    [Fact]
    public void An_open_folder_with_nothing_in_it_is_a_card_round_its_header()
    {
        (object, int, bool, bool)[] empty = [Folder("Work", 0)];
        Assert.Equal([new CardSpan("Work", 0, 0, 38)], FolderCard.Spans(At(FolderCard.Preview(empty, new HashSet<object>(), null, 0, false)), room: 4));
    }

    [Fact]
    public void A_folder_moved_into_another_takes_its_tabs_a_level_down()
    {
        (object, int, bool, bool)[] two = [Folder("Work", 0), Tab("a", 1), Folder("Home", 0), Tab("h", 1)];
        var rows = FolderCard.Preview(two, new HashSet<object> { "Home", "h" }, slot: 2, landingDepth: 1, arriving: false);
        Assert.Equal("Work@0 0:Top | a@1 0:Middle | Home@1 0:Middle 1:Top | h@2 0:Bottom 1:Bottom", Shown(rows));
    }
}
