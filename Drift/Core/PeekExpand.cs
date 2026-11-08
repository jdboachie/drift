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

    /// <summary>The edges' spring, at the start, heading <paramref name="travel"/> out (0 to travel).</summary>
    public static SpringMotion Grow(double travel) => new(Seconds, Bounce)
    {
        Position = 0,
        Target = travel,
        Velocity = travel * Kick * 2 * Math.PI / Seconds,
    };

    /// <summary>How long until an edge travelling this many pixels is at rest (SpringMotion.AtRest).</summary>
    public static double SettleSeconds(double travel)
    {
        var spring = Grow(travel);
        var seconds = 0.0;
        for (; seconds < 1 && !spring.AtRest; seconds += 0.005) spring.Step(0.005);
        return seconds;
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

    /// <summary>The page's width as the page sees it (innerWidth), laid out in a view this wide at this zoom.</summary>
    public static int CssWidth(double viewWidth, double zoom) => (int)Math.Floor(viewWidth / (zoom > 0 ? zoom : 1));
}
