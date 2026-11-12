using Drift;
using Xunit;

public class CaptionRevealTests
{
    // A 1200×800 window, the page starting 216 in (the sidebar) and 8 down, buttons 138×32 in the corner.
    private static readonly CaptionLayout Layout = new(Width: 1200, PageLeft: 216, PageTop: 8, BarWidth: 138, BarHeight: 32);

    private static PointerAt At(double x, double y, bool shown = false) => Layout.Locate((x, y), shown);

    [Fact]
    public void The_top_of_the_page_area_is_the_hot_zone()
    {
        Assert.Equal(PointerAt.Top, At(600, 0));
        Assert.Equal(PointerAt.Top, At(600, 10)); // a few pixels into the page too
        Assert.Equal(PointerAt.Away, At(600, 40));
    }

    [Fact]
    public void Not_over_the_sidebar()
    {
        Assert.Equal(PointerAt.Away, At(100, 0));
    }

    [Fact]
    public void Under_where_the_buttons_will_be_counts_as_heading_for_them()
    {
        Assert.Equal(PointerAt.TopNearButtons, At(1150, 2));
        Assert.Equal(PointerAt.TopNearButtons, At(1050, 2));
    }

    [Fact]
    public void Once_shown_the_bar_and_a_margin_round_it_count()
    {
        Assert.Equal(PointerAt.Bar, At(1150, 30, shown: true));
        Assert.Equal(PointerAt.Bar, At(1052, 40, shown: true));
        Assert.Equal(PointerAt.Away, At(1150, 60, shown: true));
        // Hidden, it isn't there to be over.
        Assert.Equal(PointerAt.Away, At(1150, 30));
    }

    [Fact]
    public void No_pointer_is_away()
    {
        Assert.Equal(PointerAt.Away, Layout.Locate(null, shown: true));
    }

    [Fact]
    public void Resting_at_the_top_shows_it_after_a_moment()
    {
        var reveal = new CaptionReveal();
        Assert.Equal(CaptionChange.None, reveal.Update(0, PointerAt.Top));
        Assert.Equal(CaptionChange.None, reveal.Update(CaptionReveal.RestMs - 10, PointerAt.Top));
        Assert.Equal(CaptionChange.Show, reveal.Update(CaptionReveal.RestMs, PointerAt.Top));
        Assert.True(reveal.Shown);
    }

    [Fact]
    public void Passing_through_the_top_does_not()
    {
        var reveal = new CaptionReveal();
        reveal.Update(0, PointerAt.Top);
        reveal.Update(60, PointerAt.Away);
        reveal.Update(100, PointerAt.Top);
        Assert.Equal(CaptionChange.None, reveal.Update(100 + CaptionReveal.RestMs - 10, PointerAt.Top));
    }

    [Fact]
    public void Heading_for_the_buttons_shows_it_sooner()
    {
        var reveal = new CaptionReveal();
        reveal.Update(0, PointerAt.TopNearButtons);
        Assert.Equal(CaptionChange.Show, reveal.Update(CaptionReveal.CornerRestMs, PointerAt.TopNearButtons));
    }

    [Fact]
    public void Not_while_a_mouse_button_is_held()
    {
        // Dragging the window by its top edge, or a selection dragged up.
        var reveal = new CaptionReveal();
        reveal.Update(0, PointerAt.Top, buttonDown: true);
        Assert.Equal(CaptionChange.None, reveal.Update(500, PointerAt.Top, buttonDown: true));
        // The wait starts on release.
        Assert.Equal(CaptionChange.None, reveal.Update(510, PointerAt.Top));
        Assert.Equal(CaptionChange.Show, reveal.Update(510 + CaptionReveal.RestMs, PointerAt.Top));
    }

    [Fact]
    public void Not_while_blocked()
    {
        var reveal = new CaptionReveal();
        reveal.Update(0, PointerAt.TopNearButtons, blocked: true);
        Assert.Equal(CaptionChange.None, reveal.Update(1000, PointerAt.TopNearButtons, blocked: true));
    }

    [Fact]
    public void It_stays_while_the_pointer_is_on_it_or_at_the_top()
    {
        var reveal = Shown();
        Assert.Equal(CaptionChange.None, reveal.Update(1000, PointerAt.Bar));
        Assert.Equal(CaptionChange.None, reveal.Update(5000, PointerAt.Top));
        Assert.Equal(CaptionChange.None, reveal.Update(9000, PointerAt.Bar, buttonDown: true));
    }

    [Fact]
    public void Leaving_hides_it_after_a_grace_period()
    {
        var reveal = Shown();
        Assert.Equal(CaptionChange.None, reveal.Update(1000, PointerAt.Away));
        Assert.Equal(CaptionChange.None, reveal.Update(1000 + CaptionReveal.LeaveMs - 10, PointerAt.Away));
        Assert.Equal(CaptionChange.Hide, reveal.Update(1000 + CaptionReveal.LeaveMs, PointerAt.Away));
        Assert.False(reveal.Shown);
    }

    [Fact]
    public void Coming_back_within_the_grace_period_keeps_it()
    {
        var reveal = Shown();
        reveal.Update(1000, PointerAt.Away);
        reveal.Update(1200, PointerAt.Bar);
        Assert.Equal(CaptionChange.None, reveal.Update(1000 + CaptionReveal.LeaveMs + 50, PointerAt.Away));
    }

    [Fact]
    public void Blocked_hides_it_at_once()
    {
        var reveal = Shown();
        Assert.Equal(CaptionChange.Hide, reveal.Update(1000, PointerAt.Bar, blocked: true));
    }

    [Fact]
    public void Hide_forgets_it()
    {
        var reveal = Shown();
        reveal.Reset();
        Assert.False(reveal.Shown);
        Assert.Equal(CaptionChange.None, reveal.Update(1000, PointerAt.Top));
    }

    private static CaptionReveal Shown()
    {
        var reveal = new CaptionReveal();
        reveal.Update(0, PointerAt.TopNearButtons);
        reveal.Update(CaptionReveal.CornerRestMs, PointerAt.TopNearButtons);
        Assert.True(reveal.Shown);
        return reveal;
    }
}
