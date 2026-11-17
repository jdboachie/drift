using System;
using System.Collections.Generic;
using System.Numerics;
using Drift.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace Drift;

/// <summary>
/// The handles on the pages, as iPad has them: three dots centred at the top of each page, on a little
/// tab that sits on the page's top edge, half in the gap above it, so it takes no room and covers only a
/// sliver of the page's header. Hover one (or click it) for what you can do with that page, in Arc's
/// round buttons (PageControls.cs); in a split, drag one to swap the pages, each keeping its width. A
/// page on its own has one too. In a split, the page you're on has a stronger border.
/// When a drag swaps is PageSwap's (Core, tested).
/// </summary>
public sealed partial class MainWindow
{
    // A press doesn't become a drag until it's moved this far: a click that wobbles is still a click.
    private const double HandleSlop = 6;

    private bool _handlesShown;
    private Button? _heldHandle;
    private bool _handleDragging;
    private bool _handleDragged;
    private bool _overHandle;
    private double _handleFrom;
    private double _handleOffset;
    private PageSwap? _swap;
    private bool _swapCrossed;
    private VelocityTracker _handleVelocity = new();
    // Where the other page has got to, making room: the compositor's spring, followed here.
    private SpringMotion? _roomSpring;
    private long _roomSpringAt;

    private void SetUpPageHandles()
    {
        foreach (var handle in new[] { LeftPageHandle, RightPageHandle })
        {
            // The button keeps presses to itself, so listen to handled ones too.
            handle.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Handle_PointerPressed), true);
            handle.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(Handle_PointerMoved), true);
            handle.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(Handle_PointerReleased), true);
            handle.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(Handle_PointerReleased), true);
            handle.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(Handle_PointerReleased), true);
            handle.PointerEntered += (sender, _) =>
            {
                _overHandle = true;
                ShowPageFocus();
                if (_heldHandle is null) ShowPageControls((Button)sender);
            };
            handle.PointerExited += (_, _) =>
            {
                _overHandle = false;
                ShowPageFocus();
                HidePageControlsSoon();
            };
            // Reached with the keyboard, it shows while it has focus.
            handle.GotFocus += (_, _) => ShowPageFocus();
            handle.LostFocus += (_, _) => ShowPageFocus();
        }
    }

    // Whether the right page's handle is out too (a split's showing).
    private bool _splitHandlesShown;

    /// <summary>A page has its handle; a split's second page has one too, and the rings.</summary>
    private void ShowPageHandles()
    {
        var shown = _active is not null;
        var split = _shownSplit is not null;
        if (shown != _handlesShown || split != _splitHandlesShown)
        {
            _handlesShown = shown;
            _splitHandlesShown = split;
            LeftPageHandle.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            RightPageHandle.Visibility = split ? Visibility.Visible : Visibility.Collapsed;
            LeftPageRing.Visibility = RightPageRing.Visibility = split ? Visibility.Visible : Visibility.Collapsed;
            DispatcherQueue.TryEnqueue(UpdateDragRegions);
        }
        ShowPageFocus();
    }

    // Which handles are showing, so the drag area's cut-outs follow them.
    private (bool Left, bool Right)? _handlesAt;

    // Where the pointer is in the window (CaptionWatch_Tick asks Windows: over a page, no pointer
    // events reach the window), or null when it's not over it.
    private (double X, double Y)? _pagePointer;

    /// <summary>The pointer's moved (or gone): the page under it shows its handle.</summary>
    private void PagePointerAt(Windows.Foundation.Point? pointer)
    {
        if (!_handlesShown) return;
        (double, double)? at = pointer is { } p ? (p.X, p.Y) : null;
        if (at == _pagePointer) return;
        _pagePointer = at;
        PageControlsPointerAt(at);
        ShowPageFocus();
        // Closer still, on the dots (or all but), and their buttons open without a click.
        if (_heldHandle is null && !_handleDragging)
            foreach (var handle in new[] { LeftPageHandle, RightPageHandle })
                if (handle.Visibility == Visibility.Visible && PointerOn(handle)) ShowPageControls(handle);
    }

    /// <summary>The right page's bounds, or nowhere when there's no split.</summary>
    private Area RightArea() => _shownSplit is null ? new Area(0, 0, 0, 0) : AreaOf(SplitCard);

    private Area AreaOf(FrameworkElement card)
    {
        var at = BoundsOf(card);
        return new Area(at.X, at.Y, at.Width, at.Height);
    }

    // The pointer this close to a handle (from its middle), and it grows to its full size.
    private const double HandleNear = 56;
    // At rest, the dots this size of their full one, with no tab under them: Arc's small profile, clear to see.
    private const float RestingDots = 0.85f;
    // At rest the dots sit high in the gap above the page; grown, they come down this far, so their tab
    // isn't pressed against the top of the window.
    private const float HandleDrop = 5;

    /// <summary>
    /// The page you're on has a border a step stronger. Each page's dots are always there, small and
    /// faint, as Arc's are; with the pointer near them they grow to their tab (held, reached with the
    /// keyboard, or with the page's controls open, too). Under the pointer, or held, they're solid.
    /// </summary>
    private void ShowPageFocus()
    {
        if (!_handlesShown) return;
        var split = _shownSplit;
        var leftOn = split is null || FrontOf(split) == split.Left;
        LeftPageRing.Opacity = split is not null && leftOn ? 1 : 0;
        RightPageRing.Opacity = split is not null && !leftOn ? 1 : 0;
        var shown = (ShowHandle(LeftPageHandle, LeftPageTab, LeftPageDots, leftOn),
            split is not null && ShowHandle(RightPageHandle, RightPageTab, RightPageDots, !leftOn));
        LeftPageHandle.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty, $"{(split?.Left ?? _active)?.Title}, page options");
        if (split is not null)
            RightPageHandle.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty, $"{split.Right.Title}, page options");
        if (_handlesAt != shown)
        {
            _handlesAt = shown;
            DispatcherQueue.TryEnqueue(UpdateDragRegions);
        }

        bool ShowHandle(Button handle, UIElement tab, UIElement dots, bool pageOn)
        {
            handle.Opacity = 1;
            handle.IsHitTestVisible = true;
            // The keyboard reaches the handle of the page you're on.
            handle.IsTabStop = pageOn;
            var under = _heldHandle == handle || (_overHandle && handle.IsPointerOver);
            var near = under || Kept(handle) || PointerNear(handle);
            tab.Opacity = near ? 1 : 0;
            dots.Opacity = under ? 1 : near ? 0.9 : 0.8;
            ScaleDots(dots, _heldHandle == handle ? 1.1f : near ? 1 : RestingDots);
            DropHandle(handle, near);
            return true;
        }
    }

    /// <summary>
    /// A handle that stays out whatever the pointer's over: held (being dragged), with keyboard focus,
    /// or with its menu open.
    /// </summary>
    private bool Kept(Button handle) =>
        _heldHandle == handle || handle.FocusState is FocusState.Keyboard || _menuHandle == handle;

    private Button? _menuHandle;

    /// <summary>Where the shown handle is, for the window's drag area and its buttons' reveal to leave alone.</summary>
    private IEnumerable<Area> PageHandleAreas()
    {
        if (!_handlesShown) yield break;
        foreach (var handle in new[] { LeftPageHandle, RightPageHandle })
        {
            if (!handle.IsHitTestVisible) continue;
            var at = BoundsOf(handle);
            yield return new Area(at.X, at.Y, at.Width, at.Height);
        }
        if (_pageControls is { Opacity: > 0 } pill)
        {
            var at = BoundsOf(pill);
            yield return new Area(at.X, at.Y, at.Width, at.Height);
        }
    }

    private BrowserTab? PageOf(object handle) =>
        _shownSplit is { } split ? handle == LeftPageHandle ? split.Left : split.Right
        : handle == LeftPageHandle ? _active : null;

    // ── Pressing and dragging ──

    private void Handle_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_shownSplit is null || IsPeekShown || sender is not Button handle) return;
        _heldHandle = handle;
        _handleDragging = false;
        _handleDragged = false;
        _handleFrom = e.GetCurrentPoint(PageColumns).Position.X;
        _handleOffset = 0;
        // At once, so the press is answered: the dots darken and swell a touch.
        ShowPageFocus();
        PressDots(handle, held: true);
    }

    private void Handle_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_heldHandle != sender || _shownSplit is not { } split) return;
        var raw = e.GetCurrentPoint(PageColumns).Position.X - _handleFrom;
        if (!_handleDragging)
        {
            if (Math.Abs(raw) < HandleSlop) return;
            StartSwapDrag(split);
        }
        var now = Environment.TickCount64;
        _handleVelocity.Add(now, raw);
        _handleOffset = _swap!.Follow(raw);
        foreach (var part in Parts(_dragSideLeft)) Spring.Jump(part, "Translation", new Vector3((float)_handleOffset, 0, 0));
        // Its middle across the divider: the other page moves into the place it left (or back).
        var crossed = _swap.Crossed(_handleOffset);
        if (crossed != _swapCrossed)
        {
            _swapCrossed = crossed;
            var to = crossed ? _swap.OtherMakesRoom : 0;
            foreach (var part in Parts(!_dragSideLeft)) Spring.To(part, "Translation", new Vector3((float)to, 0, 0), duration: 0.3, bounce: 0);
            FollowRoomSpring(to);
        }
        e.Handled = true;
    }

    private void Handle_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_heldHandle != sender || sender is not Button handle) return;
        _heldHandle = null;
        PressDots(handle, held: false);
        (handle as PageHandleButton)?.ShowMoving(false);
        if (!_handleDragging)
        {
            ShowPageFocus();
            return;
        }
        _handleDragging = false;
        // The click that ends a drag isn't one.
        _handleDragged = true;
        var velocity = _handleVelocity.Velocity(Environment.TickCount64);
        Lift(_dragSideLeft, false);
        if (_shownSplit is { } split && _swap!.Swaps(_handleOffset, velocity)) SwapPages(split, _dragSideLeft, _handleOffset, RoomNow(), velocity);
        else
        {
            // Back where it was, carrying on at the pointer's speed; the other page back too.
            foreach (var part in Parts(_dragSideLeft))
                Spring.To(part, "Translation", Vector3.Zero, duration: 0.3, bounce: 0.15,
                    from: new Vector3((float)_handleOffset, 0, 0), velocity: new Vector3((float)velocity, 0, 0));
            if (_swapCrossed) foreach (var part in Parts(!_dragSideLeft)) Spring.To(part, "Translation", Vector3.Zero, duration: 0.3, bounce: 0);
        }
        _swap = null;
        _swapCrossed = false;
        _roomSpring = null;
        ShowPageFocus();
    }

    // Which page's being dragged: the left one, or the right.
    private bool _dragSideLeft;

    private void StartSwapDrag(TabSplit split)
    {
        _handleDragging = true;
        HidePageControls();
        (_heldHandle as PageHandleButton)?.ShowMoving(true);
        _dragSideLeft = _heldHandle == LeftPageHandle;
        StopEvenSpring();
        _swap = new PageSwap(_dragSideLeft, MainCard.ActualWidth, SplitCard.ActualWidth, SplitGap);
        _swapCrossed = false;
        _roomSpring = null;
        _handleVelocity = new VelocityTracker();
        _handleVelocity.Add(Environment.TickCount64, 0);
        // Picked up: over the other page, and a touch smaller, as a window lifts on iPad.
        Lift(_dragSideLeft, true);
        // You're on the page you picked up.
        var page = _dragSideLeft ? split.Left : split.Right;
        if (_active != page) Activate(page);
    }

    /// <summary>A page and what goes with it: its card, its border, its handle.</summary>
    private UIElement[] Parts(bool left) =>
        left ? [MainCard, LeftPageRing, LeftPageHandle] : [SplitCard, RightPageRing, RightPageHandle];

    private void Lift(bool left, bool up)
    {
        foreach (var part in Parts(left)) Canvas.SetZIndex(part, up ? 2 : 0);
        var card = left ? MainCard : SplitCard;
        var ring = left ? LeftPageRing : RightPageRing;
        foreach (var part in new UIElement[] { card, ring })
        {
            part.CenterPoint = new Vector3(card.ActualSize / 2, 0);
            Spring.To(part, "Scale", up ? new Vector3(0.98f, 0.98f, 1) : Vector3.One, duration: 0.25, bounce: 0);
        }
    }

    // Pressed, the dots swell a touch (ShowPageFocus, straight after).
    private static void PressDots(Button handle, bool held) { }

    // What each set of dots is on its way to, so a spring starts only when that changes.
    private readonly Dictionary<UIElement, float> _dotsScale = new();

    private void ScaleDots(UIElement dots, float to)
    {
        if (_dotsScale.TryGetValue(dots, out var now) && now == to) return;
        _dotsScale[dots] = to;
        dots.CenterPoint = new Vector3(dots.ActualSize / 2, 0);
        if (_uiSettings.AnimationsEnabled) Spring.To(dots, "Scale", new Vector3(to, to, 1), duration: 0.15, bounce: 0);
        else Spring.Jump(dots, "Scale", new Vector3(to, to, 1));
    }

    // Which handles are down (grown), so a spring starts only when that changes.
    private readonly Dictionary<UIElement, bool> _handleDown = new();

    // What's drawn in it moves, not the handle itself: a drag moves that sideways (Parts).
    private void DropHandle(Button handle, bool down)
    {
        if (handle.Content is not UIElement face) return;
        if (_handleDown.TryGetValue(face, out var now) && now == down) return;
        _handleDown[face] = down;
        var to = new Vector3(0, down ? HandleDrop : 0, 0);
        if (_uiSettings.AnimationsEnabled) Spring.To(face, "Translation", to, duration: 0.2, bounce: 0);
        else Spring.Jump(face, "Translation", to);
    }

    /// <summary>The pointer on the handle, or within a few pixels of it: the same reach that keeps its buttons open.</summary>
    private bool PointerOn(FrameworkElement handle)
    {
        if (_pagePointer is not var (x, y) || handle.ActualWidth <= 0) return false;
        var at = BoundsOf(handle);
        return x >= at.X - 6 && x < at.X + at.Width + 6 && y >= at.Y - 6 && y < at.Y + at.Height + 6;
    }

    /// <summary>The pointer within reach of the handle's middle (where it rests).</summary>
    private bool PointerNear(FrameworkElement handle)
    {
        if (_pagePointer is not var (x, y) || handle.ActualWidth <= 0) return false;
        var at = BoundsOf(handle);
        var (dx, dy) = (x - (at.X + at.Width / 2), y - (at.Y + at.Height / 2));
        return dx * dx + dy * dy <= HandleNear * HandleNear;
    }

    /// <summary>The other page's spring, stepped alongside the compositor's, so letting go knows where it's got to.</summary>
    private void FollowRoomSpring(double to)
    {
        var now = Environment.TickCount64;
        var at = RoomNow();
        _roomSpring = new SpringMotion(0.3, 0) { Position = at, Target = to };
        _roomSpringAt = now;
    }

    private double RoomNow()
    {
        if (_roomSpring is not { } spring) return 0;
        var now = Environment.TickCount64;
        spring.Step(Math.Min(1, (now - _roomSpringAt) / 1000.0));
        _roomSpringAt = now;
        return spring.Position;
    }

    // ── Swapping ──

    /// <summary>
    /// The pages change sides, each keeping its width. They're laid out in their new places at once,
    /// then drawn from where they were and spring home: the dragged one carrying on at the pointer's
    /// speed (with a touch of bounce, having been thrown), the other without.
    /// </summary>
    /// <param name="draggedIsLeft">Which page moved first (the one dragged, or chosen in the menu).</param>
    /// <param name="draggedAt">How far it's been drawn from its place.</param>
    /// <param name="otherAt">How far the other has been drawn from its place.</param>
    private void SwapPages(TabSplit split, bool draggedIsLeft, double draggedAt, double otherAt, double velocity)
    {
        var (leftWidth, rightWidth) = (MainCard.ActualWidth, SplitCard.ActualWidth);
        // Where each page is drawn now, from the page area's left.
        var leftWasAt = (draggedIsLeft ? draggedAt : otherAt);
        var rightWasAt = leftWidth + SplitGap + (draggedIsLeft ? otherAt : draggedAt);

        split.Swap();
        MoveView(split.Left.View, WebHost);
        MoveView(split.Right.View, SplitHost);
        LayoutPages();
        PageColumns.UpdateLayout();

        // The old right page is the new left one, at 0; the old left one now starts after it.
        var newRightStart = rightWidth + SplitGap;
        var leftFrom = (float)rightWasAt;
        var rightFrom = (float)(leftWasAt - newRightStart);
        var thrown = new Vector3((float)velocity, 0, 0);
        // The page that moved first is now on the other side.
        foreach (var part in Parts(left: true))
            Spring.To(part, "Translation", Vector3.Zero, duration: 0.3, bounce: draggedIsLeft ? 0 : 0.15,
                from: new Vector3(leftFrom, 0, 0), velocity: draggedIsLeft ? null : thrown);
        foreach (var part in Parts(left: false))
            Spring.To(part, "Translation", Vector3.Zero, duration: 0.3, bounce: draggedIsLeft ? 0.15 : 0,
                from: new Vector3(rightFrom, 0, 0), velocity: draggedIsLeft ? thrown : null);

        PlaceSplitCards();
        ShowPageFocus();
        SaveSessionSoon();
        DispatcherQueue.TryEnqueue(UpdateDragRegions);
    }

    // ── The controls ──

    /// <summary>A click on the dots (or Enter on them): the page's controls, or away again.</summary>
    private void PageHandle_Click(object sender, RoutedEventArgs e)
    {
        if (_handleDragged)
        {
            _handleDragged = false;
            return;
        }
        if (sender is not Button handle) return;
        if (_controlsFor == handle && _pageControls is { Opacity: > 0 }) HidePageControls();
        else ShowPageControls(handle);
    }

    /// <summary>This page on its own, in front; the other goes back to being a tab of its own.</summary>
    private void ShowPageAlone(BrowserTab page)
    {
        if (page.Split is not { } split) return;
        var rightShown = page == split.Right;
        var rightStart = LeftPaneColumn.ActualWidth + SplitGap;
        if (_active != page) Activate(page);
        SeparateTabs(split);
        if (rightShown) AnimateSeparate(rightStart);
    }
}
