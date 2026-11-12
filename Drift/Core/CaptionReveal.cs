namespace Drift;

/// <summary>Where the pointer is, as far as the window buttons' bar is concerned.</summary>
public enum PointerAt
{
    Away,
    /// <summary>The top edge of the page area: the window's top margin and a sliver of the page.</summary>
    Top,
    /// <summary>The top edge, below where the buttons come down: heading for them.</summary>
    TopNearButtons,
    /// <summary>On the bar while it's down, or just beside it.</summary>
    Bar,
}

public enum CaptionChange { None, Show, Hide }

/// <summary>
/// The window's geometry for the bar, in view pixels: the window's width, where the page starts,
/// and the bar's size (it sits in the window's top-right corner).
/// </summary>
public sealed record CaptionLayout(double Width, double PageLeft, double PageTop, double BarWidth, double BarHeight)
{
    // How far into the page the top edge reaches: the margin above it is only 8px.
    private const double PageSliver = 4;
    // Reaching for the buttons from a little to their left still counts.
    private const double ApproachSlack = 24;
    // Slipping just off the bar doesn't count as leaving it.
    private const double BarSlack = 12;

    public PointerAt Locate((double X, double Y)? pointer, bool shown)
    {
        if (pointer is not var (x, y)) return PointerAt.Away;
        if (shown && x >= Width - BarWidth - BarSlack && y < BarHeight + BarSlack) return PointerAt.Bar;
        if (x < PageLeft || y >= PageTop + PageSliver) return PointerAt.Away;
        return x >= Width - BarWidth - ApproachSlack ? PointerAt.TopNearButtons : PointerAt.Top;
    }
}

/// <summary>
/// When the window buttons come down from the top-right corner, and when they go: they come for a
/// pointer that rests at the top of the page area (sooner if it's under where they'll be), not one
/// passing through or dragging, and they stay while the pointer is on them or along the top, leaving
/// a moment after it goes so slipping off and straight back doesn't flicker them.
/// </summary>
public sealed class CaptionReveal
{
    /// <summary>How long the pointer rests at the top before the buttons come.</summary>
    public const long RestMs = 140;
    /// <summary>The same, under where the buttons will be: that's plainly where it's going.</summary>
    public const long CornerRestMs = 40;
    /// <summary>How long the pointer is gone before they go.</summary>
    public const long LeaveMs = 350;

    private long? _atTopSince;
    private long? _awaySince;

    public bool Shown { get; private set; }

    /// <param name="buttonDown">A mouse button is held: a window or selection being dragged.</param>
    /// <param name="blocked">Something else has the window (the command bar, full screen).</param>
    public CaptionChange Update(long now, PointerAt at, bool buttonDown = false, bool blocked = false)
    {
        if (blocked)
        {
            var was = Shown;
            Reset();
            return was ? CaptionChange.Hide : CaptionChange.None;
        }

        if (!Shown)
        {
            if (at == PointerAt.Away || buttonDown)
            {
                _atTopSince = null;
                return CaptionChange.None;
            }
            _atTopSince ??= now;
            var rest = at == PointerAt.Top ? RestMs : CornerRestMs;
            if (now - _atTopSince < rest) return CaptionChange.None;
            _atTopSince = null;
            _awaySince = null;
            Shown = true;
            return CaptionChange.Show;
        }

        if (at != PointerAt.Away)
        {
            _awaySince = null;
            return CaptionChange.None;
        }
        _awaySince ??= now;
        if (now - _awaySince < LeaveMs) return CaptionChange.None;
        Reset();
        return CaptionChange.Hide;
    }

    /// <summary>Gone, and starting over: put away by something else.</summary>
    public void Reset()
    {
        Shown = false;
        _atTopSince = null;
        _awaySince = null;
    }
}
