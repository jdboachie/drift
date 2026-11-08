using Drift;
using Xunit;

public class MediaTimeTests
{
    [Fact]
    public void Under_an_hour_it_reads_minutes_and_seconds()
    {
        Assert.Equal("0:19", MediaTime.Format(19));
        Assert.Equal("1:05", MediaTime.Format(65.9));
    }

    [Fact]
    public void An_hour_or_more_reads_hours_minutes_and_seconds()
    {
        Assert.Equal("2:56:20", MediaTime.Format(2 * 3600 + 56 * 60 + 20));
        Assert.Equal("1:00:00", MediaTime.Format(3600));
    }

    [Fact]
    public void A_time_that_is_not_known_reads_zero()
    {
        Assert.Equal("0:00", MediaTime.Format(double.NaN));
        Assert.Equal("0:00", MediaTime.Format(-3));
        Assert.Equal("0:00", MediaTime.Format(double.PositiveInfinity));
    }

    [Fact]
    public void The_bar_fills_by_how_far_through_it_is()
    {
        Assert.Equal(0.25, MediaTime.Fraction(30, 120), 6);
        Assert.Equal(1, MediaTime.Fraction(200, 120), 6);
        // A live stream (no length): an empty bar.
        Assert.Equal(0, MediaTime.Fraction(30, 0), 6);
    }

    [Fact]
    public void Pressing_the_bar_seeks_to_that_point()
    {
        Assert.Equal(60, MediaTime.At(x: 50, width: 100, duration: 120), 6);
        Assert.Equal(0, MediaTime.At(x: -10, width: 100, duration: 120), 6);
        Assert.Equal(120, MediaTime.At(x: 150, width: 100, duration: 120), 6);
    }
}

public class MediaMarqueeTests
{
    [Fact]
    public void A_title_that_fits_stays_still()
    {
        Assert.False(MediaMarquee.Scrolls(textWidth: 120, width: 150));
    }

    [Fact]
    public void A_title_too_long_to_fit_scrolls()
    {
        Assert.True(MediaMarquee.Scrolls(textWidth: 300, width: 150));
    }

    [Fact]
    public void It_scrolls_at_a_steady_reading_pace()
    {
        // One loop is the title and the gap after it.
        Assert.Equal((300 + MediaMarquee.Gap) / MediaMarquee.PixelsPerSecond, MediaMarquee.LoopSeconds(300), 6);
    }
}
