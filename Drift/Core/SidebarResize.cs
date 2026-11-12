using System;

namespace Drift;

/// <summary>
/// Dragging the sidebar's edge to resize it. Between Min and Max the edge stays under the pointer;
/// past either it gives a little, with resistance, and springs back on letting go. Past Min it also
/// says how near it is to hiding the sidebar: dragged far enough shut (or flicked shut from past
/// Min), it hides: there and then once past HideAt, or on letting go if it was flicked. No UI here, so it can be tested.
/// </summary>
public static class SidebarResize
{
    public const double Default = 208;
    public const double Min = 180;
    public const double Max = 420;

    /// <summary>How far past either limit the edge can be pulled, at most, in pixels.</summary>
    public const double Stretch = 24;

    /// <summary>Where the pointer has to take the edge (a width) for letting go to hide the sidebar.</summary>
    public const double HideAt = 100;

    /// <summary>The sidebar's width for where the pointer has taken its edge.</summary>
    public static double Follow(double raw)
    {
        var edge = Math.Clamp(raw, Min, Max);
        var past = Math.Abs(raw - edge);
        // Ever less give the further it's pulled, approaching Stretch.
        return edge + Math.Sign(raw - edge) * Stretch * (1 - 1 / (1 + past / Stretch));
    }

    /// <summary>
    /// Dragged this far shut, it hides there and then, without waiting to be let go: past the point
    /// of no return, it just goes.
    /// </summary>
    public static bool HidesNow(double raw) => raw <= HideAt;

    /// <summary>0 at Min (or wider) to 1 at HideAt: how near it is to hiding.</summary>
    public static double HideProgress(double raw) => Math.Clamp((Min - raw) / (Min - HideAt), 0, 1);

    /// <summary>
    /// Let go with the pointer having taken the edge to raw, moving at a velocity (pixels a second):
    /// hide the sidebar, or the width it settles at. Only a sidebar narrowed past Min can be thrown
    /// shut, so a quick drag narrower within the limits never hides it by surprise.
    /// </summary>
    public static (bool Hide, double Width) LetGo(double raw, double velocity)
    {
        if (raw <= HideAt || raw < Min && raw + Momentum.Project(velocity) <= HideAt) return (true, Min);
        return (false, Math.Clamp(raw, Min, Max));
    }
}
