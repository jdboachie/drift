using System.Collections.Generic;

namespace Drift;

/// <summary>A tab that has played sound: whether it's playing now, and since when (ms) it has or hasn't been.</summary>
public sealed record MediaTab(object Id, bool Playing, long Since);

/// <summary>
/// Arc's mini player: the media controller at the bottom of the sidebar (MediaControls.cs). Which
/// tab it's for: one playing sound, the one that started last; else, for a while, the one that
/// stopped last, so it can be played again from there. No UI here, so it can be tested.
/// </summary>
public static class MediaBar
{
    /// <summary>How long a paused tab keeps the controller.</summary>
    public const long KeepPausedMs = 5 * 60_000;

    public static object? Pick(IReadOnlyList<MediaTab> tabs, long now)
    {
        MediaTab? playing = null, paused = null;
        foreach (var tab in tabs)
        {
            if (tab.Playing)
            {
                if (playing is null || tab.Since > playing.Since) playing = tab;
            }
            else if (now - tab.Since < KeepPausedMs && (paused is null || tab.Since > paused.Since)) paused = tab;
        }
        return (playing ?? paused)?.Id;
    }
}

/// <summary>
/// Whether a tab's sound is media, worth the mini player, rather than a notification's chime: the site
/// says what's playing (its media session), or the playing audio or video is long enough or live (a
/// stream, a call), or the sound has simply gone on long enough.
/// </summary>
public static class MediaWorth
{
    /// <summary>Shorter than this, a playing element is a sound effect.</summary>
    public const double MinSeconds = 15;

    /// <summary>Sound that's gone on this long without a break is media, whatever's making it.</summary>
    public const long SteadyMs = 10_000;

    /// <param name="playingSeconds">How long the playing audio or video is (infinite when live); null if there's none.</param>
    /// <param name="playedMs">How long the tab's been playing sound without a break.</param>
    public static bool IsMedia(bool describedBySite, double? playingSeconds, long playedMs) =>
        describedBySite || playingSeconds >= MinSeconds || playedMs >= SteadyMs;
}

public enum TabAudioIcon { None, Speaker, Muted }

/// <summary>A tab row's sound icon: a speaker while it plays sound, crossed out while it's muted (click to unmute).</summary>
public static class TabAudio
{
    public static TabAudioIcon Icon(bool playing, bool muted) =>
        muted ? TabAudioIcon.Muted : playing ? TabAudioIcon.Speaker : TabAudioIcon.None;
}
