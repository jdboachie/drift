using Drift;
using Xunit;

public class PeekDragTests
{
    [Fact]
    public void Down_the_card_follows_the_pointer_exactly()
    {
        Assert.Equal(120, PeekDrag.Follow(120));
    }

    [Fact]
    public void Up_it_gives_a_little_with_resistance()
    {
        var up = PeekDrag.Follow(-100);
        Assert.InRange(up, -PeekDrag.Stretch, -5);
        Assert.True(PeekDrag.Follow(-1000) > -PeekDrag.Stretch);
        Assert.True(PeekDrag.Follow(-200) < up);
    }

    [Fact]
    public void It_shrinks_a_touch_as_it_goes_down()
    {
        Assert.Equal(1, PeekDrag.Scale(0, 800), 3);
        Assert.True(PeekDrag.Scale(400, 800) < 1);
        Assert.Equal(1 - PeekDrag.Shrink, PeekDrag.Scale(5000, 800), 3);
        Assert.Equal(1, PeekDrag.Scale(-20, 800), 3);
    }

    [Fact]
    public void Momentum_is_projected_as_scrolling_decelerates()
    {
        // Apple's: (v / 1000) · d / (1 − d), d = 0.998.
        Assert.Equal(499, PeekDrag.Project(1000), 0);
        Assert.Equal(0, PeekDrag.Project(0), 3);
    }

    [Fact]
    public void Let_go_slowly_far_enough_down_it_closes()
    {
        Assert.True(PeekDrag.Dismisses(300, velocity: 0, height: 800));
        Assert.False(PeekDrag.Dismisses(100, velocity: 0, height: 800));
    }

    [Fact]
    public void A_flick_down_closes_it_from_just_a_little_way()
    {
        Assert.True(PeekDrag.Dismisses(20, velocity: 800, height: 800));
    }

    [Fact]
    public void Flicked_back_up_it_stays()
    {
        Assert.False(PeekDrag.Dismisses(300, velocity: -800, height: 800));
    }

    [Fact]
    public void Pulled_up_it_stays()
    {
        Assert.False(PeekDrag.Dismisses(-20, velocity: 0, height: 800));
    }

    [Fact]
    public void Velocity_comes_from_the_last_moments_of_the_drag()
    {
        var tracker = new VelocityTracker();
        tracker.Add(0, 0);
        tracker.Add(500, 0);   // held still for a while
        tracker.Add(550, 25);
        tracker.Add(600, 50);  // then 50px in 100ms
        Assert.Equal(500, tracker.Velocity(600), 0);
    }

    [Fact]
    public void Held_still_before_letting_go_is_no_velocity()
    {
        var tracker = new VelocityTracker();
        tracker.Add(0, 0);
        tracker.Add(50, 40);
        Assert.Equal(0, tracker.Velocity(400), 3);
    }

    [Fact]
    public void One_sample_is_no_velocity()
    {
        var tracker = new VelocityTracker();
        tracker.Add(0, 10);
        Assert.Equal(0, tracker.Velocity(0), 3);
    }
}
