using Drift;

public class SplitSharesTests
{
    private static void Near(double[] expected, double[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++) Assert.Equal(expected[i], actual[i], 3);
    }

    [Fact]
    public void A_page_joining_takes_an_even_share_and_the_others_keep_their_proportions()
    {
        // Two at 60/40, a third on the right: each of the two gives up a third of its share.
        Near([0.4, 0.2667, 0.3333], SplitShares.Insert([0.6, 0.4], at: 2));
        Near([0.3333, 0.4, 0.2667], SplitShares.Insert([0.6, 0.4], at: 0));
        Near([0.25, 0.25, 0.25, 0.25], SplitShares.Insert([1 / 3.0, 1 / 3.0, 1 / 3.0], at: 1));
    }

    [Fact]
    public void A_page_leaving_gives_its_share_back_in_proportion()
    {
        Near([0.6, 0.4], SplitShares.Remove([0.4, 0.2667, 0.3333], at: 2));
        Near([0.5, 0.5], SplitShares.Remove([0.25, 0.5, 0.25], at: 1));
    }

    [Fact]
    public void Moving_a_page_keeps_the_others_in_order()
    {
        Assert.Equal(["b", "c", "a", "d"], SplitShares.Moved(["a", "b", "c", "d"], from: 0, to: 2));
        Assert.Equal(["d", "a", "b", "c"], SplitShares.Moved(["a", "b", "c", "d"], from: 3, to: 0));
    }

    [Fact]
    public void Widths_leave_a_gap_between_each_two_pages()
    {
        // 1016 wide, three pages, two 8px gaps: 1000 to share.
        var widths = SplitShares.Widths([0.5, 0.3, 0.2], 1016, gap: 8);
        Near([500, 300, 200], widths);
        Near([0, 508, 816], SplitShares.Lefts(widths, gap: 8));
    }

    [Fact]
    public void A_divider_shares_out_only_the_two_pages_beside_it()
    {
        // Three pages of 1000: the second divider sits at 808 + 4. Pulled to 700, the middle page
        // shrinks to 192 and the last grows; the first doesn't move.
        var shares = SplitShares.Resized([0.5, 0.3, 0.2], divider: 1, x: 704, width: 1016, gap: 8, minPane: 100, stretch: false);
        Near([0.5, 0.192, 0.308], shares);
    }

    [Fact]
    public void With_two_pages_a_divider_works_as_before()
    {
        var shares = SplitShares.Resized([0.5, 0.5], divider: 0, x: 404, width: 1008, gap: 8, minPane: 200, stretch: false);
        Near([0.4, 0.6], shares);
        Assert.Equal(SplitLayout.Stretched(100, 1000, 200), SplitShares.Resized([0.5, 0.5], 0, 104, 1008, 8, 200, stretch: true)[0], 6);
    }

    [Fact]
    public void Neither_page_beside_a_divider_gets_narrower_than_the_minimum()
    {
        // The first two pages share 800; pulled right to the end, the second keeps 200.
        var shares = SplitShares.Resized([0.4, 0.4, 0.2], divider: 0, x: 990, width: 1016, gap: 8, minPane: 200, stretch: false);
        Near([0.6, 0.2, 0.2], shares);
    }

    [Fact]
    public void Let_go_stretched_every_page_comes_back_to_its_minimum()
    {
        var stretched = new[] { 0.19, 0.61, 0.2 };
        Near([0.2, 0.6, 0.2], SplitShares.Inside(stretched, 1016, gap: 8, minPane: 200));
    }

    [Fact]
    public void Double_clicking_a_divider_evens_out_the_two_pages_beside_it()
    {
        Near([0.5, 0.25, 0.25], SplitShares.Evened([0.5, 0.1, 0.4], divider: 1));
    }

    [Fact]
    public void Saved_shares_that_dont_fit_the_pages_come_back_even()
    {
        Near([0.5, 0.5], SplitShares.Checked(null, 2));
        Near([1 / 3.0, 1 / 3.0, 1 / 3.0], SplitShares.Checked([0.5, 0.5], 3));
        Near([0.5, 0.5], SplitShares.Checked([0.5, -1], 2));
        Near([0.25, 0.75], SplitShares.Checked([1, 3], 2));
    }

    [Fact]
    public void Saved_splits_come_back_top_row_first_with_up_to_four_pages()
    {
        var splits = SplitShares.Restorable([([4, 5, 6], 3), ([0, 1, 2, 3, 7], 0)], tabCount: 8);
        Assert.Equal(2, splits.Count);
        Assert.Equal([0, 1, 2, 3], splits[0].Pages);
        Assert.Equal(1, splits[0].Index);
        Assert.Equal([4, 5, 6], splits[1].Pages);
        Assert.Equal(0, splits[1].Index);
    }

    [Fact]
    public void A_saved_split_left_with_one_page_is_dropped()
    {
        // 9 is missing; 2 is in the split above already, leaving 3 alone, and 4 alone.
        var splits = SplitShares.Restorable([([0, 9], 0), ([1, 2], 1), ([2, 3, 2], 2), ([2, 4], 3)], tabCount: 6);
        var only = Assert.Single(splits);
        Assert.Equal([1, 2], only.Pages);
        Assert.Equal(1, only.Row);
    }
}

public class PageMoveTests
{
    // Two pages: left 600 wide, right 400, 8 between them (as swapping always was).
    private static readonly PageMove Left = new([600, 400], dragged: 0, gap: 8);
    private static readonly PageMove Right = new([600, 400], dragged: 1, gap: 8);

    [Fact]
    public void A_page_dragged_toward_the_other_follows_the_pointer_one_to_one()
    {
        Assert.Equal(150, Left.Follow(150));
        Assert.Equal(-150, Right.Follow(-150));
    }

    [Fact]
    public void Past_either_end_it_gives_with_resistance()
    {
        // The left page lands 408 over (the right page's width and the gap): pulled further, it gives less and less.
        var past = Left.Follow(408 + 100);
        Assert.InRange(past, 408, 408 + PageMove.Stretch);
        Assert.True(Left.Follow(408 + 200) > past);
        Assert.InRange(Left.Follow(-100), -PageMove.Stretch, 0);
    }

    [Fact]
    public void Once_its_middle_crosses_the_divider_the_other_page_makes_room()
    {
        // The left page's middle is at 300, the divider's at 604: 304 to go.
        Assert.Equal(0, Left.Place(300));
        Assert.Equal(1, Left.Place(305));
        Assert.Equal(1, Right.Place(-200));
        Assert.Equal(0, Right.Place(-205));
    }

    [Fact]
    public void Making_room_the_other_page_moves_to_where_the_dragged_one_was()
    {
        Assert.Equal(-608, Left.MakesRoom(page: 1, to: 1));
        Assert.Equal(408, Right.MakesRoom(page: 0, to: 0));
        Assert.Equal(0, Left.MakesRoom(page: 1, to: 0));
    }

    [Fact]
    public void Each_keeps_its_width_so_the_dragged_page_lands_beyond_the_other()
    {
        Assert.Equal(408, Left.Lands(1));
        Assert.Equal(-608, Right.Lands(0));
    }

    [Fact]
    public void A_flick_carries_it_across_short_of_the_divider()
    {
        Assert.Equal(1, Left.LandsIn(150, velocity: 1000));
        Assert.Equal(0, Left.LandsIn(350, velocity: -1000));
        Assert.Equal(1, Left.LandsIn(350, velocity: 0));
    }

    // Three pages, 300 each with 8 between: 0–300, 308–608, 616–916.
    private static readonly PageMove First = new([300, 300, 300], dragged: 0, gap: 8);
    private static readonly PageMove Middle = new([300, 300, 300], dragged: 1, gap: 8);

    [Fact]
    public void Among_three_it_passes_one_page_per_divider_its_middle_crosses()
    {
        // The first page's middle (150) crosses the dividers at 304 and 612.
        Assert.Equal(0, First.Place(150));
        Assert.Equal(1, First.Place(160));
        Assert.Equal(1, First.Place(460));
        Assert.Equal(2, First.Place(465));
        // The middle page goes either way.
        Assert.Equal(0, Middle.Place(-160));
        Assert.Equal(2, Middle.Place(160));
        Assert.Equal(1, Middle.Place(100));
    }

    [Fact]
    public void Among_three_the_pages_it_passes_shift_over_by_its_width()
    {
        // The first page bound for the end: both others move left 308; it lands 616 over.
        Assert.Equal(-308, First.MakesRoom(page: 1, to: 2));
        Assert.Equal(-308, First.MakesRoom(page: 2, to: 2));
        Assert.Equal(616, First.Lands(2));
        // Bound for the middle only the second moves.
        Assert.Equal(0, First.MakesRoom(page: 2, to: 1));
    }

    [Fact]
    public void Among_three_it_can_be_pulled_from_one_end_to_the_other()
    {
        Assert.Equal(-308, Middle.Follow(-308));
        Assert.Equal(308, Middle.Follow(308));
        Assert.InRange(Middle.Follow(400), 308, 308 + PageMove.Stretch);
    }
}

public class SessionSplitTests
{
    [Fact]
    public void A_split_saved_by_an_older_Drift_comes_back_as_its_two_pages()
    {
        var old = System.Text.Json.JsonSerializer.Deserialize<SessionSplit>("""{"Left":2,"Right":3,"Row":1,"Ratio":0.4}""")!;
        Assert.Equal([2, 3], old.PageList());
        Assert.Equal([0.4, 0.6], old.ShareList()!);
    }

    [Fact]
    public void A_split_is_saved_with_all_its_pages_and_their_widths_only()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new SessionSplit { Pages = [0, 1, 2], Shares = [0.5, 0.25, 0.25], Row = 4 });
        Assert.DoesNotContain("Left", json);
        Assert.DoesNotContain("Ratio", json);
        var back = System.Text.Json.JsonSerializer.Deserialize<SessionSplit>(json)!;
        Assert.Equal([0, 1, 2], back.PageList());
        Assert.Equal([0.5, 0.25, 0.25], back.ShareList()!);
    }
}
