using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Drift.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.Interactions;
using Microsoft.UI.Xaml.Hosting;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace Drift;

/// <summary>
/// The library, as Zen has it: Downloads and History in the sidebar's place, twice its width, with a
/// rail to switch between them. It opens from the library button, Ctrl+J and
/// Ctrl+H, or by swiping the sidebar sideways on a touchpad, which widens it under the finger (Zen's
/// swipe, measured frame by frame): let go and it springs open or shut, the way it was flicked, or
/// to whichever is nearer. The tabs give way to the library early on (LibrarySwipe.Fade).
/// The sidebar's width is layout, which the compositor can't animate, so a spring is stepped each
/// frame (SpringMotion); both are in Core, tested.
/// </summary>
public sealed partial class MainWindow
{
    // Twice the default sidebar, and always wider than the sidebar as it's been sized.
    private double LibraryWidth => Math.Max(2 * SidebarResize.Default, SidebarWidth + 140);

    /// <summary>About 0.3s, overshooting about 10px either way: a little springier than Zen (2–3px), which felt flat.</summary>
    private const double LibrarySpringSeconds = 0.3;
    private const double LibraryBounce = 0.3;

    /// <summary>Opened or shut from rest, it starts off at this many times the distance a second.</summary>
    private const double LibraryKick = LibrarySwipe.CarryPerPixel;

    /// <summary>Pixels of sidebar per unit of a touchpad's sideways scroll (a notch is 120).</summary>
    private const double SwipePixelsPerDelta = 0.5;

    /// <summary>No sideways scroll for this long: the fingers have lifted.</summary>
    private const int SwipeEndMs = 140;

    // From the sidebar's width as it is now, which it can be dragged to.
    private LibrarySwipe _librarySwipe => new(SidebarWidth, LibraryWidth);
    private readonly SpringMotion _librarySpring = new(LibrarySpringSeconds, LibraryBounce) { Position = SidebarResize.Default, Target = SidebarResize.Default };
    private readonly Stopwatch _libraryClock = Stopwatch.StartNew();
    private double _libraryWidth = SidebarResize.Default;
    private bool _libraryOpen;
    private bool _libraryStepping;
    private long _lastLibraryFrame;
    // The section it opens on: the one last shown, at first the first (Bookmarks).
    private string _libraryPage = "bookmarks";

    // A swipe in progress: where the fingers have taken the sidebar (before any give at the ends),
    // and the last moments of it, for its speed when they lift.
    private double? _swipeTo;
    private double _wheelFrom;
    private readonly List<(long Ms, double Width)> _swipeTrail = new();
    private DispatcherQueueTimer? _swipeEnd;

    private void SetUpLibrary()
    {
        // The lists scroll up and down themselves (and mark the wheel handled); sideways is the library's.
        Sidebar.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(Sidebar_Wheel), true);
        Library.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(Sidebar_Wheel), true);
        SetUpSwipe();
        Sidebar.SizeChanged += (_, _) =>
            Sidebar.CenterPoint = new System.Numerics.Vector3((float)Sidebar.ActualWidth / 2, (float)Sidebar.ActualHeight / 2, 0);
        // Always there, and drawn, just transparent and out of reach while shut: showing it for the
        // first time on each open cost a few frames, and the slide started already a third done.
        Library.Visibility = Visibility.Visible;
        Library.Opacity = 0;
        Library.IsHitTestVisible = false;
        // Nor can the keyboard reach it then: Tab goes on to the page instead.
        Library.GettingFocus += (_, e) =>
        {
            if (_libraryOpen || _trackerSwiping || _swipeTo is not null) return;
            if (_active?.View is { } view && e.TrySetNewFocusedElement(view)) return;
            e.TryCancel();
        };
    }

    // ───────────────────────── Opening and closing ─────────────────────────

    private void OpenLibrary(string page)
    {
        _libraryProps?.InsertScalar("Pulling", 0);
        if (_commandBarOpen) CloseCommandBar(focusPage: false);
        HideDownloadPeek();
        ShowLibraryPage(page);
        MoveLibrary(open: true);
    }

    /// <summary>Ctrl+J, Ctrl+H: open on that section, or close if it's the one showing.</summary>
    private void ToggleLibrary(string page)
    {
        if (_libraryOpen && _libraryPage == page) CloseLibrary();
        else
        {
            OpenLibrary(page);
            // Ready to type a search.
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => LibrarySearch.Focus(FocusState.Programmatic));
        }
    }

    private void CloseLibrary()
    {
        if (!_libraryOpen) return;
        _libraryProps?.InsertScalar("Pulling", 0);
        if (LibrarySearch.FocusState != FocusState.Unfocused || IsFocusIn(Library)) _active?.View.Focus(FocusState.Programmatic);
        MoveLibrary(open: false);
    }

    /// <summary>
    /// Springs the sidebar open into the library or shut, carrying on at a speed (a swipe's). The
    /// compositor runs the spring (on the width that everything's bound to, BindToWidth); the same
    /// spring is stepped here only to know where it's got to, and to put things at rest when it's done.
    /// Moving things from the UI thread instead, the first frames never made it to the screen, and
    /// the slide seemed to start late and already a third done.
    /// </summary>
    private void MoveLibrary(bool open, double velocity = 0)
    {
        _libraryOpen = open;
        // Opening: the sidebar's edge goes at once (it's back once the library's shut).
        PlaceResizeHandle();
        if (open) LibraryOpened();
        // Hidden: the floating sidebar's not to slide away (or out) while the library's coming.
        if (open && _sidebarHidden)
        {
            _sidebarWatch.Stop();
            _sidebarDismissTimer.Stop();
            _sidebarDismissing = false;
        }
        var target = open ? LibraryWidth : ClosedWidth;
        // Not from a swipe, from a button or a key: under way at once, like an ease-out.
        if (velocity == 0) velocity = (target - _libraryWidth) * LibraryKick;
        _librarySpring.Position = _libraryWidth;
        _librarySpring.Velocity = velocity;
        _librarySpring.Target = target;
        // Clicks go to where it's going.
        Library.IsHitTestVisible = open;
        Sidebar.IsHitTestVisible = !open;
        if (!new Windows.UI.ViewManagement.UISettings().AnimationsEnabled || _libraryProps is null)
        {
            StopLibraryMotion();
            SetSidebarWidth(target);
            LibrarySettled();
            return;
        }

        var spring = _libraryProps.Compositor.CreateSpringScalarAnimation();
        spring.Target = "Width";
        spring.InitialValue = (float)_libraryWidth;
        spring.InitialVelocity = (float)velocity;
        spring.FinalValue = (float)target;
        spring.DampingRatio = (float)(1 - LibraryBounce);
        // The compositor's Period is 1/omega (see Spring).
        spring.Period = TimeSpan.FromSeconds(LibrarySpringSeconds / (2 * Math.PI));
        BindToWidth();
        _libraryProps.StartAnimation("Width", spring);

        if (_libraryStepping) return;
        _libraryStepping = true;
        _lastLibraryFrame = _libraryClock.ElapsedMilliseconds;
        CompositionTarget.Rendering += LibraryFrame;
    }

    private void LibraryFrame(object? sender, object e)
    {
        var now = _libraryClock.ElapsedMilliseconds;
        _librarySpring.Step((now - _lastLibraryFrame) / 1000.0);
        _lastLibraryFrame = now;
        _libraryWidth = _librarySpring.Position;
        if (!_librarySpring.AtRest) return;
        StopLibraryMotion();
        SetSidebarWidth(_librarySpring.Target);
        LibrarySettled();
    }

    private void StopLibraryFrames()
    {
        if (!_libraryStepping) return;
        _libraryStepping = false;
        CompositionTarget.Rendering -= LibraryFrame;
    }

    /// <summary>Whatever's moving the sidebar stops, and it's left where it had got to.</summary>
    private void StopLibraryMotion()
    {
        StopLibraryFrames();
        if (_libraryBindings.Count == 0) return;
        SetSidebarWidth(_libraryWidth);
        foreach (var (element, animation) in _libraryBindings) element.StopAnimation(animation);
        _libraryBindings.Clear();
        _libraryProps?.StopAnimation("Width");
    }

    /// <summary>
    /// The sidebar at a width between its own and the library's, at rest: the library slides in with
    /// the page's edge, and the tabs fade into it, keeping their own width rather than stretch. The
    /// page doesn't get narrower: it's only drawn moved over (its Translation), running off the
    /// window's right edge, and Peek with it. Its layout never changes, so nothing waits on the web
    /// page to redraw at a new size (laying it out each frame ran the whole window at 15fps).
    /// </summary>
    private void SetSidebarWidth(double width)
    {
        _libraryWidth = width;
        _libraryProps?.InsertScalar("Width", (float)width);
        var shift = new System.Numerics.Vector3((float)Math.Max(0, width - ClosedWidth), 0, 0);
        PageArea.Translation = shift;
        PeekPageFrame.Translation = shift;
        PeekLayer.Translation = shift;
        var (tabs, library) = Swipe.Fade(width);
        // Its right edge on the page's.
        if (Library.Width != LibraryWidth) Library.Width = LibraryWidth;
        Library.Translation = new System.Numerics.Vector3((float)(width - LibraryWidth), 0, 0);
        Library.Opacity = library;
        Library.IsHitTestVisible = library >= 0.5;
        Sidebar.Opacity = tabs;
        Sidebar.IsHitTestVisible = tabs > 0.5;
        // The edge is there only with the library shut.
        PlaceResizeHandle();
        // (Floating, the sidebar's Translation is its slide in and out: Spring's.)
        if (!_sidebarHidden)
        {
            var leaving = 1 - (float)tabs;
            Sidebar.Translation = new System.Numerics.Vector3((float)Math.Min(0, width - ClosedWidth), 0, 0);
            var scale = 1 - (float)TabsShrink * leaving;
            Sidebar.Scale = new System.Numerics.Vector3(scale, scale, 1);
        }
    }

    /// <summary>
    /// As the library comes in, the tabs shrink away about the sidebar's middle while they fade, as
    /// in Zen (measured: 0.96 the size as they go, so the top drops ~15px and the left edge moves in
    /// a few). Nothing else moves them.
    /// </summary>
    private const double TabsShrink = 0.04;

    /// <summary>The width everything's bound to while it moves (BindToWidth).</summary>
    private CompositionPropertySet? _libraryProps;

    private readonly List<(UIElement Element, ExpressionAnimation Animation)> _libraryBindings = new();

    /// <summary>
    /// While the sidebar moves, the compositor moves everything itself every frame, from one width
    /// that a spring or the fingers drive: SetSidebarWidth, as expressions.
    /// </summary>
    private void BindToWidth()
    {
        if (_libraryProps is null || _libraryBindings.Count > 0) return;
        static string N(double v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var (closed, open) = (N(ClosedWidth), N(LibraryWidth));
        // LibrarySwipe.Fade, written out: the tabs go, then the library comes.
        var way = $"((p.Width - {closed}) / ({open} - {closed}))";
        var leaving = $"Clamp({way} / {N(LibrarySwipe.TabsGone)}, 0, 1)";
        var library = $"Clamp(({way} - {N(LibrarySwipe.LibraryFrom)}) / {N(LibrarySwipe.LibraryFull - LibrarySwipe.LibraryFrom)}, 0, 1)";
        // Short of the sidebar's own width: pulled there by the fingers (Pulling), it's the sidebar that
        // goes, and the page stays. Springing back past it after a close, the page overshoots a little,
        // under the tabs, and comes back.
        var under = $"Min(0, p.Width - {closed})";
        var shift = $"Vector3(p.Width - {closed} - p.Pulling * {under}, 0, 0)";
        Bind(PageArea, "Translation", shift);
        Bind(PeekPageFrame, "Translation", shift);
        Bind(PeekLayer, "Translation", shift);
        Bind(Library, "Translation", $"Vector3(p.Width - {open}, 0, 0)");
        Bind(Library, "Opacity", library);
        Bind(Sidebar, "Opacity", $"1 - {leaving}");
        // Shrinking away. (Floating, its Translation is its slide in and out.)
        if (!_sidebarHidden) Bind(Sidebar, "Translation", $"Vector3(p.Pulling * {under}, 0, 0)");
        Bind(Sidebar, "Scale", $"Vector3(1 - {N(TabsShrink)} * {leaving}, 1 - {N(TabsShrink)} * {leaving}, 1)");

        void Bind(UIElement element, string property, string expression)
        {
            var animation = _libraryProps.Compositor.CreateExpressionAnimation(expression);
            animation.SetReferenceParameter("p", _libraryProps);
            animation.Target = property;
            element.StartAnimation(animation);
            _libraryBindings.Add((element, animation));
        }
    }

    private void LibrarySettled()
    {
        // Hidden, the floating sidebar it opened from has gone into it; shut, it's back to hidden.
        if (_sidebarHidden && _libraryOpen) Sidebar.Visibility = Visibility.Collapsed;
        SwipeLog("TIMING settled");
        FlushSwipeLog();
        UpdateDragRegions();
        if (!_libraryOpen && _libraryPage == "downloads") Downloads.MarkSeen();
    }

    /// <summary>The library's coming into view: its section up to date.</summary>
    private void LibraryOpened()
    {
        RefreshLibraryPage();
        if (_libraryPage == "downloads") Downloads.MarkSeen();
    }

    /// <summary>Docked, floating and out, or in the library: somewhere to swipe.</summary>
    private bool SidebarShowing => !_sidebarHidden || _libraryOpen || (Sidebar.Visibility == Visibility.Visible && !_sidebarDismissing);

    /// <summary>
    /// Where the page's edge is with the library shut: after the sidebar, or with it hidden, at the
    /// window's edge (inside the page's margin). Hidden, the library opens just as it does docked,
    /// the page sliding over from there, and shuts back to the hidden sidebar.
    /// </summary>
    private double ClosedWidth => _sidebarHidden ? FloatingInset : SidebarWidth;

    private LibrarySwipe _hiddenSwipe => new(FloatingInset, LibraryWidth);
    private LibrarySwipe Swipe => _sidebarHidden ? _hiddenSwipe : _librarySwipe;

    private static bool IsFocusIn(UIElement container)
    {
        var focused = FocusManager.GetFocusedElement(container.XamlRoot) as DependencyObject;
        for (var at = focused; at is not null; at = VisualTreeHelper.GetParent(at))
            if (at == container) return true;
        return false;
    }

    // ───────────────────────── Swiping ─────────────────────────

    // A touchpad's two-finger swipe never reaches the app as scrolling over the sidebar: its lists
    // (ScrollViewers) take the whole gesture to the compositor. So the compositor reports it instead,
    // to an InteractionTracker on the window's content that only moves sideways: up and down still
    // goes to the lists, and a sideways swipe chains out of them to this. Its position isn't used to
    // draw anything; it's read as how far the fingers have gone, and the sidebar follows.

    private InteractionTracker? _swipeTracker;
    private float _swipeOrigin;
    private double _swipeFrom;
    private bool _trackerSwiping;
    private VisualInteractionSource? _swipeSource;
    private bool _swipeSourceOn;

    private void SetUpSwipe()
    {
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(Root);
            _libraryProps = visual.Compositor.CreatePropertySet();
            _libraryProps.InsertScalar("Width", (float)SidebarWidth);
            _libraryProps.InsertScalar("Pulling", 0);
            _swipeTracker = InteractionTracker.CreateWithOwner(visual.Compositor, new SwipeOwner(this));
            _swipeTracker.MinPosition = new System.Numerics.Vector3(-100000, 0, 0);
            _swipeTracker.MaxPosition = new System.Numerics.Vector3(100000, 0, 0);
            var source = VisualInteractionSource.Create(visual);
            source.ManipulationRedirectionMode = VisualInteractionSourceRedirectionMode.Off;
            source.PositionXSourceMode = InteractionSourceMode.EnabledWithInertia;
            source.PositionYSourceMode = InteractionSourceMode.Disabled;
            source.ScaleSourceMode = InteractionSourceMode.Disabled;
            source.IsPositionXRailsEnabled = true;
            // It catches touchpad swipes anywhere in the window, the page's too (even out of the
            // tracker, while redirecting), so it only redirects while the pointer is over the sidebar
            // or the library.
            _swipeSource = source;
            _swipeTracker.InteractionSources.Add(source);
            Root.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(SwipeArea_PointerMoved), true);
        }
        catch (Exception ex)
        {
            SwipeLog("setup failed: " + ex.Message);
        }
    }

    private void SwipeArea_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_swipeTracker is null || _swipeSource is null) return;
        var on = LibrarySwipe.Catches(e.GetCurrentPoint(Root).Position.X, SwipeAreaRight(), _trackerSwiping);
        if (on == _swipeSourceOn) return;
        _swipeSourceOn = on;
        _swipeSource.ManipulationRedirectionMode = on
            ? VisualInteractionSourceRedirectionMode.CapableTouchpadOnly
            : VisualInteractionSourceRedirectionMode.Off;
    }

    /// <summary>Where the sidebar, or the library, ends: past it is the page.</summary>
    private double SwipeAreaRight()
    {
        double RightOf(FrameworkElement element) =>
            element.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point(element.ActualWidth, 0)).X;
        var right = 0.0;
        if (Sidebar.Visibility == Visibility.Visible && Sidebar.IsHitTestVisible && !_sidebarDismissing) right = RightOf(Sidebar);
        if (Library.IsHitTestVisible) right = Math.Max(right, RightOf(Library));
        return right;
    }

    // Temporary, to see what a swipe sends.
    private readonly System.Text.StringBuilder _swipeLog = new();
    private void SwipeLog(string text) => _swipeLog.Append($"{_libraryClock.Elapsed.TotalMilliseconds:0.0} {text}{Environment.NewLine}");
    private void FlushSwipeLog()
    {
        System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "drift-wheel.log"), _swipeLog.ToString());
        _swipeLog.Clear();
    }

    private void TrackerSwipeStarted(float position)
    {
        SwipeLog($"interacting at {position:0.0}");
        if (!SidebarShowing || _swipeTracker is null || _libraryProps is null) return;
        _trackerSwiping = true;
        _swipeOrigin = position;
        // From wherever a spring had got to.
        StopLibraryFrames();
        _swipeFrom = _libraryWidth;
        // The section shown in full (its page, its rail button), not just its list refreshed.
        if (!_libraryOpen) ShowLibraryPage(_libraryPage);
        Library.IsHitTestVisible = false;

        // The fingers drive the width: LibrarySwipe.Follow, written out.
        static string N(double v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var raw = "(from - (t.Position.X - origin))";
        var edge = $"Clamp({raw}, {N(ClosedWidth)}, {N(LibraryWidth)})";
        var follow = _libraryProps.Compositor.CreateExpressionAnimation(
            $"{edge} + ({raw} - {edge}) / (1 + Abs({raw} - {edge}) / {N(LibrarySwipe.Stretch)})");
        follow.SetReferenceParameter("t", _swipeTracker);
        follow.SetScalarParameter("from", (float)_swipeFrom);
        follow.SetScalarParameter("origin", _swipeOrigin);
        follow.Target = "Width";
        _libraryProps.InsertScalar("Pulling", 1);
        BindToWidth();
        _libraryProps.StartAnimation("Width", follow);
    }

    private void TrackerSwipeMoved(float position)
    {
        if (!_trackerSwiping) return;
        // Fingers moving right pull the position down, and widen the sidebar. (Only noted here: the
        // compositor draws it.)
        _libraryWidth = Swipe.Follow(_swipeFrom - (position - _swipeOrigin));
    }

    /// <summary>The fingers have lifted, moving at this speed: a spring takes over from them.</summary>
    private void TrackerSwipeEnded(float velocity)
    {
        SwipeLog($"released at {velocity:0.0}/s, width {_libraryWidth:0}");
        if (!_trackerSwiping) return;
        _trackerSwiping = false;
        _swipeTracker?.TryUpdatePosition(_swipeTracker.Position);
        // Let go short of the sidebar's width, the sidebar springs back, not the page.
        _libraryProps?.InsertScalar("Pulling", _libraryWidth < ClosedWidth ? 1 : 0);
        var speed = -velocity;
        var open = Swipe.Opens(_swipeFrom, _libraryWidth, speed);
        if (!open && IsFocusIn(Library)) _active?.View.Focus(FocusState.Programmatic);
        var carry = Swipe.Carry(_libraryWidth, open ? LibraryWidth : ClosedWidth, speed);
        // Not none: a swipe let go of still shouldn't get a button's kick.
        MoveLibrary(open, carry == 0 ? 0.001 : carry);
    }

    private sealed class SwipeOwner(MainWindow window) : IInteractionTrackerOwner
    {
        private void OnUi(Action action) => window.DispatcherQueue.TryEnqueue(() => action());

        public void InteractingStateEntered(InteractionTracker sender, InteractionTrackerInteractingStateEnteredArgs args)
        {
            var at = sender.Position.X;
            OnUi(() => window.TrackerSwipeStarted(at));
        }

        public void ValuesChanged(InteractionTracker sender, InteractionTrackerValuesChangedArgs args)
        {
            var at = args.Position.X;
            OnUi(() => window.TrackerSwipeMoved(at));
        }

        public void InertiaStateEntered(InteractionTracker sender, InteractionTrackerInertiaStateEnteredArgs args)
        {
            var velocity = args.PositionVelocityInPixelsPerSecond.X;
            OnUi(() => window.TrackerSwipeEnded(velocity));
        }

        public void IdleStateEntered(InteractionTracker sender, InteractionTrackerIdleStateEnteredArgs args) =>
            OnUi(() => { if (window._trackerSwiping) window.TrackerSwipeEnded(0); });

        public void CustomAnimationStateEntered(InteractionTracker sender, InteractionTrackerCustomAnimationStateEnteredArgs args) { }

        public void RequestIgnored(InteractionTracker sender, InteractionTrackerRequestIgnoredArgs args) { }
    }

    // A mouse with a sideways wheel (or tilt) does reach the app as scrolling.

    /// <summary>
    /// A touchpad's two-finger swipe reaches the app as sideways scrolling, a little at a time, with
    /// nothing to say when the fingers lift: a pause is taken as that.
    /// </summary>
    private void Sidebar_Wheel(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Root);
        if (!point.Properties.IsHorizontalMouseWheel || !SidebarShowing) return;
        e.Handled = true;

        if (_swipeTo is null)
        {
            StopLibraryMotion();
            _swipeTo = _libraryWidth;
            _wheelFrom = _libraryWidth;
            _swipeTrail.Clear();
            // The section shown in full (its page, its rail button), not just its list refreshed.
            if (!_libraryOpen) ShowLibraryPage(_libraryPage);
        }
        // Fingers moving right scroll left (a negative delta), and widen the sidebar.
        _swipeTo -= point.Properties.MouseWheelDelta * SwipePixelsPerDelta;
        SetSidebarWidth(Swipe.Follow(_swipeTo.Value));

        var now = _libraryClock.ElapsedMilliseconds;
        _swipeTrail.Add((now, _libraryWidth));
        _swipeTrail.RemoveAll(s => now - s.Ms > 100);

        if (_swipeEnd is null)
        {
            _swipeEnd = DispatcherQueue.CreateTimer();
            _swipeEnd.Interval = TimeSpan.FromMilliseconds(SwipeEndMs);
            _swipeEnd.IsRepeating = false;
            _swipeEnd.Tick += (_, _) => EndSwipe();
        }
        _swipeEnd.Stop();
        _swipeEnd.Start();
    }

    private void EndSwipe()
    {
        if (_swipeTo is null) return;
        _swipeTo = null;
        var (first, last) = (_swipeTrail.FirstOrDefault(), _swipeTrail.LastOrDefault());
        var velocity = last.Ms > first.Ms ? (last.Width - first.Width) / ((last.Ms - first.Ms) / 1000.0) : 0;
        var open = Swipe.Opens(_wheelFrom, _libraryWidth, velocity);
        if (!open && IsFocusIn(Library)) _active?.View.Focus(FocusState.Programmatic);
        var carry = Swipe.Carry(_libraryWidth, open ? LibraryWidth : ClosedWidth, velocity);
        MoveLibrary(open, carry == 0 ? 0.001 : carry);
    }

    // ───────────────────────── Sections ─────────────────────────

    private void ShowLibraryPage(string page)
    {
        var changed = page != _libraryPage;
        _libraryPage = page;
        LibraryDownloadsPage.Visibility = page == "downloads" ? Visibility.Visible : Visibility.Collapsed;
        LibraryHistoryPage.Visibility = page == "history" ? Visibility.Visible : Visibility.Collapsed;
        LibraryBookmarksPage.Visibility = page == "bookmarks" ? Visibility.Visible : Visibility.Collapsed;
        // The filter is History's only.
        LibraryFilterButton.Visibility = page == "history" ? Visibility.Visible : Visibility.Collapsed;
        if (page != "history") ShowFilterPanel(false, animate: false);
        LibrarySearch.PlaceholderText = page switch { "downloads" => "Search downloads", "bookmarks" => "Search bookmarks", _ => "Search history" };
        ShowRailItem(LibraryBookmarksTab, LibraryBookmarksGlyph, page == "bookmarks");
        ShowRailItem(LibraryDownloadsTab, LibraryDownloadsGlyph, page == "downloads");
        ShowRailItem(LibraryHistoryTab, LibraryHistoryGlyph, page == "history");
        // Switching section is instant, as in Zen; a search doesn't carry over.
        if (changed && LibrarySearch.Text.Length > 0) LibrarySearch.Text = "";
        else RefreshLibraryPage();
        if (page == "downloads" && _libraryOpen) Downloads.MarkSeen();
    }

    /// <summary>The section showing gets a fill, and its icon the accent colour.</summary>
    private static void ShowRailItem(Button item, FontIcon glyph, bool selected)
    {
        if (selected) item.Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
        else item.ClearValue(Control.BackgroundProperty);
        if (selected) glyph.Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];
        else glyph.ClearValue(FontIcon.ForegroundProperty);
    }

    private void RefreshLibraryPage()
    {
        if (_libraryPage == "downloads") RefreshDownloads();
        else if (_libraryPage == "bookmarks") ShowLibraryBookmarks();
        else ShowLibraryHistory();
    }

    /// <summary>The library as you left it, or at Downloads while there's a finished file you haven't seen, as in Arc.</summary>
    private void LibraryButton_Click(object sender, RoutedEventArgs e) => OpenLibrary(Downloads.HasUnseen ? "downloads" : _libraryPage);

    private void LibraryBack_Click(object sender, RoutedEventArgs e) => CloseLibrary();

    private void LibraryTab_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string page) ShowLibraryPage(page);
    }

    private void LibrarySearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        _libraryHistoryShown = LibraryHistoryPageSize;
        RefreshLibraryPage();
    }

    /// <summary>Esc clears the search, then closes the library.</summary>
    private void LibrarySearch_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        e.Handled = true;
        if (LibrarySearch.Text.Length > 0) LibrarySearch.Text = "";
        else CloseLibrary();
    }

    // ───────────────────────── Downloads ─────────────────────────

    /// <summary>The downloads section: files checked again (they may have moved), and the search applied.</summary>
    private void RefreshDownloads()
    {
        Downloads.ArchiveOld();
        foreach (var item in Downloads.Items) item.Refresh();
        var query = LibrarySearch.Text.Trim();
        DownloadList.ItemsSource = query.Length == 0
            ? DownloadItems
            : Downloads.Items.Where(i => i.FileName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        UpdateDownloadsPage();
    }

    private void UpdateDownloadsPage()
    {
        var searching = LibrarySearch.Text.Trim().Length > 0;
        var shown = DownloadList.ItemsSource is System.Collections.ICollection items ? items.Count : 0;
        DownloadsEmpty.Text = searching ? "No downloads match" : "Nothing downloaded yet";
        DownloadsEmpty.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
        DownloadList.Visibility = shown > 0 ? Visibility.Visible : Visibility.Collapsed;
        DownloadsClearButton.Visibility = Downloads.Items.Any(i => !i.IsRunning) ? Visibility.Visible : Visibility.Collapsed;
    }

    // ───────────────────────── History ─────────────────────────

    private const int LibraryHistoryPageSize = 150;
    private int _libraryHistoryShown = LibraryHistoryPageSize;
    private static readonly string GlobeGlyph = ((char)0xE774).ToString();

    /// <summary>The filter, as Zen has it (HistoryFilter, in Core): since when, sorted how, the pages visited or the tabs closed.</summary>
    private HistoryFilter _historyFilter;

    /// <summary>Goes up when the closed tabs change, so a list of them knows it's out of date.</summary>
    private int _closedTabsVersion;

    /// <summary>A line of the list: a page visited (Visit), or a tab closed (Closed). When is local time.</summary>
    private sealed record HistoryLine(string Url, string Title, DateTime When, int Visits, HistoryEntry? Visit, SessionTab? Closed);

    /// <summary>The pages matching the search and the filter (HistoryView, in Core).</summary>
    private HistoryListState? _historyListShown;

    private void ShowLibraryHistory()
    {
        var query = LibrarySearch.Text;
        var filter = _historyFilter;
        var closed = filter.Source == HistorySource.ClosedTabs;
        ShowHistoryFilter();
        var now = new HistoryListState(closed ? _closedTabsVersion : History.Version, query, _libraryHistoryShown, DateTime.Today,
            Root.ActualTheme == ElementTheme.Dark, filter);
        if (!HistoryListState.Rebuild(_historyListShown, now)) return;
        _historyListShown = now;
        // The closed tabs newest first, those from before closing times were kept too.
        var items = closed
            ? Enumerable.Reverse(_closedTabs).Select(t => new HistoryLine(t.Url, t.Title, t.Closed?.ToLocalTime() ?? DateTime.MinValue, 1, null, t))
            : History.All.Select(h => new HistoryLine(h.Url, h.Title ?? "", h.LastVisit.ToLocalTime(), h.Visits, h, null));
        var since = HistoryFilter.Since(filter.Range, DateTime.Now, CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek);
        var found = HistoryView.Find(items, i => i.Title, i => i.Url, i => i.When, query, _libraryHistoryShown, DateTime.Today,
            filter.Sort, since, i => i.Visits);

        var narrowed = query.Trim().Length > 0 || filter.Range != HistoryRange.All;
        LibraryHistoryEmpty.Visibility = found.Days.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LibraryHistoryEmpty.Text = closed
            ? narrowed ? "No closed tabs match" : "No closed tabs"
            : narrowed ? "No pages match" : "Nothing in your history yet";
        LibraryHistoryMore.Visibility = found.HasMore ? Visibility.Visible : Visibility.Collapsed;
        // Rows made for the other theme have its colours: new ones.
        if (_historyRows is null || _historyRows.Dark != now.Dark)
            LibraryHistoryList.ItemTemplate = _historyRows = new HistoryRows(this, now.Dark);
        LibraryHistoryList.ItemsSource = HistoryView.Lines(found);
    }

    // ── The filter ──

    /// <summary>The choices made filled, the Filter button too while it's not the default, and the search's hint.</summary>
    private void ShowHistoryFilter()
    {
        var f = _historyFilter;
        (Button Chip, bool On)[] chips =
        [
            (RangeToday, f.Range == HistoryRange.Today), (RangeWeek, f.Range == HistoryRange.Week), (RangeMonth, f.Range == HistoryRange.Month),
            (SortDate, f.Sort == HistorySort.Date), (SortSite, f.Sort == HistorySort.Site),
            (SortMostVisited, f.Sort == HistorySort.MostVisited), (SortLastVisited, f.Sort == HistorySort.LastVisited),
            (SourceHistory, f.Source == HistorySource.History), (SourceClosedTabs, f.Source == HistorySource.ClosedTabs),
        ];
        foreach (var (chip, on) in chips)
        {
            if (on) chip.Background = (Brush)Application.Current.Resources["DriftActiveBrush"];
            else chip.ClearValue(Control.BackgroundProperty);
        }
        if (f.IsDefault) LibraryFilterButton.ClearValue(Control.BackgroundProperty);
        else LibraryFilterButton.Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
        if (_libraryPage == "history")
            LibrarySearch.PlaceholderText = f.Source == HistorySource.ClosedTabs ? "Search closed tabs" : "Search history";
    }

    private void LibraryFilter_Click(object sender, RoutedEventArgs e) => ShowFilterPanel(true);

    private void LibraryFilterDone_Click(object sender, RoutedEventArgs e) => ShowFilterPanel(false);

    private bool _filterOpen;
    private int _filterMotion;

    // Measured from Zen: the list moves with CSS's ease over 300ms; the panel and the search cross-fade
    // (their opacity transitions, in MainWindow.xaml).
    private const int FilterSlideMs = 300;
    private const int FilterFadeMs = 150;

    /// <summary>
    /// The filter in the search's place, or the search back, as in Zen: the search and the panel's
    /// heading cross-fade; the panel fades in where it'll be as the list below slides down past it,
    /// over it; closing, the panel fades where it is as the list slides back up over it.
    /// </summary>
    private async void ShowFilterPanel(bool open, bool animate = true)
    {
        if (open == _filterOpen) return;
        _filterOpen = open;
        var motion = ++_filterMotion;
        animate &= new Windows.UI.ViewManagement.UISettings().AnimationsEnabled && _libraryOpen;
        var list = LibraryHistoryPage;
        var wasAt = list.ActualHeight > 0 ? list.TransformToVisual(Library).TransformPoint(default).Y : 0;

        LibrarySearchRow.IsHitTestVisible = !open;
        if (open)
        {
            LibraryFilterPanel.Margin = new Thickness(0);
            LibraryFilterPanel.Visibility = Visibility.Visible;
        }
        else
        {
            // Out of the layout at once (the list closes up over it), still drawn while it fades.
            LibraryFilterPanel.Margin = new Thickness(0, 0, 0, -LibraryFilterPanel.ActualHeight);
            LibrarySearchRow.Visibility = Visibility.Visible;
        }
        // Laid out first: one just shown doesn't fade in if its opacity is set in the same pass.
        Library.UpdateLayout();
        (open ? LibraryFilterPanel : (UIElement)LibrarySearchRow).Opacity = 1;
        (open ? LibrarySearchRow : (UIElement)LibraryFilterPanel).Opacity = 0;
        if (!animate)
        {
            FinishFilterPanel(open);
            return;
        }

        // The list drawn where it was, sliding to where it is now.
        var isAt = list.TransformToVisual(Library).TransformPoint(default).Y;
        if (list.Visibility == Visibility.Visible && Math.Abs(wasAt - isAt) > 0.5)
        {
            var compositor = CompositionTarget.GetCompositorForCurrentThread();
            var slide = compositor.CreateVector3KeyFrameAnimation();
            slide.Target = "Translation";
            slide.InsertKeyFrame(0, new System.Numerics.Vector3(0, (float)(wasAt - isAt), 0));
            slide.InsertKeyFrame(1, System.Numerics.Vector3.Zero,
                compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0.25f, 0.1f), new System.Numerics.Vector2(0.25f, 1)));
            slide.Duration = TimeSpan.FromMilliseconds(FilterSlideMs);
            list.StartAnimation(slide);
        }
        await System.Threading.Tasks.Task.Delay(FilterFadeMs + 30);
        if (motion == _filterMotion) FinishFilterPanel(open);
    }

    /// <summary>The faded one out of the way: the search, or the panel.</summary>
    private void FinishFilterPanel(bool open)
    {
        if (open) LibrarySearchRow.Visibility = Visibility.Collapsed;
        else
        {
            LibraryFilterPanel.Visibility = Visibility.Collapsed;
            LibraryFilterPanel.Margin = new Thickness(0);
        }
    }

    /// <summary>A choice: when (choosing it again is any time), how it's sorted, or what it looks through.</summary>
    private void LibraryFilterChip_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string tag || tag.Split(':') is not [var kind, var value]) return;
        var was = _historyFilter;
        var now = kind switch
        {
            "range" when Enum.Parse<HistoryRange>(value) is var range => was with { Range = range == was.Range ? HistoryRange.All : range },
            "sort" => was with { Sort = Enum.Parse<HistorySort>(value) },
            _ => was with { Source = Enum.Parse<HistorySource>(value) },
        };
        if (now == was) return;
        _historyFilter = now;
        _libraryHistoryShown = LibraryHistoryPageSize;
        ShowLibraryHistory();
    }

    /// <summary>A tab was closed, or one reopened: the list of closed tabs, if it's showing.</summary>
    private void ClosedTabsChanged()
    {
        _closedTabsVersion++;
        if (_libraryOpen && _libraryPage == "history" && _historyFilter.Source == HistorySource.ClosedTabs) ShowLibraryHistory();
    }

    // ── The rows ──

    private HistoryRows? _historyRows;

    /// <summary>
    /// Makes the list's rows, only as many as are on screen, and reuses them as it scrolls: building
    /// a row for every page took half a second.
    /// </summary>
    private sealed class HistoryRows(MainWindow window, bool dark) : IElementFactory
    {
        private readonly Stack<Grid> _rows = new();
        private readonly Stack<TextBlock> _headings = new();
        public bool Dark { get; } = dark;

        public UIElement GetElement(ElementFactoryGetArgs args)
        {
            var line = (HistoryView.Line<HistoryLine>)args.Data;
            if (line.Day is { } day)
            {
                var heading = _headings.Count > 0 ? _headings.Pop() : new TextBlock
                {
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = (Brush)Application.Current.Resources["DriftSecondaryTextBrush"],
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                heading.Text = day;
                heading.Margin = new Thickness(8, line.FirstDay ? 2 : 14, 0, 4);
                return heading;
            }
            var row = _rows.Count > 0 ? _rows.Pop() : window.NewHistoryRow();
            window.ShowInHistoryRow(row, line.Entry!);
            return row;
        }

        public void RecycleElement(ElementFactoryRecycleArgs args)
        {
            if (args.Element is TextBlock heading) _headings.Push(heading);
            else if (args.Element is Grid row) _rows.Push(row);
        }
    }

    /// <summary>A history row's parts, and the page it's showing now (rows are reused).</summary>
    private sealed class HistoryRowParts
    {
        public required FontIcon Glyph { get; init; }
        public required Image Icon { get; init; }
        public required TextBlock Title { get; init; }
        public required TextBlock Host { get; init; }
        public required StackPanel Text { get; init; }
        public required StackPanel Actions { get; init; }
        public required Button Remove { get; init; }
        public required Button Reopen { get; init; }
        public HistoryLine? Item { get; set; }
        public Uri? Uri { get; set; }
        public string HostText { get; set; } = "";
    }

    /// <summary>
    /// A page: icon, title and site. Click to open it (a closed tab comes back as it was). Hovered, as in
    /// Zen, the site gives way to when it was visited, and two buttons show: remove it, open it again.
    /// </summary>
    private Grid NewHistoryRow()
    {
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        var row = new Grid { Padding = new Thickness(8, 6, 6, 6), ColumnSpacing = 10, CornerRadius = new CornerRadius(8), Background = transparent };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var glyph = new FontIcon { Glyph = GlobeGlyph, FontSize = 14, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center };
        var icon = new Image { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(glyph);
        row.Children.Add(icon);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleText = new TextBlock { FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
        var hostText = new TextBlock
        {
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["DriftSecondaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        text.Children.Add(titleText);
        text.Children.Add(hostText);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        Button Action(string glyph) => new()
        {
            Style = (Style)Application.Current.Resources["IconButtonStyle"],
            Width = 28,
            Height = 28,
            Content = new FontIcon { Glyph = glyph, FontSize = 13 },
        };
        var remove = Action("");
        var reopen = Action("");
        // Over the text's end, and only while hovered: otherwise the title and site have the row's whole width.
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        actions.Children.Add(remove);
        actions.Children.Add(reopen);
        Grid.SetColumn(actions, 1);
        row.Children.Add(actions);

        var parts = new HistoryRowParts { Glyph = glyph, Icon = icon, Title = titleText, Host = hostText, Text = text, Actions = actions, Remove = remove, Reopen = reopen };
        row.Tag = parts;

        row.PointerEntered += (_, _) => ShowHistoryRowHover(row, parts, true);
        row.PointerExited += (_, _) => ShowHistoryRowHover(row, parts, false);
        row.Tapped += (_, e) =>
        {
            // Not a click on one of its buttons.
            for (var at = e.OriginalSource as DependencyObject; at is not null && at != row; at = VisualTreeHelper.GetParent(at))
                if (at == actions) return;
            if (parts.Item is { } item) ReopenHistoryItem(item);
        };
        remove.Click += (_, _) =>
        {
            if (parts.Item is { } item) RemoveHistoryItem(item);
        };
        reopen.Click += (_, _) =>
        {
            if (parts.Item is { } item) ReopenHistoryItem(item);
        };
        row.ContextRequested += (_, e) =>
        {
            e.Handled = true;
            _historyMenuItem = parts.Item;
            HistoryMenuRemove.Text = parts.Item?.Closed is not null ? "Remove from list" : "Remove from history";
            var options = new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions();
            if (e.TryGetPosition(row, out var at)) options.Position = at;
            HistoryMenu.ShowAt(row, options);
        };
        return row;
    }

    /// <summary>Hovered: the row lights up, its buttons show, and the site gives way to when it was visited.</summary>
    private static void ShowHistoryRowHover(Grid row, HistoryRowParts parts, bool over)
    {
        row.Background = over
            ? (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"]
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        parts.Actions.Visibility = over ? Visibility.Visible : Visibility.Collapsed;
        // The text makes way for the buttons only while they're there.
        parts.Text.Margin = new Thickness(0, 0, over ? HistoryActionsWidth : 0, 0);
        parts.Host.Text = over && parts.Item is { } item && item.When != DateTime.MinValue ? WhenText(item.When) : parts.HostText;
    }

    /// <summary>The two buttons and a little space before them.</summary>
    private const double HistoryActionsWidth = 28 * 2 + 2 + 6;

    /// <summary>As Zen shows it: 13 Nov 2026, 9:16 pm (in the format Windows is set to).</summary>
    private static string WhenText(DateTime when) =>
        $"{when.ToString("d MMM yyyy", CultureInfo.CurrentCulture)}, {when.ToString("t", CultureInfo.CurrentCulture)}";

    private void ShowInHistoryRow(Grid row, HistoryLine item)
    {
        var parts = (HistoryRowParts)row.Tag;
        var uri = Uri.TryCreate(item.Url, UriKind.Absolute, out var parsed) ? parsed : null;
        var title = string.IsNullOrWhiteSpace(item.Title) ? item.Url : item.Title;
        var host = HistoryView.Site(item.Url);
        parts.Item = item;
        parts.Uri = uri;
        parts.Title.Text = title;
        parts.HostText = host;
        var closed = item.Closed is not null;
        ToolTipService.SetToolTip(parts.Remove, closed ? "Remove from list" : "Remove from history");
        ToolTipService.SetToolTip(parts.Reopen, closed ? "Reopen tab" : "Reopen page");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(parts.Remove, closed ? "Remove from list" : "Remove from history");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(parts.Reopen, closed ? "Reopen tab" : "Reopen page");
        // A reused row may still show the last page's hover and icon.
        ShowHistoryRowHover(row, parts, false);
        parts.Icon.Source = null;
        parts.Glyph.Visibility = Visibility.Visible;
        if (uri is not null) LoadHistoryIcon(uri.Authority, parts, item);
        ToolTipService.SetToolTip(row, item.Url);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(row, $"{title}, {host}");
    }

    /// <summary>Opened again: a page in a new tab; a closed tab back as it was, in its place.</summary>
    private void ReopenHistoryItem(HistoryLine item)
    {
        if (item.Closed is { } closed) ReopenClosedTab(closed);
        else if (Uri.TryCreate(item.Url, UriKind.Absolute, out var uri)) OpenPage(uri);
    }

    /// <summary>Out of the list: a page forgotten, or a closed tab no longer to be reopened.</summary>
    private void RemoveHistoryItem(HistoryLine item)
    {
        if (item.Closed is { } closed)
        {
            if (!_closedTabs.Remove(closed)) return;
            SaveSessionSoon();
            ClosedTabsChanged();
            return;
        }
        History.Remove(item.Url);
        ShowLibraryHistory();
    }

    /// <summary>A row's right-click menu: one, shared, since one per row made the list slow to build.</summary>
    private MenuFlyout HistoryMenu => _historyMenu ??= CreateHistoryMenu();
    private MenuFlyout? _historyMenu;
    private HistoryLine? _historyMenuItem;
    private MenuFlyoutItem HistoryMenuRemove => _historyMenuRemove ??= new MenuFlyoutItem { Text = "Remove from history", Icon = new FontIcon { Glyph = "" } };
    private MenuFlyoutItem? _historyMenuRemove;

    private MenuFlyout CreateHistoryMenu()
    {
        HistoryMenuRemove.Click += (_, _) =>
        {
            if (_historyMenuItem is { } item) RemoveHistoryItem(item);
        };
        var menu = new MenuFlyout();
        menu.Items.Add(HistoryMenuRemove);
        return menu;
    }

    private static async void LoadHistoryIcon(string site, HistoryRowParts parts, HistoryLine item)
    {
        if (await History.IconAsync(site) is not { } icon) return;
        // The row may have moved on to another page meanwhile.
        if (!ReferenceEquals(parts.Item, item)) return;
        parts.Icon.Source = icon;
        parts.Glyph.Visibility = Visibility.Collapsed;
    }

    private void LibraryHistoryMore_Click(object sender, RoutedEventArgs e)
    {
        _libraryHistoryShown += LibraryHistoryPageSize;
        ShowLibraryHistory();
    }

    private void LibraryClearData_Click(object sender, RoutedEventArgs e) => SettingsWindow.Open(this, "privacy");

    /// <summary>History was cleared elsewhere (Settings): the list, if it's showing.</summary>
    internal void HistoryChanged()
    {
        if (_libraryOpen && _libraryPage == "history") ShowLibraryHistory();
    }
}
