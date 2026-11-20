using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Drift.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Drift;

/// <summary>
/// The handles on the pages, as iPad has them: three dots centred at the top of each page, on a little
/// tab that sits on the page's top edge, half in the gap above it, so it takes no room and covers only a
/// sliver of the page's header. Hover one (or click it) for what you can do with that page, in Arc's
/// round buttons (PageControls.cs); in a split, drag one to move the page among the others, each keeping
/// its width. A page on its own has one too. In a split, the page you're on has a stronger border.
/// Where a drag lands is PageMove's (Core, tested).
/// </summary>
public sealed partial class MainWindow
{
    // A press doesn't become a drag until it's moved this far: a click that wobbles is still a click.
    private const double HandleSlop = 6;

    private bool _handlesShown;
    private Button? _heldHandle;
    private bool _handleDragging;
    // When a drag by the dots ended: the click its release makes isn't one. A time, not a flag, since
    // a drag let go off the dots makes no click to clear it, and the next click would be lost.
    private long? _handleDraggedAt;
    private const int DragClickMs = 400;
    private bool _overHandle;
    private double _handleFrom;
    private double _handleOffset;
    private PageMove? _move;
    private int _movePlace;
    private VelocityTracker _handleVelocity = new();
    // Where each other page has got to, making room: the compositor's springs, followed here.
    private readonly Dictionary<int, (SpringMotion Spring, long At)> _roomSprings = new();

    private void SetUpPageHandles()
    {
        SetUpPanes();
        foreach (var handle in _panes.Select(p => p.Handle))
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

    // How many handles are out (a split's pages each have one).
    private int _handleCount;

    /// <summary>A page has its handle; in a split each page has one, and the rings.</summary>
    private void ShowPageHandles()
    {
        var shown = _active is not null;
        var count = !shown ? 0 : _shownSplit?.Count ?? 1;
        if (shown != _handlesShown || count != _handleCount)
        {
            _handlesShown = shown;
            _handleCount = count;
            for (var i = 0; i < _panes.Length; i++)
            {
                _panes[i].Handle.Visibility = i < count ? Visibility.Visible : Visibility.Collapsed;
                _panes[i].Ring.Visibility = _shownSplit is not null && i < count ? Visibility.Visible : Visibility.Collapsed;
            }
            DispatcherQueue.TryEnqueue(UpdateDragRegions);
        }
        ShowPageFocus();
    }

    // Which handles are showing, so the drag area's cut-outs follow them.
    private int _handlesAt = -1;

    // Where the pointer is in the window (CaptionWatch_Tick asks Windows: over a page, no pointer
    // events reach the window), or null when it's not over it.
    private (double X, double Y)? _pagePointer;

    /// <summary>The pointer's moved (or gone): the handles near it grow.</summary>
    private void PagePointerAt(Windows.Foundation.Point? pointer)
    {
        if (!_handlesShown) return;
        (double, double)? at = pointer is { } p ? (p.X, p.Y) : null;
        if (at == _pagePointer) return;
        _pagePointer = at;
        PageControlsPointerAt(at);
        ShowPageFocus();
        // Closer still, within PillReach of the dots, and their buttons open without a click.
        if (_heldHandle is null && !_handleDragging)
            foreach (var handle in _panes.Select(p => p.Handle))
                if (handle.Visibility == Visibility.Visible && PointerOn(handle)) ShowPageControls(handle);
    }

    // The pointer this close to a handle (from its middle), and it grows to its full size.
    private const double HandleNear = 56;
    // Closer, this close to the dots' edges, and their buttons open (and stay, while it's within this
    // or on them). Not only on the dots: having to touch them felt late, the pill coming after you'd
    // already arrived.
    private const double PillReach = 20;
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
        var front = split is null ? 0 : split.IndexOf(FrontOf(split));
        var mask = 0;
        for (var i = 0; i < _handleCount && i < _panes.Length; i++)
        {
            var pane = _panes[i];
            pane.Ring.Opacity = split is not null && i == front ? 1 : 0;
            ShowHandle(pane, i == front);
            var page = split is null ? _active : split.Pages[i];
            pane.Handle.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty, $"{page?.Title}, page options");
            mask |= 1 << i;
        }
        if (_handlesAt != mask)
        {
            _handlesAt = mask;
            DispatcherQueue.TryEnqueue(UpdateDragRegions);
        }

        void ShowHandle(PagePane pane, bool pageOn)
        {
            var handle = pane.Handle;
            handle.Opacity = 1;
            handle.IsHitTestVisible = true;
            // The keyboard reaches the handle of the page you're on.
            handle.IsTabStop = pageOn;
            var under = _heldHandle == handle || (_overHandle && handle.IsPointerOver);
            var near = under || Kept(handle) || PointerNear(handle);
            pane.Tab.Opacity = near ? 1 : 0;
            pane.Dots.Opacity = under ? 1 : near ? 0.9 : 0.8;
            ScaleDots(pane.Dots, _heldHandle == handle ? 1.1f : near ? 1 : RestingDots);
            DropHandle(handle, near);
        }
    }

    /// <summary>
    /// A handle that stays out whatever the pointer's over: held (being dragged), with keyboard focus,
    /// or with its menu open.
    /// </summary>
    private bool Kept(Button handle) =>
        _heldHandle == handle || handle.FocusState is FocusState.Keyboard || _menuHandle == handle;

    private Button? _menuHandle;

    /// <summary>Where the shown handles are, for the window's drag area and its buttons' reveal to leave alone.</summary>
    private IEnumerable<Area> PageHandleAreas()
    {
        if (!_handlesShown) yield break;
        foreach (var handle in _panes.Select(p => p.Handle))
        {
            if (handle.Visibility != Visibility.Visible || !handle.IsHitTestVisible) continue;
            var at = BoundsOf(handle);
            yield return new Area(at.X, at.Y, at.Width, at.Height);
        }
        if (PageControlsShown && _pageControls is { } pill)
        {
            var at = BoundsOf(pill);
            yield return new Area(at.X, at.Y, at.Width, at.Height);
        }
    }

    /// <summary>Which pane a handle is on.</summary>
    private int PaneOf(object handle) => Array.FindIndex(_panes, p => ReferenceEquals(p.Handle, handle));

    private BrowserTab? PageOf(object handle)
    {
        var at = PaneOf(handle);
        if (_shownSplit is { } split) return at >= 0 && at < split.Count ? split.Pages[at] : null;
        return at == 0 ? _active : null;
    }

    // ── Pressing and dragging ──

    private void Handle_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_shownSplit is null || IsPeekShown || sender is not Button handle) return;
        _heldHandle = handle;
        _handleDragging = false;
        _handleFrom = e.GetCurrentPoint(PageColumns).Position.X;
        _handleOffset = 0;
        // At once, so the press is answered: the dots darken and swell a touch.
        ShowPageFocus();
    }

    private void Handle_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_heldHandle != sender || _shownSplit is not { } split) return;
        var raw = e.GetCurrentPoint(PageColumns).Position.X - _handleFrom;
        if (!_handleDragging)
        {
            if (Math.Abs(raw) < HandleSlop) return;
            StartPageMove(split);
        }
        var move = _move!;
        var now = Environment.TickCount64;
        _handleVelocity.Add(now, raw);
        _handleOffset = move.Follow(raw);
        foreach (var part in PaneParts(move.Dragged)) Spring.Jump(part, "Translation", new Vector3((float)_handleOffset, 0, 0));
        // Its middle across a divider: the page beyond moves into the place it left (or back).
        var place = move.Place(_handleOffset);
        if (place != _movePlace)
        {
            _movePlace = place;
            for (var i = 0; i < split.Count; i++)
            {
                if (i == move.Dragged) continue;
                var to = move.MakesRoom(i, place);
                foreach (var part in PaneParts(i)) Spring.To(part, "Translation", new Vector3((float)to, 0, 0), duration: 0.3, bounce: 0);
                FollowRoomSpring(i, to);
            }
        }
        e.Handled = true;
    }

    private void Handle_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_heldHandle != sender || sender is not Button handle) return;
        _heldHandle = null;
        (handle as PageHandleButton)?.ShowMoving(false);
        if (!_handleDragging || _move is not { } move)
        {
            ShowPageFocus();
            return;
        }
        _handleDragging = false;
        // The click that ends a drag isn't one.
        _handleDraggedAt = Environment.TickCount64;
        var velocity = _handleVelocity.Velocity(Environment.TickCount64);
        Lift(move.Dragged, false);
        var lands = move.LandsIn(_handleOffset, velocity);
        if (_shownSplit is { } split && lands != move.Dragged && move.Dragged < split.Count)
        {
            var others = Enumerable.Range(0, split.Count).Select(RoomNow).ToArray();
            MovePage(split, move.Dragged, lands, i => i == move.Dragged ? _handleOffset : others[i], velocity);
        }
        else
        {
            LowerSoon(move.Dragged);
            // Back where it was, carrying on at the pointer's speed; the others back too.
            foreach (var part in PaneParts(move.Dragged))
                Spring.To(part, "Translation", Vector3.Zero, duration: 0.3, bounce: 0.15,
                    from: new Vector3((float)_handleOffset, 0, 0), velocity: new Vector3((float)velocity, 0, 0));
            if (_movePlace != move.Dragged)
                for (var i = 0; i < _handleCount; i++)
                    if (i != move.Dragged)
                        foreach (var part in PaneParts(i)) Spring.To(part, "Translation", Vector3.Zero, duration: 0.3, bounce: 0);
        }
        _move = null;
        _roomSprings.Clear();
        ShowPageFocus();
    }

    private void StartPageMove(TabSplit split)
    {
        _handleDragging = true;
        HidePageControls();
        (_heldHandle as PageHandleButton)?.ShowMoving(true);
        var dragged = PaneOf(_heldHandle!);
        StopEvenSpring();
        _move = new PageMove(Enumerable.Range(0, split.Count).Select(i => _panes[i].Card.ActualWidth).ToArray(), dragged, SplitGap);
        _movePlace = dragged;
        _roomSprings.Clear();
        _handleVelocity = new VelocityTracker();
        _handleVelocity.Add(Environment.TickCount64, 0);
        // Picked up: over the others, and a touch smaller, as a window lifts on iPad.
        for (var i = 0; i < _panes.Length; i++)
            if (i != dragged) Raise(i, false);
        Lift(dragged, true);
        // You're on the page you picked up.
        var page = split.Pages[dragged];
        if (_active != page) Activate(page);
    }

    /// <summary>
    /// Picked up, a page is over the others and outlined in the accent colour; put down, it's back to
    /// its size and outline, but stays over them until it's back in its place (LowerSoon), or it would
    /// slide home under the page beside it.
    /// </summary>
    private void Lift(int pane, bool up)
    {
        if (up)
        {
            Raise(pane, true);
            _panes[pane].Ring.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        }
        else _panes[pane].Ring.ClearValue(Border.BorderBrushProperty);
        var card = _panes[pane].Card;
        foreach (var part in new UIElement[] { card, _panes[pane].Ring })
        {
            part.CenterPoint = new Vector3(card.ActualSize / 2, 0);
            Spring.To(part, "Scale", up ? new Vector3(0.98f, 0.98f, 1) : Vector3.One, duration: 0.25, bounce: 0);
        }
    }

    private void Raise(int pane, bool over)
    {
        foreach (var part in PaneParts(pane)) Canvas.SetZIndex(part, over ? 2 : 0);
        if (over) CardAfterTheOthers(_panes[pane].Card);
    }

    /// <summary>
    /// A card moved after the other cards in the page area (still before the borders and handles). A web
    /// page is drawn by the engine and stacked by where its card is in the tree, whatever its z-index:
    /// with z-index alone the card rose, but its page stayed under the page beside it, as did its
    /// face, leaving only its border on top. At rest the cards don't overlap, so the order's kept.
    /// </summary>
    private void CardAfterTheOthers(PageCardView card)
    {
        var children = PageColumns.Children;
        var at = children.IndexOf(card);
        var last = _panes.Max(p => children.IndexOf(p.Card));
        if (at >= 0 && at < last) children.Move((uint)at, (uint)last);
    }

    // The latest page let go (or moved), so an older one's lowering doesn't lower it.
    private int _lowering;

    /// <summary>A page let go, or moved: over the others until its spring's all but done, then level with them.</summary>
    private async void LowerSoon(int pane)
    {
        var mine = ++_lowering;
        await System.Threading.Tasks.Task.Delay(450);
        if (mine == _lowering && _move is null) Raise(pane, false);
    }

    // What each set of dots is on its way to, so a spring starts only when that changes.
    private readonly Dictionary<UIElement, float> _dotsScale = new();

    private void ScaleDots(UIElement dots, float to)
    {
        if (_dotsScale.TryGetValue(dots, out var now) && now == to) return;
        _dotsScale[dots] = to;
        dots.CenterPoint = new Vector3(dots.ActualSize / 2, 0);
        // Growing as the pointer comes, shrinking back quicker as it goes.
        var growing = to > (now == 0 ? RestingDots : now);
        if (_uiSettings.AnimationsEnabled) Spring.To(dots, "Scale", new Vector3(to, to, 1), duration: growing ? 0.15 : 0.1, bounce: 0);
        else Spring.Jump(dots, "Scale", new Vector3(to, to, 1));
    }

    // Which handles are down (grown), so a spring starts only when that changes.
    private readonly Dictionary<UIElement, bool> _handleDown = new();

    // What's drawn in it moves, not the handle itself: a drag moves that sideways (PaneParts).
    private void DropHandle(Button handle, bool down)
    {
        if (handle.Content is not UIElement face) return;
        if (_handleDown.TryGetValue(face, out var now) && now == down) return;
        _handleDown[face] = down;
        var to = new Vector3(0, down ? HandleDrop : 0, 0);
        // Down as the pointer comes, back up quicker as it goes.
        if (_uiSettings.AnimationsEnabled) Spring.To(face, "Translation", to, duration: down ? 0.2 : 0.13, bounce: 0);
        else Spring.Jump(face, "Translation", to);
    }

    /// <summary>The pointer within PillReach of the handle: the reach that opens its buttons and keeps them open.</summary>
    private bool PointerOn(FrameworkElement handle)
    {
        if (_pagePointer is not var (x, y) || handle.ActualWidth <= 0) return false;
        var at = BoundsOf(handle);
        return x >= at.X - PillReach && x < at.X + at.Width + PillReach && y >= at.Y - PillReach && y < at.Y + at.Height + PillReach;
    }

    /// <summary>The pointer within reach of the handle's middle (where it rests).</summary>
    private bool PointerNear(FrameworkElement handle)
    {
        if (_pagePointer is not var (x, y) || handle.ActualWidth <= 0) return false;
        var at = BoundsOf(handle);
        var (dx, dy) = (x - (at.X + at.Width / 2), y - (at.Y + at.Height / 2));
        return dx * dx + dy * dy <= HandleNear * HandleNear;
    }

    /// <summary>A page's spring making room, stepped alongside the compositor's, so letting go knows where it's got to.</summary>
    private void FollowRoomSpring(int pane, double to)
    {
        var at = RoomNow(pane);
        _roomSprings[pane] = (new SpringMotion(0.3, 0) { Position = at, Target = to }, Environment.TickCount64);
    }

    private double RoomNow(int pane)
    {
        if (!_roomSprings.TryGetValue(pane, out var room)) return 0;
        var now = Environment.TickCount64;
        room.Spring.Step(Math.Min(1, (now - room.At) / 1000.0));
        _roomSprings[pane] = (room.Spring, now);
        return room.Spring.Position;
    }

    // ── Moving ──

    /// <summary>
    /// A page moves to another place, every page keeping its width. They're laid out in their new places
    /// at once, then drawn from where they were and spring home: the moved one carrying on at the
    /// pointer's speed (with a touch of bounce, having been thrown), the others without.
    /// </summary>
    /// <param name="drawnAt">How far each page (by its place before) is drawn from its place.</param>
    private void MovePage(TabSplit split, int from, int to, Func<int, double> drawnAt, double velocity)
    {
        var before = split.Pages.ToArray();
        var drawn = Enumerable.Range(0, before.Length).Select(i => _panes[i].Card.ActualOffset.X + drawnAt(i)).ToArray();

        split.Move(from, to);
        HostPages(split);
        LayoutPages();
        PageColumns.UpdateLayout();
        // The page that moved, now in its new place, goes over the others on its way there.
        for (var i = 0; i < _panes.Length; i++) Raise(i, i == to);
        LowerSoon(to);

        var thrown = new Vector3((float)velocity, 0, 0);
        for (var place = 0; place < split.Count; place++)
        {
            var was = Array.IndexOf(before, split.Pages[place]);
            var moved = was == from;
            var start = new Vector3((float)(drawn[was] - _panes[place].Card.ActualOffset.X), 0, 0);
            foreach (var part in PaneParts(place))
                Spring.To(part, "Translation", Vector3.Zero, duration: 0.3, bounce: moved ? 0.15 : 0,
                    from: start, velocity: moved ? thrown : null);
        }

        PlaceSplitCards();
        ShowPageFocus();
        SaveSessionSoon();
        DispatcherQueue.TryEnqueue(UpdateDragRegions);
    }

    // ── The controls ──

    /// <summary>A click on the dots (or Enter on them): the page's controls, or away again.</summary>
    private void PageHandle_Click(object sender, RoutedEventArgs e)
    {
        if (_handleDraggedAt is { } dragged && Environment.TickCount64 - dragged < DragClickMs) return;
        if (sender is not Button handle) return;
        if (_controlsFor == handle && PageControlsShown) HidePageControls();
        else ShowPageControls(handle);
    }

    /// <summary>This page on its own, in front; the others stay together (or, one left, it's a tab of its own again).</summary>
    private void ShowPageAlone(BrowserTab page)
    {
        if (page.Split is not { } split) return;
        if (_active != page) Activate(page);
        var start = ShownPageStart(split);
        TakeOutOfSplit(page);
        if (start is { } at) AnimateSeparate(at);
    }
}
