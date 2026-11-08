using Drift;
using Xunit;

public class MediaBarTests
{
    private const long Minute = 60_000;

    [Fact]
    public void Nothing_has_played_so_there_is_no_controller()
    {
        Assert.Null(MediaBar.Pick([], now: 10 * Minute));
    }

    [Fact]
    public void A_tab_playing_sound_gets_the_controller()
    {
        Assert.Equal("music", MediaBar.Pick([new MediaTab("music", Playing: true, Since: 0)], now: Minute));
    }

    [Fact]
    public void Of_two_playing_the_one_that_started_last_gets_it()
    {
        Assert.Equal("video", MediaBar.Pick(
            [new MediaTab("music", true, Since: 0), new MediaTab("video", true, Since: 2 * Minute)], now: 3 * Minute));
    }

    [Fact]
    public void A_playing_tab_comes_before_one_paused_more_recently()
    {
        Assert.Equal("music", MediaBar.Pick(
            [new MediaTab("music", true, Since: 0), new MediaTab("video", false, Since: 2 * Minute)], now: 3 * Minute));
    }

    [Fact]
    public void Paused_it_stays_a_while_so_it_can_be_played_again()
    {
        Assert.Equal("music", MediaBar.Pick([new MediaTab("music", false, Since: 0)], now: MediaBar.KeepPausedMs - 1));
    }

    [Fact]
    public void Paused_for_long_enough_it_goes()
    {
        Assert.Null(MediaBar.Pick([new MediaTab("music", false, Since: 0)], now: MediaBar.KeepPausedMs));
    }

    [Fact]
    public void Of_two_paused_the_one_paused_last_stays()
    {
        Assert.Equal("video", MediaBar.Pick(
            [new MediaTab("music", false, Since: 0), new MediaTab("video", false, Since: Minute)], now: 2 * Minute));
    }
}

public class TabAudioTests
{
    [Fact]
    public void A_tab_playing_sound_shows_a_speaker()
    {
        Assert.Equal(TabAudioIcon.Speaker, TabAudio.Icon(playing: true, muted: false));
    }

    [Fact]
    public void A_muted_tab_shows_it_is_muted_whether_or_not_it_is_playing()
    {
        Assert.Equal(TabAudioIcon.Muted, TabAudio.Icon(playing: true, muted: true));
        Assert.Equal(TabAudioIcon.Muted, TabAudio.Icon(playing: false, muted: true));
    }

    [Fact]
    public void A_quiet_tab_shows_nothing()
    {
        Assert.Equal(TabAudioIcon.None, TabAudio.Icon(playing: false, muted: false));
    }
}
