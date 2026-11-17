using Drift;
using Xunit;

namespace Drift.Tests;

public class PeekLayoutTests
{
    [Fact]
    public void Card_takes_about_three_quarters_of_the_page()
    {
        // Arc: an 848-wide card in a 1152-wide page.
        var side = PeekLayout.CardSide(1152);
        Assert.InRange(1152 - 2 * side, 840, 856);
    }

    [Fact]
    public void Card_leaves_room_beside_it_for_the_buttons_on_a_narrow_page()
    {
        Assert.Equal(PeekLayout.MinSide, PeekLayout.CardSide(300));
        Assert.True(PeekLayout.MinSide >= PeekLayout.ButtonGap + PeekLayout.ButtonSize);
    }

    [Fact]
    public void Page_shrinks_by_eight_pixels_a_side()
    {
        var (scale, shift) = PeekLayout.PageShrink(1000, pixels: 1);
        Assert.Equal(984.0 / 1000, scale, 6);
        Assert.Equal(8, shift);
    }

    [Fact]
    public void Page_shrinks_to_whole_device_pixels()
    {
        // 150% scale: 801 DIPs are 1202 device pixels (rounded away from zero); 8 DIPs a side are 12.
        var (scale, shift) = PeekLayout.PageShrink(801, pixels: 1.5);
        Assert.Equal(1178.0 / 1202, scale, 6);
        Assert.Equal(12 / 1.5, shift, 6);
    }

    [Fact]
    public void Page_too_small_to_shrink_stays_as_it_is()
    {
        Assert.Equal((1.0, 0.0), PeekLayout.PageShrink(10, pixels: 1));
        Assert.Equal((1.0, 0.0), PeekLayout.PageShrink(0, pixels: 1));
    }
}
