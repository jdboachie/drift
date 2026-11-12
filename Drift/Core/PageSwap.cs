using System;

namespace Drift;

/// <summary>
/// Dragging a split's page by its handle to the other side, as on iPad: it follows the pointer
/// sideways, giving with resistance past where it would land; once its middle crosses the divider the
/// other page moves into the place it left; let go across (or flicked across) they swap, each keeping
/// its width. Distances are the dragged page's sideways offset from where it sits.
/// </summary>
public sealed class PageSwap(bool draggedIsLeft, double leftWidth, double rightWidth, double gap)
{
    /// <summary>How far past either end it can be pulled, at most.</summary>
    public const double Stretch = 24;

    /// <summary>Toward the other side is right for the left page, left for the right one.</summary>
    private double Toward => draggedIsLeft ? 1 : -1;

    /// <summary>Where the dragged page lands, swapped: beyond the other page.</summary>
    public double Lands => Toward * ((draggedIsLeft ? rightWidth : leftWidth) + gap);

    /// <summary>Where the other page goes to make room: where the dragged one was.</summary>
    public double OtherMakesRoom => -Toward * ((draggedIsLeft ? leftWidth : rightWidth) + gap);

    /// <summary>How far toward the other side its middle has to go to reach the divider.</summary>
    private double ToDivider => ((draggedIsLeft ? leftWidth : rightWidth) + gap) / 2;

    /// <summary>Where it's drawn for where the pointer's taken it.</summary>
    public double Follow(double offset)
    {
        var along = offset * Toward;
        var end = Math.Abs(Lands);
        var edge = Math.Clamp(along, 0, end);
        var past = Math.Abs(along - edge);
        var given = edge + Math.Sign(along - edge) * Stretch * (1 - 1 / (1 + past / Stretch));
        return given * Toward;
    }

    /// <summary>Its middle is past the divider: the other page makes room.</summary>
    public bool Crossed(double offset) => offset * Toward > ToDivider;

    /// <summary>Let go at an offset, moving at a velocity: where its momentum takes it decides.</summary>
    public bool Swaps(double offset, double velocity) => Crossed(offset + Momentum.Project(velocity));
}
