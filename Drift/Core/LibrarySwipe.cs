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

    /// <summary>
    /// The library starts coming this far along, the tabs all but gone (under 15%): one after the other, never across
    /// each other (the user, 2026-11-24: "the sidebar should be almost faded out completely before the library comes
    /// sliding in... its overlapping and its ugly").
    /// </summary>
    public const double LibraryFrom = 0.3;

    /// <summary>
    /// The library is at full strength this soon after it starts: it isn't faded in, it slides in (LibraryShift), as
    /// Zen's does. Shown only from its first few pixels, so nothing of it is left at the window's edge while it's shut.
    /// </summary>
    public const double LibraryShown = 0.05;

    /// <summary>How visible the tabs and the library are at a width.</summary>
    public (double Tabs, double Library) Fade(double width)
    {
        var way = Way(width);
        return (1 - Math.Clamp(way / TabsGone, 0, 1), Math.Clamp((way - LibraryFrom) / LibraryShown, 0, 1));
    }

    /// <summary>
    /// How far the library is drawn moved over at a width: off the window's left edge till it starts coming
    /// (LibraryFrom), all but KeptOnWindow, in place open, sliding the whole way in between (as Zen's:
    /// translateX(-100% * (1 - progress))).
    /// Seen fading in once it was nearly in place, it seemed just to appear (the user, 2026-11-24: "the library should
    /// slide in. not just appear"). Past open (the spring's overshoot) it goes only as far past its place as the page:
    /// coming in faster than the page, it went three times as far, an empty strip opening at the window's edge.
    /// </summary>
    public double LibraryShift(double width) =>
        Math.Max(KeptOnWindow - Open, -(1 - Math.Clamp((Way(width) - LibraryFrom) / (1 - LibraryFrom), 0, 1)) * Open)
        + Math.Max(0, width - Open);

    /// <summary>
    /// How much of the library stays on the window while it's shut, unseen (it's wholly transparent till it comes). Its
    /// History list builds only the rows it takes to be on screen, judging by where the library's drawn as the window
    /// lays out; finding it wholly off the window as an open began, it built none, and the library slid in empty, the
    /// rows showing only once the fingers lifted (the user, 2026-11-25: "its just empty and then shows"). Past the
    /// panel's and the list's padding (12px), and no wider than the library's come once it's fully shown (about 30px),
    /// so it never seems to jump in.
    /// </summary>
    public const double KeptOnWindow = 24;

    /// <summary>
    /// The spring that opens or shuts it: how long it takes, how much it bounces, and how fast it sets off (a button's
    /// or a key's kick, times the distance, a second; fingers give their own speed). Measured 2026-11-25: the old one
    /// (0.3s, 0.3 bounce, kick 7) had the library cross its whole width in about 90ms, too fast to be seen sliding,
    /// then wobble 10px for 240ms more. Now, as a drawer: opening, the tabs go in about 60ms and the library slides
    /// in over about 250ms, easing out; shutting is quicker, as what's leaving needn't be watched. Only fingers that
    /// let go moving throw it, so only then does it bounce, a little.
    /// </summary>
    public static (double Seconds, double Bounce, double Kick) Motion(bool open, bool thrown) =>
        thrown ? (0.3, 0.15, 0)
        : open ? (0.4, 0, 4)
        : (0.3, 0, 5);

    /// <summary>How far along from shut (0) to open (1) a width is.</summary>
    public double Way(double width) => (width - Closed) / (Open - Closed);
}
