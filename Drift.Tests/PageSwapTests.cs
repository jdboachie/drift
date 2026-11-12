using Drift;

public class PageSwapTests
{
    // Left page 600 wide, right 400, 8 between them.
    private static readonly PageSwap Left = new(draggedIsLeft: true, leftWidth: 600, rightWidth: 400, gap: 8);
    private static readonly PageSwap Right = new(draggedIsLeft: false, leftWidth: 600, rightWidth: 400, gap: 8);

    [Fact]
    public void A_page_dragged_toward_the_other_follows_the_pointer_one_to_one()
    {
        Assert.Equal(150, Left.Follow(150));
        Assert.Equal(-150, Right.Follow(-150));
    }

    [Fact]
    public void Past_where_it_would_land_it_gives_with_resistance()
    {
        // The left page lands 408 over (the right page's width and the gap): pulled further, it gives less and less.
        var past = Left.Follow(408 + 100);
        Assert.InRange(past, 408, 408 + PageSwap.Stretch);
        Assert.True(Left.Follow(408 + 200) > past);
        // Pulled the wrong way, the same.
        Assert.InRange(Left.Follow(-100), -PageSwap.Stretch, 0);
    }

    [Fact]
    public void Once_its_middle_crosses_the_divider_the_other_page_makes_room()
    {
        // The left page's middle is at 300, the divider's at 604: 304 to go.
        Assert.False(Left.Crossed(300));
        Assert.True(Left.Crossed(305));
        // The right page's middle is 204 from the divider, the other way.
        Assert.False(Right.Crossed(-200));
        Assert.True(Right.Crossed(-205));
    }

    [Fact]
    public void Making_room_the_other_page_moves_to_where_the_dragged_one_was()
    {
        // The right page goes to the left edge; the left page to the right edge.
        Assert.Equal(-608, Left.OtherMakesRoom);
        Assert.Equal(408, Right.OtherMakesRoom);
    }

    [Fact]
    public void Each_keeps_its_width_so_the_dragged_page_lands_beyond_the_other()
    {
        Assert.Equal(408, Left.Lands);
        Assert.Equal(-608, Right.Lands);
    }

    [Fact]
    public void Let_go_across_the_divider_they_swap()
    {
        Assert.True(Left.Swaps(350, velocity: 0));
        Assert.False(Left.Swaps(250, velocity: 0));
    }

    [Fact]
    public void A_flick_toward_the_other_side_swaps_short_of_the_divider()
    {
        // 150 over, flicked right fast enough to carry it past the divider.
        Assert.True(Left.Swaps(150, velocity: 1000));
        // Across, but flicked back: it goes back.
        Assert.False(Left.Swaps(350, velocity: -1000));
        Assert.True(Right.Swaps(-100, velocity: -1000));
    }
}
