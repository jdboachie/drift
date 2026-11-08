using System;

namespace Drift;

/// <summary>A rectangle on screen, in pixels.</summary>
public readonly record struct Box(int X, int Y, int Width, int Height);

/// <summary>
/// Where Drift's floating player goes, and how big: shaped like its video, in the bottom-right
/// corner the first time, then wherever it was last left (kept on screen). No UI here, so it can be tested.
/// </summary>
public static class PlayerPlacement
{
    public const int MaxWidth = 400, MaxHeight = 360, Margin = 16;

    /// <summary>The video's shape, as large as fits in MaxWidth × MaxHeight (16:9 when it isn't known).</summary>
    public static (int Width, int Height) Size(double videoWidth, double videoHeight)
    {
        var ratio = videoWidth > 0 && videoHeight > 0 ? videoWidth / videoHeight : 16.0 / 9;
        return ratio >= (double)MaxWidth / MaxHeight
            ? (MaxWidth, (int)Math.Round(MaxWidth / ratio, MidpointRounding.AwayFromZero))
            : ((int)Math.Round(MaxHeight * ratio, MidpointRounding.AwayFromZero), MaxHeight);
    }

    /// <param name="last">Where the last player was left, if one has been.</param>
    /// <param name="screen">The work area it opens on.</param>
    public static Box Place(Box? last, int width, int height, Box screen)
    {
        if (last is not { } was)
            return new(screen.X + screen.Width - Margin - width, screen.Y + screen.Height - Margin - height, width, height);
        var w = Math.Min(was.Width, screen.Width);
        var h = Math.Min(was.Height, screen.Height);
        return new(Math.Clamp(was.X, screen.X, screen.X + screen.Width - w), Math.Clamp(was.Y, screen.Y, screen.Y + screen.Height - h), w, h);
    }
}
