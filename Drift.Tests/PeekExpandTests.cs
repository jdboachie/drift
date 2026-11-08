using Drift;
using Xunit;

public class PeekExpandTests
{
    [Fact]
    public void The_page_is_laid_out_for_the_tab_once_its_width_in_css_pixels_matches()
    {
        // 1200 wide at 100%, it's 1200 CSS pixels; at 125% zoom, 960.
        Assert.Equal(1200, PeekExpand.CssWidth(1200, zoom: 1));
        Assert.Equal(960, PeekExpand.CssWidth(1200, zoom: 1.25));
    }

    [Fact]
    public void A_fraction_of_a_pixel_rounds_down_as_the_engine_does()
    {
        Assert.Equal(1199, PeekExpand.CssWidth(1199.6, zoom: 1));
    }

    [Fact]
    public void No_zoom_known_counts_as_100_percent()
    {
        Assert.Equal(800, PeekExpand.CssWidth(800, zoom: 0));
    }
}

public class PeekExpandEdgesTests
{
    [Fact]
    public void The_picture_is_held_exactly_where_the_cards_page_was_inside_the_page_sized_card()
    {
        // Card 96 in from the left and right of the column, the page 0 and 8: 96 and 88 in.
        Assert.Equal(new Edges(96, 0, 88, 0), PeekExpand.Inset(card: new Edges(96, 8, 96, 8), page: new Edges(0, 8, 8, 8)));
    }

    [Fact]
    public void A_card_already_as_big_as_the_page_is_not_inset()
    {
        Assert.Equal(new Edges(0, 0, 0, 0), PeekExpand.Inset(card: new Edges(0, 8, 8, 8), page: new Edges(0, 8, 8, 8)));
    }
}

public class PeekExpandSpringTests
{
    [Fact]
    public void The_card_overshoots_the_page_a_little_then_settles_like_peek_opening()
    {
        var spring = PeekExpand.Grow(travel: 1);
        var peak = 0.0;
        for (var t = 0; t < 1000; t++)
        {
            spring.Step(0.001);
            peak = System.Math.Max(peak, spring.Position);
        }
        // Alive but not prominent: 2–6% past the end, and back at it within a second.
        Assert.InRange(peak, 1.02, 1.06);
        Assert.InRange(spring.Position, 0.999, 1.001);
    }

    [Fact]
    public void It_leaves_at_once_rather_than_easing_out_of_the_peek()
    {
        Assert.True(PeekExpand.Grow(travel: 96).Velocity > 0);
    }

    [Fact]
    public void It_counts_as_settled_once_less_than_half_a_pixel_is_left()
    {
        var seconds = PeekExpand.SettleSeconds(travel: 96);
        Assert.InRange(seconds, 0.25, 0.5);
        var spring = PeekExpand.Grow(travel: 96);
        spring.Step(seconds);
        Assert.True(spring.AtRest);
    }

    [Fact]
    public void Nothing_to_travel_is_settled_at_once()
    {
        Assert.Equal(0, PeekExpand.SettleSeconds(travel: 0));
    }
}

public class PeekExpandBulgeTests
{
    [Fact]
    public void Past_its_end_each_edge_goes_out_by_its_own_inset_times_the_overshoot()
    {
        var bulge = PeekExpand.Bulge(new Edges(96, 0, 88, 0), width: 1000, height: 600);
        const double over = 0.1;
        var scaleX = 1 + bulge.PerX * over;
        // Scaled about the centre, the left edge moves out by CenterX * (scale - 1), the right by the rest.
        Assert.Equal(9.6, bulge.CenterX * (scaleX - 1), 6);
        Assert.Equal(8.8, (1000 - bulge.CenterX) * (scaleX - 1), 6);
        Assert.Equal(0, bulge.PerY);
    }

    [Fact]
    public void A_card_with_no_inset_doesnt_bulge()
    {
        var bulge = PeekExpand.Bulge(new Edges(0, 0, 0, 0), width: 1000, height: 600);
        Assert.Equal(0, bulge.PerX);
        Assert.Equal(0, bulge.PerY);
        Assert.Equal(500, bulge.CenterX);
        Assert.Equal(300, bulge.CenterY);
    }
}
