using System;
using System.Collections.Generic;

namespace Drift;

/// <summary>A rectangle in view pixels, without the UI's types, so geometry here can be tested.</summary>
public readonly record struct Area(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

public static class Areas
{
    /// <summary>
    /// What's left of <paramref name="box"/> with <paramref name="hole"/> cut out: up to four boxes,
    /// the full-width bands above and below the hole, then the pieces either side of it.
    /// </summary>
    public static List<Area> Carve(Area box, Area hole)
    {
        var left = Math.Max(box.X, hole.X);
        var top = Math.Max(box.Y, hole.Y);
        var right = Math.Min(box.Right, hole.Right);
        var bottom = Math.Min(box.Bottom, hole.Bottom);
        if (right <= left || bottom <= top) return [box];

        var parts = new List<Area>();
        if (top > box.Y) parts.Add(new Area(box.X, box.Y, box.Width, top - box.Y));
        if (bottom < box.Bottom) parts.Add(new Area(box.X, bottom, box.Width, box.Bottom - bottom));
        if (left > box.X) parts.Add(new Area(box.X, top, left - box.X, bottom - top));
        if (right < box.Right) parts.Add(new Area(right, top, box.Right - right, bottom - top));
        return parts;
    }
}
