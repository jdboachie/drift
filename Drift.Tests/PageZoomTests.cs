using Drift;
using Xunit;

public class PageZoomTests
{
    [Fact]
    public void The_zoom_is_the_pages_pixel_ratio_over_the_screens()
    {
        Assert.Equal(1.1, PageZoom.Of(devicePixelRatio: 1.1, screenScale: 1), 3);
        Assert.Equal(1.25, PageZoom.Of(devicePixelRatio: 1.875, screenScale: 1.5), 3);
        Assert.Equal(1, PageZoom.Of(devicePixelRatio: 1.5, screenScale: 1.5), 3);
    }

    [Fact]
    public void Ratios_off_by_a_rounding_settle_on_the_browsers_own_step()
    {
        // Chromium's pixel ratios carry float noise; 1.0999999 is its 110%.
        Assert.Equal(1.1, PageZoom.Of(1.0999999, 1), 6);
        Assert.Equal(0.67, PageZoom.Of(0.6666667, 1), 6);
        Assert.Equal(0.33, PageZoom.Of(0.3333333, 1), 6);
    }

    [Fact]
    public void It_reads_as_a_whole_percentage()
    {
        Assert.Equal("110%", PageZoom.Label(1.1));
        Assert.Equal("67%", PageZoom.Label(0.67));
        Assert.Equal("100%", PageZoom.Label(1));
    }

    [Fact]
    public void Only_a_zoom_away_from_100_percent_is_shown_in_the_address_pill()
    {
        Assert.False(PageZoom.IsZoomed(1));
        Assert.False(PageZoom.IsZoomed(1.0004));
        Assert.True(PageZoom.IsZoomed(0.9));
        Assert.True(PageZoom.IsZoomed(1.1));
    }
}
