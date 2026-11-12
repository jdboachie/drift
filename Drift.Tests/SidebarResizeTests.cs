using Drift;
using Xunit;

public class SidebarResizeTests
{
    [Fact]
    public void Between_the_limits_it_is_where_the_pointer_put_it()
    {
        Assert.Equal(300, SidebarResize.Follow(300));
        Assert.Equal(SidebarResize.Min, SidebarResize.Follow(SidebarResize.Min));
        Assert.Equal(SidebarResize.Max, SidebarResize.Follow(SidebarResize.Max));
    }

    [Fact]
    public void Past_the_widest_it_gives_a_little_with_resistance()
    {
        var past = SidebarResize.Follow(SidebarResize.Max + 100);
        Assert.InRange(past, SidebarResize.Max + 5, SidebarResize.Max + SidebarResize.Stretch);
        Assert.True(SidebarResize.Follow(SidebarResize.Max + 1000) < SidebarResize.Max + SidebarResize.Stretch);
        Assert.True(SidebarResize.Follow(SidebarResize.Max + 200) > past);
    }

    [Fact]
    public void Past_the_narrowest_too()
    {
        var past = SidebarResize.Follow(SidebarResize.Min - 50);
        Assert.InRange(past, SidebarResize.Min - SidebarResize.Stretch, SidebarResize.Min - 5);
    }

    [Fact]
    public void How_near_letting_go_would_hide_it()
    {
        Assert.Equal(0, SidebarResize.HideProgress(SidebarResize.Min), 3);
        Assert.Equal(0, SidebarResize.HideProgress(300), 3);
        Assert.Equal(1, SidebarResize.HideProgress(SidebarResize.HideAt), 3);
        Assert.Equal(1, SidebarResize.HideProgress(0), 3);
        Assert.InRange(SidebarResize.HideProgress((SidebarResize.Min + SidebarResize.HideAt) / 2), 0.4, 0.6);
    }

    [Fact]
    public void Let_go_inside_the_limits_it_stays()
    {
        Assert.Equal((false, 300.0), SidebarResize.LetGo(300, velocity: 0));
        // However fast it was moving: only a narrowed sidebar can be thrown shut.
        Assert.Equal((false, 260.0), SidebarResize.LetGo(260, velocity: -2000));
    }

    [Fact]
    public void Let_go_past_a_limit_it_springs_back_to_it()
    {
        Assert.Equal((false, SidebarResize.Max), SidebarResize.LetGo(SidebarResize.Max + 60, velocity: 0));
        Assert.Equal((false, SidebarResize.Min), SidebarResize.LetGo(SidebarResize.Min - 20, velocity: 0));
    }

    [Fact]
    public void Dragged_far_enough_shut_it_hides()
    {
        Assert.True(SidebarResize.LetGo(SidebarResize.HideAt - 1, velocity: 0).Hide);
        Assert.True(SidebarResize.LetGo(10, velocity: 0).Hide);
    }

    [Fact]
    public void Flicked_shut_from_just_past_the_narrowest_it_hides()
    {
        Assert.True(SidebarResize.LetGo(SidebarResize.Min - 10, velocity: -600).Hide);
        // Flicked back open, it doesn't.
        Assert.False(SidebarResize.LetGo(SidebarResize.HideAt + 10, velocity: 800).Hide);
    }

    [Fact]
    public void Past_the_point_of_hiding_it_hides_without_waiting_to_be_let_go()
    {
        Assert.True(SidebarResize.HidesNow(SidebarResize.HideAt));
        Assert.True(SidebarResize.HidesNow(20));
        Assert.False(SidebarResize.HidesNow(SidebarResize.HideAt + 1));
        Assert.False(SidebarResize.HidesNow(SidebarResize.Min - 30));
    }
}
