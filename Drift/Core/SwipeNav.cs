using System.Collections.Generic;

namespace Drift;

/// <summary>
/// A two-finger swipe on a touchpad, sideways over a page that can't scroll that way: back (fingers moving right) or
/// forward. The arrow's path is Chromium's (gesture_nav_simple.cc, overscroll_configuration.cc): it's all the way in
/// at 30% of the screen's longer side. It shows from the first few pixels, so the swipe answers at once. Letting go
/// is decided as Drift's Library swipe decides it (LibrarySwipe.Opens): a flick goes the way it was flicked, and a
/// slower swipe goes through only if the arrow's all the way in. Its way is set by its first movement; coming back
/// the other way takes it back to nothing, never to the other way.
/// Or (ForPage) the page itself moves with the fingers, as Safari's does, on WebKit's measures
/// (ViewGestureController.cpp, SwipeProgressTrackerMac.mm): from 15 in, one for one, and let go, it goes through if
/// where it's heading in 0.3s is past 187.5 (or half a narrow page).
/// </summary>
public sealed class SwipeNav
{
    /// <summary>Travel before the arrow shows: enough to tell a swipe from a wobble, and no more.</summary>
    public const double StartAfter = 10;

    /// <summary>The share of the screen's longer side at which the arrow's all the way in.</summary>
    public const double CompleteShare = 0.3;

    /// <summary>How far in the arrow has come by the time it's all the way in.</summary>
    public const double ArrowTravel = 146;

    /// <summary>How much further it comes past that, slowing.</summary>
    public const double ArrowExtra = 72;

    /// <summary>The fingers' speed is measured over this last stretch of the swipe, as the Library's is.</summary>
    private const double SpeedOverMs = 100;

    /// <summary>Travel before the page moves: WebKit's minimumHorizontalSwipeDistance.</summary>
    public const double PageStartAfter = 15;

    /// <summary>How far the page must be heading to go through: WebKit's completionThresholdPoints…</summary>
    public const double PageThrough = 187.5;

    /// <summary>…or this share of a narrower page (maximumCompletionThresholdRatio).</summary>
    public const double PageThroughMostShare = 0.5;

    /// <summary>How far ahead, in seconds, where it's heading is reckoned (kineticProjectionDuration).</summary>
    public const double PageProjectSeconds = 0.3;

    private readonly double _startAfter = StartAfter;
    private readonly double? _pageWidth;
    private readonly double _completeAt;
    private double _moved;
    private readonly List<(double Ms, double Moved)> _trail = new();

    public SwipeNav(double screenLongSide) =>
        _completeAt = System.Math.Max(StartAfter * 2, screenLongSide * CompleteShare);

    private SwipeNav(double pageWidth, bool _)
    {
        _pageWidth = pageWidth;
        _startAfter = PageStartAfter;
        _completeAt = PageStartAfter + System.Math.Min(PageThrough, pageWidth * PageThroughMostShare);
    }

    /// <summary>A swipe that moves the page itself, <paramref name="pageWidth"/> wide.</summary>
    public static SwipeNav ForPage(double pageWidth) => new(pageWidth, true);

    /// <summary>Whether it moves the page (ForPage), rather than bringing in an arrow.</summary>
    public bool MovesPage => _pageWidth is not null;

    /// <summary>How far the page has moved with the fingers: from nothing to its whole width.</summary>
    public double PageOffset => System.Math.Clamp(_moved - _startAfter, 0, _pageWidth ?? 0);

    /// <summary>-1 back, 1 forward; 0 until it first moves.</summary>
    public int Direction { get; private set; }

    /// <summary>
    /// A movement in DIPs, as the page scrolls (less than 0 is the fingers moving right), at a time in milliseconds.
    /// </summary>
    public void Move(double dx, double ms = 0)
    {
        if (Direction == 0)
        {
            if (dx == 0) return;
            Direction = dx < 0 ? -1 : 1;
        }
        // The fingers' own travel, before it's held at nothing: their speed is theirs.
        var raw = (_trail.Count > 0 ? _trail[^1].Moved : _moved) + dx * Direction;
        _moved = System.Math.Max(0, _moved + dx * Direction);
        _trail.Add((ms, raw));
        _trail.RemoveAll(t => ms - t.Ms > SpeedOverMs);
    }

    public bool Showing => _moved > _startAfter;

    /// <summary>0 as the arrow shows, 1 where it's all the way in; more past that.</summary>
    public double Progress => System.Math.Max(0, _moved - _startAfter) / (_completeAt - _startAfter);

    /// <summary>
    /// The fingers' speed over the last tenth of a second, in DIPs a second: more than 0 is the swipe's own way,
    /// less is back the other.
    /// </summary>
    public double Speed
    {
        get
        {
            if (_trail.Count < 2) return 0;
            var (first, last) = (_trail[0], _trail[^1]);
            return last.Ms > first.Ms ? (last.Moved - first.Moved) / ((last.Ms - first.Ms) / 1000) : 0;
        }
    }

    /// <summary>The arrow's speed, in DIPs a second: the fingers', at the arrow's share of their travel.</summary>
    public double ArrowSpeed => Progress < 1 ? Speed * ArrowTravel / (_completeAt - _startAfter) : 0;

    /// <summary>
    /// Let go now, does it go through? The arrow: a flick goes the way it was flicked; slower, if it's all the way in.
    /// The page: if where it's heading is far enough.
    /// </summary>
    public bool GoesThrough =>
        Showing && (MovesPage
            ? PageOffset + Speed * PageProjectSeconds >= _completeAt - _startAfter
            : System.Math.Abs(Speed) >= LibrarySwipe.FlickSpeed ? Speed > 0 : Progress >= 1);

    /// <summary>How far in from the page's edge the arrow has come.</summary>
    public static double ArrowOffset(double progress)
    {
        if (progress <= 1) return ArrowTravel * System.Math.Max(0, progress);
        var extra = System.Math.Min(1, (progress - 1) / (ArrowExtra / ArrowTravel));
        return ArrowTravel + ArrowExtra * FastOutSlowIn(extra);
    }

    /// <summary>Material's standard curve, cubic-bezier(0.4, 0, 0.2, 1), which Chromium's arrow moves on.</summary>
    public static double FastOutSlowIn(double t)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        // Solve x(s) = t for s by bisection, then y(s).
        static double Bezier(double s, double p1, double p2) => 3 * (1 - s) * (1 - s) * s * p1 + 3 * (1 - s) * s * s * p2 + s * s * s;
        double lo = 0, hi = 1, mid = t;
        for (var i = 0; i < 30; i++)
        {
            mid = (lo + hi) / 2;
            if (Bezier(mid, 0.4, 0.2) < t) lo = mid; else hi = mid;
        }
        return Bezier(mid, 0, 1);
    }
}
