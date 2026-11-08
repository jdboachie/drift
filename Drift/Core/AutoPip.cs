using System.Collections.Generic;

namespace Drift;

/// <summary>A video on a page, as the page reports it.</summary>
public sealed record PageVideo(bool Playing, bool Muted, double Width, double Height, bool PipAllowed, double LastPlayed);

public enum PipKind { None, Video, Page }

/// <summary>What to do with a tab's media when you leave it: float one of its videos (by index), or the page itself.</summary>
public sealed record PipAction(PipKind Kind, int Video = -1)
{
    public static readonly PipAction None = new(PipKind.None);

    /// <summary>The page floats itself (a meeting's whole call, in its own picture-in-picture window).</summary>
    public static readonly PipAction PageFloats = new(PipKind.Page);
    public static PipAction FloatVideo(int index) => new(PipKind.Video, index);
}

/// <summary>
/// Arc's automatic picture-in-picture: leave a tab with a video playing and it floats in its own
/// window, over everything; come back and it's back in the page. No UI here, so it can be tested.
/// </summary>
public static class AutoPip
{
    public static PipAction OnLeave(bool enabled, bool pageFloatsItself, IReadOnlyList<PageVideo> videos)
    {
        if (!enabled) return PipAction.None;
        if (pageFloatsItself) return PipAction.PageFloats;
        var best = -1;
        for (var i = 0; i < videos.Count; i++)
        {
            if (!Floats(videos[i])) continue;
            if (best < 0 || Watched(videos[i]).CompareTo(Watched(videos[best])) > 0) best = i;
        }
        return best < 0 ? PipAction.None : PipAction.FloatVideo(best);
    }

    /// <summary>The one being watched: the biggest, and of the same size, the one started last.</summary>
    private static (double Area, double LastPlayed) Watched(PageVideo v) => (v.Width * v.Height, v.LastPlayed);

    /// <summary>Smaller than this (CSS pixels), a video's a thumbnail, a preview or an ad.</summary>
    public const double MinWidth = 200, MinHeight = 112;

    /// <summary>
    /// Playing with its sound on, as in Chrome's own automatic picture-in-picture: a muted video is
    /// a feed's autoplay or a hover preview, not something being watched.
    /// </summary>
    private static bool Floats(PageVideo v) =>
        v.Playing && !v.Muted && v.PipAllowed && v.Width >= MinWidth && v.Height >= MinHeight;

    /// <summary>
    /// The floating window's gone. Its Close pauses the video (measured in WebView2), and Back to tab
    /// leaves it playing: so still playing, it was Back to tab, and Drift should show that tab.
    /// </summary>
    public static bool WentBackToTab(bool stillPlaying) => stillPlaying;
}
