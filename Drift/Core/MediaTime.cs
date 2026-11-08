using System;

namespace Drift;

/// <summary>The expanded mini player's times and seek bar (MediaControls.cs). No UI here, so it can be tested.</summary>
public static class MediaTime
{
    /// <summary>"0:19", "1:05", "2:56:20"; a time not known (NaN, negative, endless) reads "0:00".</summary>
    public static string Format(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) seconds = 0;
        var whole = (long)Math.Floor(seconds);
        var (h, m, s) = (whole / 3600, whole / 60 % 60, whole % 60);
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
    }

    /// <summary>How full the bar is; empty when the length isn't known (a live stream).</summary>
    public static double Fraction(double time, double duration) =>
        duration > 0 && !double.IsInfinity(duration) ? Math.Clamp(time / duration, 0, 1) : 0;

    /// <summary>The time at a point pressed on the bar.</summary>
    public static double At(double x, double width, double duration) =>
        width > 0 ? Math.Clamp(x / width, 0, 1) * duration : 0;
}

/// <summary>A title too long for the mini player scrolls by, round and round, at a steady pace.</summary>
public static class MediaMarquee
{
    /// <summary>Between the end of the title and its start coming round again.</summary>
    public const double Gap = 40;
    public const double PixelsPerSecond = 30;

    public static bool Scrolls(double textWidth, double width) => textWidth > width;

    public static double LoopSeconds(double textWidth) => (textWidth + Gap) / PixelsPerSecond;
}
