using System.Linq;
using Drift;
using Xunit;
using static Drift.CardPart;

namespace Drift.Tests;

public class FolderCardTests
{
    private static (int Depth, bool IsFolder, bool Open) Folder(int depth, bool open) => (depth, true, open);
    private static (int Depth, bool IsFolder, bool Open) Tab(int depth) => (depth, false, false);

    private static string Pieces(CardRow row) => string.Join(" ", row.Pieces.Select(p => $"{p.Level}:{p.Part}{(p.Rank > 0 ? p.Rank.ToString() : "")}"));

    [Fact]
    public void An_open_folder_and_its_tabs_make_one_card()
    {
        var rows = FolderCard.Layout([Folder(0, open: true), Tab(1), Tab(1), Tab(1)]);
        Assert.Equal(["0:Top", "0:Middle", "0:Middle", "0:Bottom1"], rows.Select(Pieces));
        Assert.Equal([0, 1, 1, 1], rows.Select(r => r.Inside));
        Assert.Equal([0, 0, 0, 1], rows.Select(r => r.Ends));
    }

    [Fact]
    public void An_open_folder_with_nothing_in_it_is_a_card_on_its_own()
    {
        Assert.Equal(["0:Only"], FolderCard.Layout([Folder(0, open: true)]).Select(Pieces));
    }

    [Fact]
    public void A_closed_folder_on_its_own_has_its_card_only_while_hovered()
    {
        Assert.Equal(["0:Hover"], FolderCard.Layout([Folder(0, open: false)]).Select(Pieces));
    }

    [Fact]
    public void A_closed_folder_still_showing_the_tab_in_front_is_a_card_with_it()
    {
        Assert.Equal(["0:Top", "0:Bottom1"], FolderCard.Layout([Folder(0, open: false), Tab(1)]).Select(Pieces));
    }

    [Fact]
    public void A_folder_inside_another_is_a_card_of_its_own_on_its_parents()
    {
        var rows = FolderCard.Layout([Folder(0, open: true), Folder(1, open: true), Tab(2), Folder(1, open: false), Tab(1)]);
        Assert.Equal(["0:Top", "0:Middle 1:Top", "0:Middle 1:Bottom1", "0:Middle 1:Hover", "0:Bottom1"], rows.Select(Pieces));
        Assert.Equal([0, 1, 2, 1, 1], rows.Select(r => r.Inside));
        Assert.Equal([0, 0, 1, 0, 1], rows.Select(r => r.Ends));
    }

    [Fact]
    public void Where_a_folder_ends_with_its_parent_both_end_under_its_last_row_the_parent_outside()
    {
        var rows = FolderCard.Layout([Folder(0, open: true), Folder(1, open: true), Tab(2)]);
        Assert.Equal("0:Bottom2 1:Bottom1", Pieces(rows[2]));
        Assert.Equal(2, rows[2].Ends);
    }

    [Fact]
    public void Each_folder_has_its_own_card()
    {
        var rows = FolderCard.Layout([Folder(0, open: true), Tab(1), Folder(0, open: false), Folder(0, open: true), Tab(1)]);
        Assert.Equal(["0:Top", "0:Bottom1", "0:Hover", "0:Top", "0:Bottom1"], rows.Select(Pieces));
    }

    [Fact]
    public void No_rows_no_cards() => Assert.Empty(FolderCard.Layout([]));
}
