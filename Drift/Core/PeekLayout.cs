using System;

namespace Drift;

/// <summary>
/// Where Peek's card sits and how far the page behind it shrinks, as Arc has them: the card about
/// three quarters of the page's width, centred, with its round buttons just off its right edge; the
/// page 8px in on every side.
/// </summary>
public static class PeekLayout
{
    /// <summary>The share of the page's width left either side of the card.</summary>
    public const double SideShare = 0.132;

    public const double ButtonSize = 32;
    public const double ButtonGap = 9;

    /// <summary>Never less than this either side, so the buttons always fit beside the card.</summary>
    public const double MinSide = 44;

    /// <summary>How far the page comes in on each side while Peek is open.</summary>
    public const double PageInset = 8;

    /// <summary>The room either side of the card, in a page this wide.</summary>
    public static double CardSide(double pageWidth) => Math.Max(Math.Round(pageWidth * SideShare), MinSide);

    /// <summary>
    /// The page's scale, from its top-left corner, and the shift that keeps it centred, so each edge
    /// comes in by <see cref="PageInset"/> and lands on a whole device pixel (a 1px border between
    /// pixels comes out soft). A page with no room to shrink stays as it is.
    /// </summary>
    public static (double Scale, double Shift) PageShrink(double size, double pixels)
    {
        // Drawn at its layout-rounded size, which can be half a pixel larger than reported.
        var sizePx = Math.Round(size * pixels, MidpointRounding.AwayFromZero);
        var insetPx = Math.Round(PageInset * pixels);
        if (sizePx <= 4 * insetPx) return (1, 0);
        return ((sizePx - 2 * insetPx) / sizePx, insetPx / pixels);
    }
}
