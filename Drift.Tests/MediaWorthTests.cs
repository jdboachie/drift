using Drift;
using Xunit;

public class MediaWorthTests
{
    [Fact]
    public void A_notification_chime_is_not_media() =>
        Assert.False(MediaWorth.IsMedia(describedBySite: false, playingSeconds: 1.2, playedMs: 1500));

    [Fact]
    public void Sound_with_no_element_is_not_media_at_first() =>
        Assert.False(MediaWorth.IsMedia(describedBySite: false, playingSeconds: null, playedMs: 3000));

    [Fact]
    public void Sound_that_goes_on_long_enough_is_media() =>
        Assert.True(MediaWorth.IsMedia(describedBySite: false, playingSeconds: null, playedMs: MediaWorth.SteadyMs));

    [Fact]
    public void What_the_site_describes_is_media_at_once() =>
        Assert.True(MediaWorth.IsMedia(describedBySite: true, playingSeconds: 2, playedMs: 0));

    [Theory]
    [InlineData(14.9, false)]
    [InlineData(15, true)]
    [InlineData(212, true)]
    [InlineData(double.PositiveInfinity, true)]
    public void A_playing_element_long_enough_or_live_is_media(double seconds, bool media) =>
        Assert.Equal(media, MediaWorth.IsMedia(describedBySite: false, playingSeconds: seconds, playedMs: 0));
}
