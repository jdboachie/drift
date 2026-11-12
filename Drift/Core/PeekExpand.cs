using System;

namespace Drift;

/// <summary>A box's four edge distances (a Thickness, without the UI).</summary>
public readonly record struct Edges(double Left, double Top, double Right, double Bottom);

/// <summary>
/// Opening a Peek as a tab. A web page can't follow a size that changes every frame (it lags, then
/// snaps), so it never animates: it moves into its tab and lays itself out at full width out of sight,
/// while a picture of it stands in, held still, and the card's edges spring out to the page's. Then the
/// picture fades into the page.
/// </summary>
public static class PeekExpand
{
    // The card's edges spring out like Peek opening (MainWindow's PeekGrow*): a quick start, a small
    // overshoot past the page's edges, then they settle on them.
    public const double Seconds = 0.32;
    public const double Bounce = 0.25;
    public const double Kick = 0.6;
    public const double FadeSeconds = 0.14;

    // Opening as a split, the card and the page it came from move on Peek's own spring, so it's plainly
    // the same feature: it leaves at once and settles quickly. With less bounce, since these are the
    // largest things that move (a big surface with the same bounce looks wobbly), and a gentler push off:
    // Peek's whole push took the card most of the way in two frames, and it read as a jump.
    public const double SplitSeconds = Seconds;
    public const double SplitBounce = 0.1;
    public const double SplitKick = 0.15;

    /// <summary>The edges' spring, at the start, heading <paramref name="travel"/> out (0 to travel).</summary>
    public static SpringMotion Grow(double travel, double seconds = Seconds, double bounce = Bounce, double kick = Kick) => new(seconds, bounce)
    {
        Position = 0,
        Target = travel,
        Velocity = travel * kick * 2 * Math.PI / seconds,
    };

    /// <summary>How long until an edge travelling this many pixels is at rest (SpringMotion.AtRest).</summary>
    public static double SettleSeconds(double travel, double seconds = Seconds, double bounce = Bounce, double kick = Kick)
    {
        var spring = Grow(travel, seconds, bounce, kick);
        var time = 0.0;
        for (; time < 2 && !spring.AtRest; time += 0.005) spring.Step(0.005);
        return time;
    }

    /// <summary>
    /// How long until an edge travelling <paramref name="travel"/> pixels on the split's spring is all but
    /// there: within <paramref name="within"/> pixels of its end, or past it on the overshoot.
    /// </summary>
    public static double ArriveSeconds(double travel, double within)
    {
        var spring = Grow(travel, SplitSeconds, SplitBounce, SplitKick);
        var time = 0.0;
        for (; time < 2 && travel - spring.Position > within; time += 0.001) spring.Step(0.001);
        return time;
    }

    /// <summary>
    /// Past its end (the clip can't go further than the card), the card scales instead: by
    /// 1 + PerX × overshoot across, about CenterX, so each edge goes out by its own inset × overshoot.
    /// </summary>
    public readonly record struct Swell(double PerX, double PerY, double CenterX, double CenterY);

    public static Swell Bulge(Edges inset, double width, double height)
    {
        double across = inset.Left + inset.Right, down = inset.Top + inset.Bottom;
        return new Swell(
            width > 0 ? across / width : 0,
            height > 0 ? down / height : 0,
            across > 0 ? inset.Left * width / across : width / 2,
            down > 0 ? inset.Top * height / down : height / 2);
    }

    /// <summary>Where the card's page sits inside the card once the card is page-sized (both as margins in the same cell).</summary>
    public static Edges Inset(Edges card, Edges page) => new(
        Math.Max(0, card.Left - page.Left),
        Math.Max(0, card.Top - page.Top),
        Math.Max(0, card.Right - page.Right),
        Math.Max(0, card.Bottom - page.Bottom));

    /// <summary>The smallest box (as margins in the same cell) holding both: the card while it moves from one to the other.</summary>
    public static Edges Union(Edges a, Edges b) => new(
        Math.Min(a.Left, b.Left), Math.Min(a.Top, b.Top), Math.Min(a.Right, b.Right), Math.Min(a.Bottom, b.Bottom));

    /// <summary>The page's width as the page sees it (innerWidth), laid out in a view this wide at this zoom.</summary>
    public static int CssWidth(double viewWidth, double zoom) => (int)Math.Floor(viewWidth / (zoom > 0 ? zoom : 1));
}
