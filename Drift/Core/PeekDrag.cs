using System;
using System.Collections.Generic;

namespace Drift;

/// <summary>
/// Pulling a Peek card down by its grabber to close it, as a sheet is: the card follows the pointer
/// down exactly, gives only a little (with resistance) upwards, and shrinks a touch as it goes, hinting
/// where it's headed. Let go, it closes if where its momentum would carry it is far enough down;
/// otherwise it springs back. No UI here, so it can be tested.
/// </summary>
public static class PeekDrag
{
    /// <summary>How far above its place the card can be pulled, at most, in pixels.</summary>
    public const double Stretch = 24;

    /// <summary>How much smaller the card is once it's been pulled its own height down.</summary>
    public const double Shrink = 0.06;

    /// <summary>Where its momentum carries it has to be this share of its height down to close it.</summary>
    public const double DismissShare = 0.3;

    /// <summary>The card's offset for the pointer having moved dy (down positive).</summary>
    public static double Follow(double dy) =>
        dy >= 0 ? dy : -Stretch * (1 - 1 / (1 + -dy / Stretch));

    /// <summary>The card's scale, pulled dy down.</summary>
    public static double Scale(double dy, double height) =>
        height <= 0 ? 1 : 1 - Shrink * Math.Clamp(dy / height, 0, 1);

    /// <summary>How much further something let go at a velocity (pixels a second) travels before it stops.</summary>
    public static double Project(double velocity) => Momentum.Project(velocity);

    /// <summary>Let go at dy, moving at a velocity: does it close?</summary>
    public static bool Dismisses(double dy, double velocity, double height) =>
        dy > 0 && dy + Project(velocity) > height * DismissShare;
}

/// <summary>
/// A drag's velocity at letting go: over its last 100ms, so a pointer that came to rest before
/// letting go has none, however fast it moved before.
/// </summary>
public sealed class VelocityTracker
{
    private const long WindowMs = 100;
    private readonly List<(long Ms, double Position)> _samples = [];

    public void Add(long ms, double position)
    {
        _samples.Add((ms, position));
        // Only the recent ones are ever needed.
        while (_samples.Count > 2 && _samples[0].Ms < ms - WindowMs * 2) _samples.RemoveAt(0);
    }

    /// <summary>Pixels a second.</summary>
    public double Velocity(long now)
    {
        var recent = _samples.FindAll(s => s.Ms >= now - WindowMs);
        if (recent.Count < 2) return 0;
        var (firstMs, first) = recent[0];
        var (lastMs, last) = recent[^1];
        return lastMs > firstMs ? (last - first) / ((lastMs - firstMs) / 1000.0) : 0;
    }
}
