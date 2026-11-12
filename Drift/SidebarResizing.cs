using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Drift;

/// <summary>
/// Resizing the docked sidebar by dragging its edge. The edge stays under the pointer, where it was
/// grabbed; past the widest or narrowest it gives a little with resistance and springs back on
/// letting go. Past the narrowest the sidebar fades as it nears hiding, and let go far enough shut
/// (or flicked shut) it carries on closing at the pointer's speed and hides. Double-clicking the edge
/// springs it to the default width. The rules are SidebarResize's (Core, tested); the Essentials
/// take more columns as it widens (EssentialsGrid, LayoutEssentials).
/// The width is layout, which the compositor can't animate, so springs are stepped each frame
/// (SpringMotion), as for the split divider.
/// </summary>
public sealed partial class MainWindow
{
    // The handle is 10 wide, centred on the 4px gap between the sidebar's content and the page.
    private const double ResizeHandleInset = 2 + 5;

    private bool _resizing;
    private bool _resizeMoved;
    private bool _overResizeHandle;
    private double _resizeGrab;
    private double _resizeRaw;
    private double _resizeFrom;
    private VelocityTracker _resizeVelocity = new();

    private SpringMotion? _widthSpring;
    private long _widthFrame;
    private Action? _widthSettled;

    /// <summary>
    /// The handle on the sidebar's edge; gone while the sidebar's hidden, and out of sight and reach
    /// while the library's in its place (or coming). This runs on every step of a library swipe, so it
    /// changes nothing that needs laying out unless it has to: collapsing the handle as a swipe opened
    /// the library laid out the whole window on the swipe's first frames, and they were dropped.
    /// </summary>
    private void PlaceResizeHandle()
    {
        var shut = !_libraryOpen && _libraryWidth <= SidebarWidth + 0.5;
        var visibility = _sidebarHidden ? Visibility.Collapsed : Visibility.Visible;
        if (SidebarResizeHandle.Visibility != visibility) SidebarResizeHandle.Visibility = visibility;
        // Neither of these needs layout.
        if (SidebarResizeHandle.IsHitTestVisible != shut) SidebarResizeHandle.IsHitTestVisible = shut;
        SidebarResizeHandle.Opacity = shut ? 1 : 0;
        var margin = new Thickness(Math.Max(0, SidebarColumn.Width.Value - ResizeHandleInset), 8, 0, 8);
        if (SidebarResizeHandle.Margin != margin) SidebarResizeHandle.Margin = margin;
    }

    /// <summary>The width from last time, before anything's shown.</summary>
    private void RestoreSidebarWidth(double width)
    {
        _sidebarWidth = Math.Clamp(width, SidebarResize.Min, SidebarResize.Max);
        if (_sidebarHidden) return;
        ShowSidebarWidth(_sidebarWidth);
        SetSidebarWidth(_sidebarWidth);
    }

    /// <summary>The docked sidebar at a width, mid-drag or mid-spring: its column, and its edge's handle.</summary>
    private void ShowSidebarWidth(double width)
    {
        _sidebarWidth = width;
        _libraryWidth = width;
        SidebarColumn.Width = new GridLength(Math.Max(0, width));
        PlaceResizeHandle();
    }

    // ── Hover and press: the grip, as split view's ──

    private void ShowSidebarGrip() => ShowGrip(SidebarGrip, SidebarGripAccent, _overResizeHandle, _resizing, atRest: 0);

    private void ResizeHandle_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _overResizeHandle = true;
        ShowSidebarGrip();
    }

    private void ResizeHandle_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _overResizeHandle = false;
        ShowSidebarGrip();
    }

    private void ResizeHandle_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Not while the library's in its place, nor while it's closing itself after a drag.
        if (_sidebarHidden || _libraryOpen || _libraryStepping || _widthSettled is not null) return;
        if (!SidebarResizeHandle.CapturePointer(e.Pointer)) return;
        // Caught mid-spring: from wherever it's got to.
        StopWidthSpring();
        _resizing = true;
        _resizeMoved = false;
        _resizeFrom = _sidebarWidth;
        // Where on the edge it was grabbed, so the edge doesn't jump to the pointer.
        _resizeGrab = e.GetCurrentPoint(Root).Position.X - _sidebarWidth;
        _resizeRaw = _sidebarWidth;
        _resizeVelocity = new VelocityTracker();
        _resizeVelocity.Add(Environment.TickCount64, _resizeRaw);
        // At once, so the press is answered.
        ShowSidebarGrip();
        e.Handled = true;
    }

    private void ResizeHandle_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizing) return;
        var raw = e.GetCurrentPoint(Root).Position.X - _resizeGrab;
        // A click (or a double-click) that wobbles a pixel isn't a drag.
        if (!_resizeMoved && Math.Abs(raw - _resizeFrom) < 2) return;
        _resizeMoved = true;
        _resizeRaw = raw;
        _resizeVelocity.Add(Environment.TickCount64, raw);
        // Past the point of hiding: it goes there and then, carrying on at the pointer's speed.
        if (SidebarResize.HidesNow(raw))
        {
            _resizing = false;
            SidebarResizeHandle.ReleasePointerCapture(e.Pointer);
            ShowSidebarGrip();
            HideSidebarByDrag(_resizeVelocity.Velocity(Environment.TickCount64));
            e.Handled = true;
            return;
        }
        ShowSidebarWidth(SidebarResize.Follow(raw));
        // Past the narrowest, it fades as it nears hiding: letting go there hides it.
        Sidebar.Opacity = 1 - 0.65 * SidebarResize.HideProgress(raw);
        e.Handled = true;
    }

    private void ResizeHandle_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizing) return;
        SidebarResizeHandle.ReleasePointerCapture(e.Pointer);
        LetGoOfSidebarEdge();
        e.Handled = true;
    }

    private void ResizeHandle_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (_resizing) LetGoOfSidebarEdge();
    }

    private void LetGoOfSidebarEdge()
    {
        _resizing = false;
        ShowSidebarGrip();
        if (!_resizeMoved) return;
        var velocity = _resizeVelocity.Velocity(Environment.TickCount64);
        var (hide, width) = SidebarResize.LetGo(_resizeRaw, velocity);
        if (hide)
        {
            HideSidebarByDrag(velocity);
            return;
        }
        if (Sidebar.Opacity < 1) Ui.Fade(Sidebar, 1);
        // Past a limit, back to it with a little give; inside them, it's already where it was let go.
        if (Math.Abs(width - _sidebarWidth) > 0.5) SpringSidebarWidth(width, velocity: 0, bounce: 0.15);
        else SettleSidebarWidth();
    }

    /// <summary>
    /// Let go far enough shut: it carries on closing at the pointer's speed as it fades, then hides,
    /// keeping the width it had before the drag for when it's shown again.
    /// </summary>
    private void HideSidebarByDrag(double velocity)
    {
        var keep = Math.Clamp(_resizeFrom, SidebarResize.Min, SidebarResize.Max);
        Ui.Fade(Sidebar, 0);
        SpringSidebarWidth(0, velocity: Math.Min(0, velocity), bounce: 0, done: () =>
        {
            _sidebarWidth = keep;
            SetSidebarHidden(true);
            Sidebar.Opacity = 1;
            SaveSessionSoon();
        });
    }

    /// <summary>Double-click: the default width, under way at once like an ease-out, without bounce.</summary>
    private void ResizeHandle_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_sidebarHidden || _libraryOpen || _libraryStepping || _widthSettled is not null) return;
        var to = SidebarResize.Default;
        if (Math.Abs(_sidebarWidth - to) < 0.5) return;
        SpringSidebarWidth(to, velocity: (to - _sidebarWidth) * 4, bounce: 0);
        e.Handled = true;
    }

    // ── The width's spring ──

    private void SpringSidebarWidth(double target, double velocity, double bounce, Action? done = null)
    {
        StopWidthSpring();
        _widthSettled = done;
        if (!_uiSettings.AnimationsEnabled)
        {
            ShowSidebarWidth(target);
            FinishWidthSpring();
            return;
        }
        _widthSpring = new SpringMotion(0.3, bounce) { Position = _sidebarWidth, Target = target, Velocity = velocity };
        _widthFrame = Environment.TickCount64;
        CompositionTarget.Rendering += WidthFrame;
    }

    private void WidthFrame(object? sender, object e)
    {
        if (_widthSpring is not { } spring)
        {
            StopWidthSpring();
            return;
        }
        var now = Environment.TickCount64;
        spring.Step(Math.Min(0.05, (now - _widthFrame) / 1000.0));
        _widthFrame = now;
        if (!spring.AtRest)
        {
            ShowSidebarWidth(spring.Position);
            return;
        }
        ShowSidebarWidth(spring.Target);
        StopWidthSpring();
        FinishWidthSpring();
    }

    private void FinishWidthSpring()
    {
        var done = _widthSettled;
        _widthSettled = null;
        if (done is not null) done();
        else SettleSidebarWidth();
    }

    private void StopWidthSpring()
    {
        CompositionTarget.Rendering -= WidthFrame;
        _widthSpring = null;
    }

    /// <summary>At rest at its new width: everything that hangs off the width catches up, and it's kept.</summary>
    private void SettleSidebarWidth()
    {
        SetSidebarWidth(_sidebarWidth);
        SaveSessionSoon();
    }
}
