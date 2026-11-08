using System;
using System.Collections.Generic;

namespace Drift;

/// <summary>One row's move: it starts From pixels away from its new place (and springs there), fading in if Fade.</summary>
public readonly record struct Slide(object Key, double From, bool Fade);

/// <summary>
/// Which rows of a list slide, and from where, when the list changes (FLIP: where each row was,
/// where it is now, and the difference to animate away). No UI here, so it can be tested.
/// </summary>
public static class SlidePlan
{
    /// <param name="before">Each row's key and top, before the change.</param>
    /// <param name="after">The same, after it, in list order.</param>
    /// <param name="except">A row that goes straight to its place (a dropped tab: it's where the pointer let go).</param>
    /// <param name="unfold">New rows come out from under the row above them (a folder opening), fading in;
    /// otherwise they appear in place.</param>
    public static IReadOnlyList<Slide> For(IReadOnlyList<(object Key, double Y)> before,
        IReadOnlyList<(object Key, double Y)> after, object? except = null, bool unfold = false)
    {
        var was = new Dictionary<object, double>();
        foreach (var (key, y) in before) was[key] = y;

        var slides = new List<Slide>();
        double? above = null;   // Where the nearest row above that was already there started.
        foreach (var (key, y) in after)
        {
            if (was.TryGetValue(key, out var from))
            {
                above = from;
                if (Math.Abs(from - y) >= 0.5 && !Equals(key, except)) slides.Add(new Slide(key, from - y, Fade: false));
            }
            else if (unfold && above is { } start && !Equals(key, except))
            {
                slides.Add(new Slide(key, start - y, Fade: true));
            }
        }
        return slides;
    }
}
