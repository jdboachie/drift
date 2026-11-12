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
/// The handles on a split's pages, as iPad has them: three dots centred at the top of each page, on a
/// little tab that sits on the page's top edge, half in the gap above it, so it takes no room and
/// covers only a sliver of the page's header. Click one for what you can do with that page alone (show it
/// alone, swap sides, put another page there, close it); drag one to swap the pages, each keeping its
/// width. Only the page you're on has its handle, and a stronger border.
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
            handle.PointerEntered += (_, _) => { _overHandle = true; ShowPageFocus(); };
            handle.PointerExited += (_, _) => { _overHandle = false; ShowPageFocus(); };
            // Reached with the keyboard, it shows while it has focus.
            handle.GotFocus += (_, _) => ShowPageFocus();
            handle.LostFocus += (_, _) => ShowPageFocus();
        }
    }

    /// <summary>The handles come with a split, and go with it.</summary>
    private void ShowPageHandles()
    {
        var shown = _shownSplit is not null;
        if (shown != _handlesShown)
        {
            _handlesShown = shown;
            LeftPageHandle.Visibility = RightPageHandle.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            LeftPageRing.Visibility = RightPageRing.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
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
        if (at == _pagePointer || _shownSplit is not { } split) return;
        _pagePointer = at;
        var (left, right) = PageHandles.Shown(at, AreaOf(MainCard), AreaOf(SplitCard));
        if (_handlesAt is not { } now || now.Left != (left || Kept(LeftPageHandle)) || now.Right != (right || Kept(RightPageHandle))) ShowPageFocus();
    }

    private Area AreaOf(FrameworkElement card)
    {
        var at = BoundsOf(card);
        return new Area(at.X, at.Y, at.Width, at.Height);
    }

    /// <summary>
    /// The page you're on has a border a step stronger. Only the page under the pointer shows its
    /// handle (PageHandles): it fades in as you move over the page, and out as you leave. Held, or
    /// reached with the keyboard (only the page you're on's can be), a handle stays. Under the
    /// pointer, or held, the dots are solid.
    /// </summary>
    private void ShowPageFocus()
    {
        if (_shownSplit is not { } split) return;
        var leftOn = FrontOf(split) == split.Left;
        LeftPageRing.Opacity = leftOn ? 1 : 0;
        RightPageRing.Opacity = leftOn ? 0 : 1;
        var (overLeft, overRight) = PageHandles.Shown(_pagePointer, AreaOf(MainCard), AreaOf(SplitCard));
        var shown = (ShowHandle(LeftPageHandle, LeftPageDots, overLeft, leftOn), ShowHandle(RightPageHandle, RightPageDots, overRight, !leftOn));
        LeftPageHandle.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty, $"{split.Left.Title}, page options");
        RightPageHandle.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty, $"{split.Right.Title}, page options");
        if (_handlesAt != shown)
        {
            _handlesAt = shown;
            DispatcherQueue.TryEnqueue(UpdateDragRegions);
        }

        bool ShowHandle(Button handle, UIElement dots, bool over, bool pageOn)
        {
            var on = over || Kept(handle);
            handle.Opacity = on ? 1 : 0;
            handle.IsHitTestVisible = on;
            // The keyboard reaches the handle of the page you're on.
            handle.IsTabStop = pageOn;
            dots.Opacity = _heldHandle == handle || (_overHandle && handle.IsPointerOver) ? 1 : 0.75;
            return on;
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
    }

    private BrowserTab? PageOf(object handle) =>
        _shownSplit is { } split ? handle == LeftPageHandle ? split.Left : split.Right : null;

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

    private static void PressDots(Button handle, bool held)
    {
        if (handle.Content is not UIElement dots) return;
        dots.CenterPoint = new Vector3(dots.ActualSize / 2, 0);
        Spring.To(dots, "Scale", held ? new Vector3(1.1f, 1.1f, 1) : Vector3.One, duration: 0.15, bounce: 0);
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

    // ── The menu ──

    private void PageHandle_Click(object sender, RoutedEventArgs e)
    {
        if (_handleDragged)
        {
            _handleDragged = false;
            return;
        }
        if (sender is not Button handle || PageOf(handle) is not { } page) return;
        var menu = new MenuFlyout();
        menu.Items.Add(Item("Show alone", "\uE740", () => ShowPageAlone(page)));
        menu.Items.Add(Item("Swap sides", "\uE8AB", () =>
        {
            if (page.Split is { } split) SwapPages(split, split.Left == page, 0, 0, 0);
        }));
        menu.Items.Add(Item("Replace page…", "\uE8A7", () =>
        {
            if (_active != page) Activate(page);
            OpenCommandBar(newTab: false, animate: true);
        }));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Close page", "\uE711", () => CloseSplitPage(page)));
        _menuHandle = handle;
        menu.Closed += (_, _) =>
        {
            if (_menuHandle == handle) _menuHandle = null;
            ShowPageFocus();
        };
        menu.ShowAt(handle, new FlyoutShowOptions { Placement = FlyoutPlacementMode.Bottom });

        static MenuFlyoutItem Item(string text, string glyph, Action act)
        {
            var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
            item.Click += (_, _) => act();
            return item;
        }
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
