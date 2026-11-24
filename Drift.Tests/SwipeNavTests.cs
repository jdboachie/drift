using Drift;
using Xunit;

public class SwipeNavTests
{
    // A 1920-wide screen: a swipe goes through at 30% of it, 576.

    [Fact]
    public void Fingers_to_the_right_go_back()
    {
        var swipe = new SwipeNav(1920);
        swipe.Move(-10);
        Assert.Equal(-1, swipe.Direction);
    }

    [Fact]
    public void Fingers_to_the_left_go_forward()
    {
        var swipe = new SwipeNav(1920);
        swipe.Move(10);
        Assert.Equal(1, swipe.Direction);
    }

    [Fact]
    public void Its_way_is_set_by_its_first_movement()
    {
        var swipe = new SwipeNav(1920);
        swipe.Move(-100);
        swipe.Move(300);
        Assert.Equal(-1, swipe.Direction);
        Assert.False(swipe.Showing);
    }

    [Fact]
    public void Nothing_shows_for_the_first_10()
    {
        var swipe = new SwipeNav(1920);
        swipe.Move(-10);
        Assert.False(swipe.Showing);
        swipe.Move(-1);
        Assert.True(swipe.Showing);
    }

    [Fact]
    public void It_goes_through_at_30_percent_of_the_screen()
    {
        var swipe = new SwipeNav(1920);
        swipe.Move(-575);
        Assert.False(swipe.GoesThrough);
        swipe.Move(-1);
        Assert.True(swipe.GoesThrough);
        Assert.Equal(1, swipe.Progress, 3);
    }

    [Fact]
    public void Halfway_between_showing_and_going_through_is_half()
    {
        var swipe = new SwipeNav(1920);
        swipe.Move(10 + (576 - 10) / 2.0);
        Assert.Equal(0.5, swipe.Progress, 3);
    }

    [Fact]
    public void Going_back_the_other_way_never_goes_below_nothing()
    {
        var swipe = new SwipeNav(1920);
        swipe.Move(-200);
        swipe.Move(500);
        Assert.Equal(0, swipe.Progress);
        swipe.Move(-11);
        Assert.True(swipe.Showing);
    }

    [Fact]
    public void A_tiny_screen_still_needs_more_than_the_start()
    {
        var swipe = new SwipeNav(10);
        swipe.Move(-11);
        Assert.True(swipe.Progress is > 0 and <= 1);
    }

    [Fact]
    public void The_arrow_comes_in_146_by_the_time_it_would_go_through()
    {
        Assert.Equal(0, SwipeNav.ArrowOffset(0));
        Assert.Equal(73, SwipeNav.ArrowOffset(0.5), 3);
        Assert.Equal(146, SwipeNav.ArrowOffset(1), 3);
    }

    [Fact]
    public void Past_that_it_comes_at_most_72_further_and_slows()
    {
        var a = SwipeNav.ArrowOffset(1.1) - 146;
        var b = SwipeNav.ArrowOffset(1.2) - SwipeNav.ArrowOffset(1.1);
        Assert.True(a > 0 && b > 0);
        Assert.Equal(146 + 72, SwipeNav.ArrowOffset(1 + 72 / 146.0), 3);
        Assert.Equal(146 + 72, SwipeNav.ArrowOffset(5), 3);
        Assert.True(SwipeNav.ArrowOffset(1.45) - SwipeNav.ArrowOffset(1.4) < b);
    }

    [Fact]
    public void Fast_out_slow_in_starts_at_nothing_and_ends_at_all()
    {
        Assert.Equal(0, SwipeNav.FastOutSlowIn(0), 3);
        Assert.Equal(1, SwipeNav.FastOutSlowIn(1), 3);
        // Material's standard curve, cubic-bezier(0.4, 0, 0.2, 1): about 0.77 of the way at half the time.
        Assert.InRange(SwipeNav.FastOutSlowIn(0.5), 0.76, 0.79);
    }

    // Let go, as Drift's Library swipe (LibrarySwipe.Opens): a flick goes the way it was flicked; slowly, it goes
    // through only where the arrow says it will.

    /// <summary>A swipe of <paramref name="total"/> (fingers right) in steps of 10, a step every <paramref name="msPerStep"/>.</summary>
    private static SwipeNav Swiped(double total, double msPerStep, int direction = -1)
    {
        var swipe = new SwipeNav(1920);
        var ms = 0.0;
        for (var moved = 0.0; moved < total; moved += 10, ms += msPerStep) swipe.Move(10 * direction, ms);
        return swipe;
    }

    [Fact]
    public void A_flick_goes_through_before_the_arrow_is_all_the_way_in()
    {
        // 100 at 10 every 16ms: 625 a second.
        var swipe = Swiped(100, 16);
        Assert.True(swipe.Progress < 1);
        Assert.True(swipe.Speed > LibrarySwipe.FlickSpeed);
        Assert.True(swipe.GoesThrough);
    }

    [Fact]
    public void A_slow_swipe_short_of_the_end_springs_back()
    {
        // 100 at 10 every 50ms: 200 a second.
        var swipe = Swiped(100, 50);
        Assert.False(swipe.GoesThrough);
    }

    [Fact]
    public void A_slow_swipe_past_the_end_goes_through()
    {
        var swipe = Swiped(600, 50);
        Assert.True(swipe.GoesThrough);
    }

    [Fact]
    public void Flicked_back_the_other_way_it_doesnt_go_through_even_past_the_end()
    {
        var swipe = Swiped(700, 50);
        var ms = 70 * 50.0;
        for (var i = 0; i < 6; i++) swipe.Move(10, ms += 16);
        Assert.True(swipe.Progress >= 1);
        Assert.True(swipe.Speed < -LibrarySwipe.FlickSpeed);
        Assert.False(swipe.GoesThrough);
    }

    [Fact]
    public void Its_speed_is_over_the_last_tenth_of_a_second()
    {
        // Fast for 100, then slower for 300 more: let go slowly.
        var swipe = Swiped(100, 8);
        var ms = 10 * 8.0;
        for (var i = 0; i < 30; i++) swipe.Move(-10, ms += 60);
        Assert.InRange(swipe.Speed, 150, 190);
        Assert.False(swipe.GoesThrough);
    }

    [Fact]
    public void A_flick_too_small_to_show_the_arrow_does_nothing()
    {
        var swipe = new SwipeNav(1920);
        swipe.Move(-5, 0);
        swipe.Move(-5, 4);
        Assert.False(swipe.Showing);
        Assert.False(swipe.GoesThrough);
    }

    [Fact]
    public void The_arrow_moves_at_its_share_of_the_fingers_speed()
    {
        // Before the end, the arrow comes 146 over the fingers' 566 (10 to 576).
        var swipe = Swiped(200, 16);
        Assert.Equal(swipe.Speed * 146 / 566, swipe.ArrowSpeed, 3);
    }

    // Moving the page, as Safari does: WebKit's measures (ViewGestureController.cpp, SwipeProgressTrackerMac.mm).

    /// <summary>A page swipe on a page <paramref name="width"/> wide: <paramref name="total"/> in steps of 5, a step every <paramref name="msPerStep"/>.</summary>
    private static SwipeNav PageSwiped(double width, double total, double msPerStep)
    {
        var swipe = SwipeNav.ForPage(width);
        var ms = 0.0;
        for (var moved = 0.0; moved < total; moved += 5, ms += msPerStep) swipe.Move(-5, ms);
        return swipe;
    }

    [Fact]
    public void The_page_moves_with_the_fingers_after_the_first_15()
    {
        var swipe = SwipeNav.ForPage(1000);
        swipe.Move(-15);
        Assert.False(swipe.Showing);
        Assert.Equal(0, swipe.PageOffset);
        swipe.Move(-100);
        Assert.True(swipe.Showing);
        Assert.Equal(100, swipe.PageOffset);
    }

    [Fact]
    public void The_page_never_moves_past_its_own_width()
    {
        var swipe = SwipeNav.ForPage(1000);
        swipe.Move(-3000);
        Assert.Equal(1000, swipe.PageOffset);
    }

    [Fact]
    public void Slowly_the_page_goes_through_once_its_moved_187_and_a_half()
    {
        // 5 every 100ms: 50 a second, which carries it 15 further in 0.3s.
        Assert.False(PageSwiped(1000, 15 + 170, 100).GoesThrough);
        Assert.True(PageSwiped(1000, 15 + 175, 100).GoesThrough);
    }

    [Fact]
    public void On_a_narrow_page_half_its_width_is_enough()
    {
        Assert.True(PageSwiped(300, 15 + 140, 100).GoesThrough);
        Assert.False(PageSwiped(300, 15 + 130, 100).GoesThrough);
    }

    [Fact]
    public void A_flick_carries_the_page_on_three_tenths_of_a_second()
    {
        // 100 moved at 500 a second: it would get to 250.
        var swipe = PageSwiped(1000, 15 + 100, 10);
        Assert.InRange(swipe.Speed, 490, 510);
        Assert.True(swipe.GoesThrough);
    }

    [Fact]
    public void Moving_back_the_other_way_carries_it_back()
    {
        var swipe = PageSwiped(1000, 15 + 250, 100);
        var ms = 53 * 100.0;
        for (var i = 0; i < 10; i++) swipe.Move(5, ms += 10);
        Assert.True(swipe.PageOffset > 187.5);
        Assert.False(swipe.GoesThrough);
    }
}
