using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace Drift;

/// <summary>
/// The Essentials' tiles (EssentialList's panel), laid out by EssentialsGrid: every row fills the
/// sidebar's width, a short last row with wider tiles. A grid of equal cells (ItemsWrapGrid) can't do
/// that, and a tile moving between a full row and a short one changes width as well as place, which the
/// compositor's springs on Translation can't show; so the tiles' own places and widths spring here
/// (LayoutSpring), arranged each frame while they move.
/// While a tab is dragged over Essentials, the tiles take the order a drop would give (ShowOrder), with
/// a slot for a tab arriving from elsewhere, whose placeholder (on the window's drop layer) follows it.
/// </summary>
public sealed partial class EssentialsPanel : Panel
{
    private static readonly UISettings Settings = new();

    private sealed class Motion
    {
        public double X, Y, Width, SpeedX, SpeedY, SpeedWidth;
        public EssentialsGrid.Cell Target;
        public bool Moving;

        public void Snap(EssentialsGrid.Cell to)
        {
            (X, Y, Width) = (to.X, to.Y, to.Width);
            SpeedX = SpeedY = SpeedWidth = 0;
            Target = to;
            Moving = false;
        }
    }

    private readonly Dictionary<UIElement, Motion> _motions = [];
    private int _columns = 2;
    private int[]? _slots;          // while previewing: each item's slot, by its index
    private int _slotCount;         // while previewing: how many slots
    private int? _extraSlot;        // while previewing: the arriving tab's slot
    private Motion? _extra;
    private (double Duration, double Bounce)? _animateNext;
    private bool _snapNext;
    private (double Duration, double Bounce) _spring = (0.3, 0);
    private TimeSpan? _lastFrame;
    private bool _ticking;

    public EssentialsPanel() => Unloaded += (_, _) => StopTicking();

    /// <summary>How many across a full row holds (EssentialsGrid.Columns).</summary>
    public int Columns
    {
        get => _columns;
        set
        {
            if (_columns == value) return;
            _columns = Math.Max(1, value);
            InvalidateMeasure();
        }
    }

    /// <summary>A full row's cell (a tile and the 5 around it).</summary>
    public double CellWidth => ActualWidth / Columns;

    /// <summary>Where the arriving tab's placeholder goes, in this panel's space, as it moves.</summary>
    public Action<Rect>? PlaceExtra { get; set; }

    /// <summary>The next layout's changes spring into place rather than land.</summary>
    public void Animate(double duration, double bounce)
    {
        _animateNext = (duration, bounce);
        InvalidateArrange();
    }

    /// <summary>
    /// The tiles in another order: <paramref name="slots"/>[i] is where item i goes, of <paramref name="count"/>
    /// slots, with <paramref name="extraSlot"/> held for a tab arriving from elsewhere.
    /// </summary>
    public void ShowOrder(int[] slots, int count, int? extraSlot, double duration, double bounce)
    {
        _slots = slots;
        _slotCount = count;
        if (extraSlot is null) _extra = null;
        _extraSlot = extraSlot;
        Animate(duration, bounce);
        InvalidateMeasure();
    }

    /// <summary>Back to the items' own order: springing there, or straight there.</summary>
    public void ClearOrder(bool animate)
    {
        _slots = null;
        _extraSlot = null;
        _extra = null;
        if (animate) Animate(0.3, 0.15);
        else _snapNext = true;
        InvalidateMeasure();
    }

    private int SlotCount => _slots is null ? Children.Count : _slotCount;

    private int SlotOf(UIElement child, int position)
    {
        var index = ItemsControl.GetItemsOwner(this)?.IndexFromContainer(child) ?? -1;
        if (index < 0) index = position;
        return _slots is { } slots && index < slots.Length ? slots[index] : index;
    }

    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? Columns * EssentialsGrid.MinCell : available.Width;
        var count = SlotCount;
        for (var i = 0; i < Children.Count; i++)
        {
            var cell = EssentialsGrid.CellOf(SlotOf(Children[i], i), count, Columns, width);
            Children[i].Measure(new Size(cell.Width, EssentialsGrid.RowHeight));
        }
        return new Size(width, EssentialsGrid.Rows(count, Columns) * EssentialsGrid.RowHeight);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var count = SlotCount;
        var animate = Settings.AnimationsEnabled ? _animateNext : null;
        var snap = _snapNext || !Settings.AnimationsEnabled;
        _animateNext = null;
        _snapNext = false;
        if (animate is { } spring) _spring = spring;
        var here = new HashSet<UIElement>();
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            here.Add(child);
            var target = EssentialsGrid.CellOf(SlotOf(child, i), count, Columns, final.Width);
            if (!_motions.TryGetValue(child, out var motion))
            {
                // A tile that's new here lands.
                motion = new Motion();
                motion.Snap(target);
                _motions[child] = motion;
            }
            Retarget(motion, target, animate is not null, snap);
            child.Arrange(new Rect(motion.X, motion.Y, Math.Max(0, motion.Width), EssentialsGrid.RowHeight));
        }
        if (_motions.Count != here.Count)
            foreach (var gone in new List<UIElement>(_motions.Keys))
                if (!here.Contains(gone)) _motions.Remove(gone);

        if (_extraSlot is { } extraSlot)
        {
            var target = EssentialsGrid.CellOf(extraSlot, count, Columns, final.Width);
            if (_extra is null)
            {
                // The placeholder shows where it's going, straight away.
                _extra = new Motion();
                _extra.Snap(target);
            }
            Retarget(_extra, target, animate is not null, snap);
            PlaceExtra?.Invoke(new Rect(_extra.X, _extra.Y, Math.Max(0, _extra.Width), EssentialsGrid.RowHeight));
        }

        if (Moving()) StartTicking();
        return new Size(final.Width, EssentialsGrid.Rows(count, Columns) * EssentialsGrid.RowHeight);
    }

    /// <summary>
    /// A new target: sprung to when asked (or when already on its way, keeping its speed), otherwise straight
    /// there; the sidebar being dragged wider moves tiles along with it, not after it.
    /// </summary>
    private static void Retarget(Motion motion, EssentialsGrid.Cell target, bool animate, bool snap)
    {
        if (snap) motion.Snap(target);
        else if (motion.Target != target)
        {
            if (animate || motion.Moving)
            {
                motion.Target = target;
                motion.Moving = true;
            }
            else motion.Snap(target);
        }
    }

    private bool Moving()
    {
        if (_extra is { Moving: true }) return true;
        foreach (var motion in _motions.Values)
            if (motion.Moving) return true;
        return false;
    }

    private void StartTicking()
    {
        if (_ticking) return;
        _ticking = true;
        _lastFrame = null;
        CompositionTarget.Rendering += Frame;
    }

    private void StopTicking()
    {
        if (!_ticking) return;
        _ticking = false;
        CompositionTarget.Rendering -= Frame;
    }

    private void Frame(object? sender, object e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        var seconds = _lastFrame is { } last ? (now - last).TotalSeconds : 1 / 60.0;
        _lastFrame = now;
        if (seconds <= 0) return;
        // A long stall (the window hidden) carries on from where it was, not from far along.
        seconds = Math.Min(seconds, 0.1);

        foreach (var motion in _motions.Values) Step(motion, seconds);
        if (_extra is not null) Step(_extra, seconds);
        InvalidateArrange();
        if (!Moving()) StopTicking();
    }

    private void Step(Motion motion, double seconds)
    {
        if (!motion.Moving) return;
        var (duration, bounce) = _spring;
        var target = motion.Target;
        LayoutSpring.Step(ref motion.X, ref motion.SpeedX, target.X, seconds, duration, bounce);
        LayoutSpring.Step(ref motion.Y, ref motion.SpeedY, target.Y, seconds, duration, bounce);
        LayoutSpring.Step(ref motion.Width, ref motion.SpeedWidth, target.Width, seconds, duration, bounce);
        if (LayoutSpring.Resting(motion.X, motion.SpeedX, target.X)
            && LayoutSpring.Resting(motion.Y, motion.SpeedY, target.Y)
            && LayoutSpring.Resting(motion.Width, motion.SpeedWidth, target.Width))
            motion.Snap(target);
    }
}
