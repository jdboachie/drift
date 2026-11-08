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

public enum TabAudioIcon { None, Speaker, Muted }

/// <summary>A tab row's sound icon: a speaker while it plays sound, crossed out while it's muted (click to unmute).</summary>
public static class TabAudio
{
    public static TabAudioIcon Icon(bool playing, bool muted) =>
        muted ? TabAudioIcon.Muted : playing ? TabAudioIcon.Speaker : TabAudioIcon.None;
}
