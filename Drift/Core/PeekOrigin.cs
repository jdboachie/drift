using System;
using System.Globalization;

namespace Drift;

/// <summary>A link press as the page reports it, in CSS pixels: where, the page's width, and the link's box.</summary>
public readonly record struct LinkPressReport(double X, double Y, double PageWidth, Area? Link);

/// <summary>The card's first frame: its scale on each axis about its top-left corner, and how far it's moved.</summary>
public readonly record struct CardStart(double ScaleX, double ScaleY, double OffsetX, double OffsetY);

/// <summary>
/// Where Peek's card grows from, as Zen's Glance does: the link itself. The card starts covering the
/// link's box exactly and each edge springs out to its place, so it's plain which link opened.
/// </summary>
public static class PeekOrigin
{
    /// <summary>"x,y,pageWidth", or with the link's box: "x,y,pageWidth,left,top,width,height".</summary>
    public static LinkPressReport? Parse(string args)
    {
        var parts = args.Split(',');
        if (parts.Length is not (3 or 7)) return null;
        var n = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out n[i]) || !double.IsFinite(n[i]))
                return null;
        if (n[2] <= 0) return null;
        Area? link = parts.Length == 7 && n[5] > 0 && n[6] > 0 ? new Area(n[3], n[4], n[5], n[6]) : null;
        return new LinkPressReport(n[0], n[1], n[2], link);
    }

    /// <summary>The part of the link that shows; null if none of it does.</summary>
    public static Area? Clip(Area link, Area view)
    {
        var left = Math.Max(link.X, view.X);
        var top = Math.Max(link.Y, view.Y);
        var right = Math.Min(link.Right, view.Right);
        var bottom = Math.Min(link.Bottom, view.Bottom);
        return right > left && bottom > top ? new Area(left, top, right - left, bottom - top) : null;
    }

    /// <summary>
    /// The card, scaled about its top-left corner and moved, so it covers the link (both in the same
    /// coordinates). A link smaller than <paramref name="minSide"/> starts that size, about its middle.
    /// </summary>
    public static CardStart From(Area link, Area card, double minSide = 4)
    {
        var width = Math.Max(link.Width, minSide);
        var height = Math.Max(link.Height, minSide);
        var left = link.X + link.Width / 2 - width / 2;
        var top = link.Y + link.Height / 2 - height / 2;
        return new CardStart(width / card.Width, height / card.Height, left - card.X, top - card.Y);
    }
}
