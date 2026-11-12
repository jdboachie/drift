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

    [Fact]
    public void Into_a_split_the_card_spans_where_it_is_and_the_right_half()
    {
        // Card 96 in either side; the right half from 604 in to 8 from the right.
        var card = new Edges(96, 8, 96, 8);
        var half = new Edges(604, 8, 8, 8);
        var union = PeekExpand.Union(card, half);
        Assert.Equal(new Edges(96, 8, 8, 8), union);
        // Inside it: the card's page where it was, and the half at the right.
        Assert.Equal(new Edges(0, 0, 88, 0), PeekExpand.Inset(card, union));
        Assert.Equal(new Edges(508, 0, 0, 0), PeekExpand.Inset(half, union));
    }

    [Fact]
    public void Opening_as_a_tab_the_card_spans_the_page()
    {
        var page = new Edges(0, 8, 8, 8);
        Assert.Equal(page, PeekExpand.Union(new Edges(96, 8, 96, 8), page));
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
    public void Into_a_split_it_moves_on_peeks_spring_with_less_bounce()
    {
        static double Peak(SpringMotion spring)
        {
            var peak = 0.0;
            for (var t = 0; t < 1000; t++)
            {
                spring.Step(0.001);
                peak = System.Math.Max(peak, spring.Position);
            }
            return peak;
        }
        var split = PeekExpand.Grow(1, PeekExpand.SplitSeconds, PeekExpand.SplitBounce, PeekExpand.SplitKick);
        // Leaves at once, like Peek opening.
        Assert.True(split.Velocity > 0);
        var splitPeak = Peak(split);
        // Past the end, but less than opening as a tab: it's the biggest thing that moves.
        Assert.InRange(splitPeak, 1.0, Peak(PeekExpand.Grow(1)));
        Assert.True(splitPeak < 1.03);
        Assert.InRange(PeekExpand.SettleSeconds(508, PeekExpand.SplitSeconds, PeekExpand.SplitBounce, PeekExpand.SplitKick), 0.2, 0.6);
    }

    [Fact]
    public void Into_a_split_it_leaves_gently_enough_to_be_seen_going()
    {
        // The biggest thing that moves: a tenth of the way or less in its first frame, not a jump.
        var split = PeekExpand.Grow(1, PeekExpand.SplitSeconds, PeekExpand.SplitBounce, PeekExpand.SplitKick);
        split.Step(1.0 / 60);
        Assert.InRange(split.Position, 0.0, 0.1);
        Assert.True(PeekExpand.Grow(1, PeekExpand.SplitSeconds, PeekExpand.SplitBounce, PeekExpand.SplitKick).Velocity
            < PeekExpand.Grow(1).Velocity);
    }

    [Fact]
    public void Into_a_split_the_page_fades_in_once_the_card_is_all_but_there_not_after_its_tail()
    {
        var arrive = PeekExpand.ArriveSeconds(travel: 508, within: 2);
        var spring = PeekExpand.Grow(508, PeekExpand.SplitSeconds, PeekExpand.SplitBounce, PeekExpand.SplitKick);
        spring.Step(arrive);
        Assert.True(508 - spring.Position <= 2.5);
        Assert.InRange(arrive, 0.08, 0.3);
        Assert.True(arrive < PeekExpand.SettleSeconds(508, PeekExpand.SplitSeconds, PeekExpand.SplitBounce, PeekExpand.SplitKick));
        Assert.Equal(0, PeekExpand.ArriveSeconds(travel: 1, within: 2));
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
