using System;
using System.Collections.Generic;
using System.Linq;

namespace Drift;

/// <summary>
/// Dragging a split's page by its handle to another place among them, as on iPad: it follows the
/// pointer sideways, giving with resistance past either end; once its middle crosses a divider (where
/// the dividers were when it was picked up), the page beyond moves into the place it left; let go (or
/// flicked) and it lands in the place it's reached, every page keeping its width. With two pages this
/// is swapping them. Distances are the dragged page's sideways offset from where it sat.
/// </summary>
public sealed class PageMove
{
    /// <summary>How far past either end it can be pulled, at most.</summary>
    public const double Stretch = 24;

    private readonly double[] _widths;
    private readonly double[] _lefts;
    private readonly double _gap;

    public PageMove(IReadOnlyList<double> widths, int dragged, double gap)
    {
        _widths = widths.ToArray();
        _lefts = SplitShares.Lefts(_widths, gap);
        _gap = gap;
        Dragged = dragged;
    }

    public int Dragged { get; }

    /// <summary>Where each page starts with the dragged one in place <paramref name="to"/>, the others in their order.</summary>
    private double[] LeftsWith(int to)
    {
        var order = SplitShares.Moved(Enumerable.Range(0, _widths.Length).ToArray(), Dragged, to);
        var lefts = SplitShares.Lefts(order.Select(i => _widths[i]).ToArray(), _gap);
        var result = new double[_widths.Length];
        for (var at = 0; at < order.Length; at++) result[order[at]] = lefts[at];
        return result;
    }

    /// <summary>How far the dragged page goes to land in place <paramref name="to"/>.</summary>
    public double Lands(int to) => LeftsWith(to)[Dragged] - _lefts[Dragged];

    /// <summary>How far page <paramref name="page"/> moves to make room, the dragged one bound for <paramref name="to"/>.</summary>
    public double MakesRoom(int page, int to) => LeftsWith(to)[page] - _lefts[page];

    /// <summary>Where it's drawn for where the pointer's taken it: between the two ends, giving a little past them.</summary>
    public double Follow(double offset)
    {
        var (low, high) = (Lands(0), Lands(_widths.Length - 1));
        var edge = Math.Clamp(offset, low, high);
        var past = Math.Abs(offset - edge);
        return edge + Math.Sign(offset - edge) * Stretch * (1 - 1 / (1 + past / Stretch));
    }

    /// <summary>The place its middle has reached, offset this far: past each divider it crosses, one more page over.</summary>
    public int Place(double offset)
    {
        var middle = _lefts[Dragged] + _widths[Dragged] / 2 + offset;
        var place = Dragged;
        // Rightward, divider d is between pages d and d+1; leftward, between d-1 and d.
        while (place + 1 < _widths.Length && middle > _lefts[place + 1] - _gap / 2) place++;
        while (place > 0 && middle < _lefts[place] - _gap / 2) place--;
        return place;
    }

    /// <summary>Let go at an offset, moving at a velocity: where its momentum takes it.</summary>
    public int LandsIn(double offset, double velocity) => Place(offset + Momentum.Project(velocity));
}
