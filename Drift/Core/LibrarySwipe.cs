using System;

namespace Drift;

/// <summary>
/// Swiping the sidebar open into the library and back, as Zen does: the sidebar's width follows the
/// finger between its own width and the library's, and on letting go goes to one or the other. The
/// library slides in attached to the page's edge; the tabs give way to it early in the swipe.
/// No UI here, so it can be tested.
/// </summary>
public sealed class LibrarySwipe
{
    /// <summary>How far past either end the sidebar can be pulled, at most.</summary>
    public const double Stretch = 24;

    public LibrarySwipe(double closed, double open)
    {
        Closed = closed;
        Open = open;
    }

    public double Closed { get; }
    public double Open { get; }

    /// <summary>
    /// Whether a touchpad swipe with the pointer at x is the sidebar's: over the sidebar (or the
    /// library) it is, over the page the page scrolls. One already going keeps going.
    /// </summary>
    public static bool Catches(double x, double sidebarRight, bool swiping) => swiping || x < sidebarRight;

    /// <summary>The sidebar's width for where the finger has taken it.</summary>
    public double Follow(double width)
    {
        var edge = Math.Clamp(width, Closed, Open);
        var past = Math.Abs(width - edge);
        // Ever less give the further it's pulled, approaching Stretch.
        return edge + Math.Sign(width - edge) * Stretch * (1 - 1 / (1 + past / Stretch));
    }

    /// <summary>Faster than this (pixels a second), letting go is a flick: it goes the way it was flicked.</summary>
    public const double FlickSpeed = 300;

    /// <summary>Let go slowly, a swipe that's come this part of the way carries on to the other end.</summary>
    public const double Commit = 0.25;

    /// <summary>
    /// On letting go at a width, moving at a velocity (pixels a second), after a swipe that began at
    /// <paramref name="from"/>: does it open? A flick goes the way it was flicked. Slowly, it goes the
    /// way it was swiped if that's got a quarter of the way, and otherwise back where it was.
    /// (Halfway was too far: a slow swipe on a touchpad often stopped short of it, and sprang back.)
    /// </summary>
    public bool Opens(double from, double width, double velocity)
    {
        if (Math.Abs(velocity) >= FlickSpeed) return velocity > 0;
        var wasOpen = from >= (Closed + Open) / 2;
        var moved = (width - from) / (Open - Closed);
        return wasOpen ? moved > -Commit : moved >= Commit;
    }

    /// <summary>
    /// Springing to an end, it sets off at most this many times its distance from there a second: as
    /// a button sends it, which overshoots a little (about 10px).
    /// </summary>
    public const double CarryPerPixel = 7;

    /// <summary>
    /// How fast it sets off for <paramref name="target"/> when let go moving at a velocity: only
    /// towards there, and no faster than a button sends it. Carrying a fast flick on as it was, it
    /// shot 40-100px past the end and bounced back; flicked away from where it was going, it went on
    /// that way first.
    /// </summary>
    public double Carry(double width, double target, double velocity)
    {
        var towards = target - width;
        if (velocity * towards <= 0) return 0;
        var most = Math.Abs(towards) * CarryPerPixel;
        return Math.Clamp(velocity, -most, most);
    }

    /// <summary>By this part of the way the tabs have faded out (Zen, measured: 61% at 16%, 8% at 34%).</summary>
    public const double TabsGone = 0.35;

    /// <summary>The library fades in over this part of the way: after the tabs, not across them.</summary>
    public const double LibraryFrom = 0.3, LibraryFull = 0.6;

    /// <summary>How visible the tabs and the library are at a width.</summary>
    public (double Tabs, double Library) Fade(double width)
    {
        var way = (width - Closed) / (Open - Closed);
        return (1 - Math.Clamp(way / TabsGone, 0, 1),
                Math.Clamp((way - LibraryFrom) / (LibraryFull - LibraryFrom), 0, 1));
    }
}
