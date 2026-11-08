namespace Drift.Tests;

public class AutoPipTests
{
    private static PageVideo Video(bool playing = true, bool muted = false, double width = 640, double height = 360,
        bool allowed = true, double lastPlayed = 0) => new(playing, muted, width, height, allowed, lastPlayed);

    [Fact]
    public void Leaving_a_tab_with_a_video_playing_floats_it()
    {
        var leave = AutoPip.OnLeave(enabled: true, pageFloatsItself: false, [Video()]);

        Assert.Equal(PipAction.FloatVideo(0), leave);
    }

    [Fact]
    public void Nothing_floats_when_nothing_is_playing_or_it_is_turned_off()
    {
        Assert.Equal(PipAction.None, AutoPip.OnLeave(enabled: true, pageFloatsItself: false, [Video(playing: false)]));
        Assert.Equal(PipAction.None, AutoPip.OnLeave(enabled: true, pageFloatsItself: false, []));
        Assert.Equal(PipAction.None, AutoPip.OnLeave(enabled: false, pageFloatsItself: false, [Video()]));
    }

    [Fact]
    public void Silent_autoplay_and_tiny_videos_stay_put()
    {
        // A feed's muted autoplay, a thumbnail's hover preview, an ad.
        Assert.Equal(PipAction.None, AutoPip.OnLeave(true, false, [Video(muted: true)]));
        Assert.Equal(PipAction.None, AutoPip.OnLeave(true, false, [Video(width: 160, height: 90)]));
    }

    [Fact]
    public void A_video_that_forbids_picture_in_picture_stays_put()
    {
        Assert.Equal(PipAction.None, AutoPip.OnLeave(true, false, [Video(allowed: false)]));
    }

    [Fact]
    public void Of_several_playing_the_biggest_floats_and_of_equals_the_one_started_last()
    {
        Assert.Equal(PipAction.FloatVideo(1), AutoPip.OnLeave(true, false, [Video(width: 320, height: 180), Video(width: 1280, height: 720)]));
        Assert.Equal(PipAction.FloatVideo(0), AutoPip.OnLeave(true, false, [Video(lastPlayed: 20), Video(lastPlayed: 10)]));
    }

    [Fact]
    public void A_page_that_floats_itself_like_a_meeting_does_so_instead()
    {
        // Google Meet asks to (the media session's "enterpictureinpicture"): its whole call floats.
        Assert.Equal(PipAction.PageFloats, AutoPip.OnLeave(true, pageFloatsItself: true, [Video(muted: true)]));
        Assert.Equal(PipAction.None, AutoPip.OnLeave(false, pageFloatsItself: true, []));
    }

    [Fact]
    public void A_window_closed_with_its_video_playing_on_was_back_to_tab()
    {
        // The window's Close pauses the video (measured in WebView2); Back to tab leaves it playing.
        Assert.True(AutoPip.WentBackToTab(stillPlaying: true));
        Assert.False(AutoPip.WentBackToTab(stillPlaying: false));
    }
}
