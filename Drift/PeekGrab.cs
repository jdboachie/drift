using System;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace Drift;

/// <summary>
/// Pulling Peek's card down by its grabber to close it, as a sheet is. The grabber is out of sight
/// above the card until the pointer rests at the window's top over it; then the card comes down a
/// little (its bottom staying put) to show it, below the window's drag edge. When that happens is
/// CaptionReveal's, as for the window buttons; how the drag follows and when it closes, PeekDrag's
/// (both tested). This is the window's side: the card tracks the pointer 1:1, and on letting go
/// either carries on down at the pointer's speed as Peek fades, or springs back.
/// </summary>
public sealed partial class MainWindow
{
    // How far the card comes down to show the grabber.
    private const float PeekLower = 14;
    private const double PeekGrabberWidth = 160;
    // Lowered: from the window's top edge to a little way onto the card.
    private const double PeekGrabberReach = 8 + PeekLower + 10;
    // Where resting the pointer at the top is for Peek's grabber (not the window buttons): either side of its middle.
    private const double PeekTopZoneHalfWidth = 120;

    private readonly CaptionReveal _peekLowered = new();
    private DispatcherQueueTimer? _peekCenterRestore;

    private bool _peekGrabbing;
    private double _peekGrabFrom;
    private double _peekGrabY;
    private Vector3 _peekCenterAtRest;
    private bool _peekCenterHeld;
    private VelocityTracker _peekGrabVelocity = new();

    /// <summary>Set by a drag that closed Peek, for ClosePeek: out downwards at this speed, not back to the link.</summary>
    private double? _peekFlungAt;

    private double PeekMiddle => SidebarColumn.ActualWidth + PeekLayer.ActualWidth / 2;

    private bool PeekOpenForGrab => IsPeekShown && PeekLayer.Visibility == Visibility.Visible && _closingPeekView is null;

    /// <summary>The grabber, while it's showing: cut out of the window's drag area.</summary>
    private Area? PeekGrabberArea =>
        PeekOpenForGrab && _peekLowered.Shown
            ? new Area(PeekMiddle - PeekGrabberWidth / 2, 0, PeekGrabberWidth, PeekGrabberReach)
            : null;

    /// <summary>The top of the window over Peek's middle: for the grabber, not the window buttons.</summary>
    private bool OverPeekTop(Windows.Foundation.Point pointer) =>
        PeekOpenForGrab && Math.Abs(pointer.X - PeekMiddle) <= PeekTopZoneHalfWidth && pointer.Y < PeekGrabberReach + 12;

    /// <summary>The card, lowered or not: the same shift and scale the drag starts from.</summary>
    private (float Y, float Scale) PeekRest()
    {
        if (!_peekLowered.Shown) return (0, 1);
        var height = PeekCardFrame.ActualHeight;
        // Shrunk about its top by as much as it comes down, so its bottom edge stays where it was.
        return (PeekLower, height > PeekLower ? (float)(1 - PeekLower / height) : 1);
    }

    /// <summary>From the window buttons' watch, every 25ms: whether the card comes down to show its grabber.</summary>
    private void PeekLowerTick(Windows.Foundation.Point? pointer, bool buttonDown)
    {
        if (!PeekOpenForGrab)
        {
            if (_peekLowered.Shown) _peekLowered.Reset();
            return;
        }
        var at = PointerAt.Away;
        if (_peekGrabbing) at = PointerAt.Bar;
        else if (pointer is { } p && Math.Abs(p.X - PeekMiddle) <= PeekTopZoneHalfWidth)
        {
            // Heading for it at the top edge; once it's down, anywhere on the grabber and a little round it.
            if (p.Y < PageArea.Margin.Top + 4) at = PointerAt.TopNearButtons;
            else if (_peekLowered.Shown && p.Y < PeekGrabberReach + 12) at = PointerAt.Bar;
        }
        switch (_peekLowered.Update(Environment.TickCount64, at, buttonDown && !_peekGrabbing))
        {
            case CaptionChange.Show: LowerPeek(true); break;
            case CaptionChange.Hide: LowerPeek(false); break;
        }
    }

    /// <summary>Down a little to show the grabber, or back up: on a spring without bounce, about 250ms.</summary>
    private void LowerPeek(bool lower)
    {
        _peekCenterRestore?.Stop();
        if (lower) HoldPeekCenterAtTop();
        var (y, scale) = PeekRest();
        Spring.To(PeekCardFrame, "Translation", new Vector3(0, y, 0), duration: 0.25, bounce: 0);
        Spring.To(PeekCardFrame, "Scale", new Vector3(scale, scale, 1), duration: 0.25, bounce: 0);
        PeekGrabber.IsHitTestVisible = lower;
        PeekGrabberBar.Opacity = lower ? 0.35 : 0;
        if (!lower) RestorePeekCenterSoon();
        UpdateDragRegions();
    }

    /// <summary>Scaling about the card's top middle while lowered or dragged; its own (towards the link) kept for closing.</summary>
    private void HoldPeekCenterAtTop()
    {
        if (!_peekCenterHeld) _peekCenterAtRest = PeekCardFrame.CenterPoint;
        _peekCenterHeld = true;
        PeekCardFrame.CenterPoint = new Vector3((float)PeekCardFrame.ActualWidth / 2, 0, 0);
    }

    /// <summary>Once it's back at scale 1 (where the centre makes no difference), its own centre again.</summary>
    private void RestorePeekCenterSoon()
    {
        if (_peekCenterRestore is null)
        {
            _peekCenterRestore = DispatcherQueue.CreateTimer();
            _peekCenterRestore.Interval = TimeSpan.FromMilliseconds(400);
            _peekCenterRestore.IsRepeating = false;
            _peekCenterRestore.Tick += (_, _) =>
            {
                if (_peekLowered.Shown || _peekGrabbing || !_peekCenterHeld || _closingPeekView is not null) return;
                PeekCardFrame.CenterPoint = _peekCenterAtRest;
                _peekCenterHeld = false;
            };
        }
        _peekCenterRestore.Start();
    }

    /// <summary>Peek's gone: nothing lowered or held.</summary>
    private void ResetPeekGrab()
    {
        _peekLowered.Reset();
        _peekGrabbing = false;
        _peekCenterHeld = false;
        _peekCenterRestore?.Stop();
        PeekGrabber.IsHitTestVisible = false;
        PeekGrabberBar.Opacity = 0;
    }

    private void PeekGrabber_PointerEntered(object sender, PointerRoutedEventArgs e) => PeekGrabberBar.Opacity = 0.7;

    private void PeekGrabber_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_peekGrabbing && _peekLowered.Shown) PeekGrabberBar.Opacity = 0.35;
    }

    private void PeekGrabber_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!PeekOpenForGrab || !_peekLowered.Shown) return;
        if (!PeekGrabber.CapturePointer(e.Pointer)) return;
        _peekGrabbing = true;
        PeekGrabberBar.Opacity = 0.7;
        _peekGrabFrom = e.GetCurrentPoint(Root).Position.Y;
        _peekGrabY = 0;
        HoldPeekCenterAtTop();
        _peekGrabVelocity = new VelocityTracker();
        _peekGrabVelocity.Add(Environment.TickCount64, 0);
        e.Handled = true;
    }

    private void PeekGrabber_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_peekGrabbing) return;
        _peekGrabY = e.GetCurrentPoint(Root).Position.Y - _peekGrabFrom;
        _peekGrabVelocity.Add(Environment.TickCount64, _peekGrabY);
        var height = PeekCardFrame.ActualHeight;
        var (y, rest) = PeekRest();
        var scale = rest * (float)PeekDrag.Scale(_peekGrabY, height);
        Spring.Jump(PeekCardFrame, "Translation", new Vector3(0, y + (float)PeekDrag.Follow(_peekGrabY), 0));
        Spring.Jump(PeekCardFrame, "Scale", new Vector3(scale, scale, 1));
        // The buttons beside it shrink away as it goes.
        var controls = (float)(1 - 0.3 * Math.Clamp(_peekGrabY / (height * PeekDrag.DismissShare), 0, 1));
        Spring.Jump(PeekControls, "Scale", new Vector3(controls, controls, 1));
        e.Handled = true;
    }

    private void PeekGrabber_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_peekGrabbing) return;
        PeekGrabber.ReleasePointerCapture(e.Pointer);
        LetGoOfPeek(Environment.TickCount64);
        e.Handled = true;
    }

    private void PeekGrabber_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (_peekGrabbing) LetGoOfPeek(Environment.TickCount64);
    }

    private void LetGoOfPeek(long now)
    {
        _peekGrabbing = false;
        PeekGrabberBar.Opacity = 0.35;
        var velocity = _peekGrabVelocity.Velocity(now);
        if (PeekDrag.Dismisses(_peekGrabY, velocity, PeekCardFrame.ActualHeight))
        {
            _peekFlungAt = velocity;
            ClosePeek(animate: true);
            return;
        }
        // Back up to where it was lowered to, carrying on from the pointer's speed; a little give, since it was thrown.
        var (y, scale) = PeekRest();
        var at = PeekCardFrame.Translation;
        Spring.To(PeekCardFrame, "Translation", new Vector3(0, y, 0), duration: 0.35, bounce: 0.1,
            from: at, velocity: new Vector3(0, (float)velocity, 0));
        Spring.To(PeekCardFrame, "Scale", new Vector3(scale, scale, 1), duration: 0.35, bounce: 0);
        Spring.To(PeekControls, "Scale", Vector3.One, duration: 0.25, bounce: 0);
    }

    /// <summary>
    /// Closed by a pull: on down the way it was going, at the pointer's speed, while Peek fades
    /// (PeekExitAnimation), rather than back to the link.
    /// </summary>
    private void SpringPeekAway(double velocity)
    {
        var at = PeekCardFrame.Translation;
        var away = new Vector3(0, at.Y + (float)Math.Max(PeekCardFrame.ActualHeight * 0.4, PeekDrag.Project(velocity)), 0);
        Spring.To(PeekCardFrame, "Translation", away, duration: 0.3, bounce: 0,
            from: at, velocity: new Vector3(0, (float)Math.Max(0, velocity), 0));
        var shrunk = (float)(1 - PeekDrag.Shrink);
        Spring.To(PeekCardFrame, "Scale", new Vector3(shrunk, shrunk, 1), duration: 0.3, bounce: 0);
        // Already shrinking away with the pull: they carry on, and fade (PeekExitAnimation).
        Spring.To(PeekControls, "Scale", new Vector3(0.7f, 0.7f, 1), duration: 0.2, bounce: 0);
    }
}
