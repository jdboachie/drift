using Drift;
using Xunit;

public class SplitLayoutTests
{
    [Fact]
    public void A_tab_dropped_on_the_left_half_goes_on_the_left()
    {
        Assert.Equal(SplitSide.Left, SplitLayout.Side(100, 1000));
        Assert.Equal(SplitSide.Right, SplitLayout.Side(600, 1000));
    }

    [Fact]
    public void A_saved_split_pairs_a_tab_with_the_one_before_it()
    {
        Assert.Equal([(0, 1), (3, 4)], SplitLayout.Pairs([false, true, false, false, true]));
    }

    [Fact]
    public void A_split_with_nothing_to_pair_with_is_left_as_a_tab()
    {
        // The first tab has nothing before it; a third can't join a pair that's already two.
        Assert.Empty(SplitLayout.Pairs([true, false]));
        Assert.Equal([(0, 1)], SplitLayout.Pairs([false, true, true]));
    }

    [Fact]
    public void The_drop_outline_covers_the_half_it_would_take()
    {
        // 1000 wide with an 8 gap: each half is 496, the right one starting after the gap.
        Assert.Equal(496, SplitLayout.HalfWidth(1000, gap: 8), 3);
        Assert.Equal(0, SplitLayout.HalfOffset(SplitSide.Left, 1000, gap: 8), 3);
        Assert.Equal(504, SplitLayout.HalfOffset(SplitSide.Right, 1000, gap: 8), 3);
    }

    [Fact]
    public void The_divider_follows_the_pointer()
    {
        Assert.Equal(0.4, SplitLayout.Ratio(400, 1000, minPane: 200), 3);
    }

    [Fact]
    public void Neither_page_gets_narrower_than_the_minimum()
    {
        Assert.Equal(0.2, SplitLayout.Ratio(50, 1000, minPane: 200), 3);
        Assert.Equal(0.8, SplitLayout.Ratio(990, 1000, minPane: 200), 3);
    }

    [Fact]
    public void Dragged_past_the_minimum_it_gives_a_little_with_resistance()
    {
        // Within the limits, it's where the pointer is.
        Assert.Equal(0.4, SplitLayout.Stretched(400, 1000, minPane: 200), 3);
        // 100px past the left limit: it goes past, but by much less, and never by Stretch or more.
        var past = SplitLayout.Stretched(100, 1000, minPane: 200) * 1000;
        Assert.InRange(past, 200 - SplitLayout.Stretch, 195);
        Assert.True(SplitLayout.Stretched(-5000, 1000, minPane: 200) * 1000 > 200 - SplitLayout.Stretch);
        // Further out, further past: it never stops dead.
        Assert.True(SplitLayout.Stretched(50, 1000, minPane: 200) < SplitLayout.Stretched(100, 1000, minPane: 200));
        // The same on the right.
        Assert.InRange(SplitLayout.Stretched(900, 1000, minPane: 200) * 1000, 805, 800 + SplitLayout.Stretch);
    }

    [Fact]
    public void Too_narrow_to_stretch_the_pages_share_evenly()
    {
        Assert.Equal(0.5, SplitLayout.Stretched(100, 300, minPane: 200), 3);
    }

    [Fact]
    public void Too_narrow_for_both_minimums_the_pages_share_evenly()
    {
        Assert.Equal(0.5, SplitLayout.Ratio(100, 300, minPane: 200), 3);
        Assert.Equal(0.5, SplitLayout.Ratio(100, 0, minPane: 200), 3);
    }
}

public class SplitDragPreviewTests
{
    // 1000 wide with an 8 gap: each half 496, the right one starting at 504.

    [Fact]
    public void Dragging_over_the_left_half_moves_the_page_over_to_the_right_half()
    {
        Assert.Equal(496, SplitLayout.PreviewWidth(SplitSide.Left, 1000, gap: 8), 3);
        Assert.Equal(504, SplitLayout.PreviewOffset(SplitSide.Left, 1000, gap: 8), 3);
    }

    [Fact]
    public void Dragging_over_the_right_half_leaves_the_page_on_the_left_half()
    {
        Assert.Equal(496, SplitLayout.PreviewWidth(SplitSide.Right, 1000, gap: 8), 3);
        Assert.Equal(0, SplitLayout.PreviewOffset(SplitSide.Right, 1000, gap: 8), 3);
    }

    [Fact]
    public void Not_over_the_page_it_takes_the_whole_width()
    {
        Assert.Equal(1000, SplitLayout.PreviewWidth(null, 1000, gap: 8), 3);
        Assert.Equal(0, SplitLayout.PreviewOffset(null, 1000, gap: 8), 3);
    }
}

public class SplitRowTests
{
    [Fact]
    public void A_new_split_takes_the_place_of_the_higher_of_its_rows()
    {
        Assert.Equal(1, SplitLayout.RowIndex(leftAt: 3, rightAt: 1, count: 5));
        Assert.Equal(2, SplitLayout.RowIndex(leftAt: 2, rightAt: null, count: 5));
    }

    [Fact]
    public void Two_tabs_with_no_row_in_the_list_get_a_new_row_at_the_end()
    {
        // Two pinned tabs: they keep their own rows, and the split's row is added to the tab list.
        Assert.Equal(5, SplitLayout.RowIndex(leftAt: null, rightAt: null, count: 5));
    }

    [Fact]
    public void Saved_splits_come_back_top_row_first()
    {
        var splits = SplitLayout.Restorable([(Left: 4, Right: 5, Row: 3), (Left: 0, Right: 1, Row: 0)], tabCount: 6);
        Assert.Equal([(0, 1, 0), (4, 5, 3)], splits);
    }

    [Fact]
    public void A_saved_split_whose_tabs_are_missing_or_shared_is_dropped()
    {
        var splits = SplitLayout.Restorable(
            [(Left: 0, Right: 9, Row: 0), (Left: 2, Right: 2, Row: 1), (Left: 3, Right: 4, Row: 2), (Left: 4, Right: 5, Row: 3)],
            tabCount: 6);
        Assert.Equal([(3, 4, 2)], splits);
    }
}
