using System;
using System.Threading.Tasks;
using System.Globalization;
using System.Linq;
using Drift.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;

namespace Drift;

/// <summary>
/// Two-finger swipes on a touchpad: back and forward (Core/SwipeNav.cs). WinUI's WebView2 hands them to the page as
/// sideways scrolling, never as Chromium's own swipe, so each page reports them (SwipeScript): "swipe:dx" as the
/// fingers move, in screen pixels, and "swipe:end" when they stop ("swipe:cancel" if the page took them after all).
/// The arrow comes in from the edge of the page's card (SwipeArrowView); or, with Settings › Tabs' "Slide the page",
/// the page itself moves aside over a picture of the one it's going to, as Safari's does (PageCardView, SwipePage.cs).
/// </summary>
public sealed partial class MainWindow
{
    private SwipeNav? _swipe;
    private WebView2? _swipeView;
    private SwipeArrowView? _swipeArrow;
    private PageCardView? _swipeCard;    // the card whose page the swipe moves, when it moves the page (SwipePage.cs)
    private int _swipeCount;             // so a page settling after one swipe leaves the next one be
    private bool _swipeNowhere;          // this swipe's way has nowhere to go (no page back, or forward)
    private BrowserTab? _swipeTab;       // the tab swiped on (null in Peek)
    private BrowserTab? _swipeCloses;    // swiping back closes the tab, back to this one, the tab whose link opened it
    private DispatcherQueueTimer? _swipeLost;

    // A run of wheel events, until they stop for as long as the Library's swipe waits (SwipeEndMs), is a swipe if it
    // starts sideways, without Ctrl (zoom) or Shift (a mouse wheel scrolling sideways), and nothing under it would
    // take it: an element that could scroll that way, one with overscroll-behavior (as pages say "no swipes here"),
    // or a page that handles the wheel itself. Its movement goes once a frame.
    private static string SwipeScript() => $$"""
          {
            let run = 0, ax = 0, ay = 0, owed = 0, frame = 0, pause = 0;
            const keeps = (e, dx) => {
              const top = document.scrollingElement || document.documentElement;
              const path = e.composedPath().filter((n) => n instanceof Element);
              if (!path.includes(top)) path.push(top);
              for (const el of path) {
                const s = getComputedStyle(el);
                const isTop = el === top;
                if (!isTop && s.overflowX !== 'auto' && s.overflowX !== 'scroll') continue;
                if (s.overscrollBehaviorX !== 'auto') return true;
                const max = el.scrollWidth - el.clientWidth;
                if (max <= 1 || (isTop && (s.overflowX === 'hidden' || s.overflowX === 'clip'))) continue;
                const rtl = s.direction === 'rtl';
                if (dx < 0 ? el.scrollLeft > (rtl ? -max : 0) + 1 : el.scrollLeft < (rtl ? 0 : max) - 1) return true;
              }
              return false;
            };
            const flush = () => { frame = 0; if (owed) { post(token + ':swipe:' + owed); owed = 0; } };
            const finish = (how, next) => {
              if (run === 1) { if (frame) cancelAnimationFrame(frame); flush(); post(token + ':swipe:' + how); }
              run = next; ax = ay = owed = 0;
            };
            window.addEventListener('wheel', (e) => {
              if (!e.isTrusted || e.ctrlKey || e.shiftKey || e.altKey || e.metaKey) return;
              clearTimeout(pause);
              pause = setTimeout(() => finish('end', 0), {{SwipeEndMs}});
              if (run === -1) return;
              const unit = (e.deltaMode === 1 ? 16 : e.deltaMode === 2 ? innerWidth : 1) * devicePixelRatio;
              const dx = e.deltaX * unit;
              if (run === 0) {
                ax += dx; ay += e.deltaY * unit;
                if (Math.abs(ax) + Math.abs(ay) < 8) return;
                if (Math.abs(ax) <= Math.abs(ay) || e.defaultPrevented || keeps(e, ax)) { run = -1; return; }
                run = 1; owed = ax;
              } else owed += dx;
              setTimeout(() => { if (run === 1 && e.defaultPrevented) finish('cancel', -1); }, 0);
              if (!frame) frame = requestAnimationFrame(flush);
            }, { passive: true });
          }
        """;

    private void OnSwipeMessage(BrowserTab? source, string what)
    {
        // Peek's page reports with no tab.
        var view = source?.View ?? _peekView;
        if (view?.CoreWebView2 is null) return;
        if (what is "end" or "cancel")
        {
            if (view == _swipeView) EndSwipe(letGo: what == "end");
            return;
        }
        if (!double.TryParse(what, NumberStyles.Float, CultureInfo.InvariantCulture, out var pixels) || pixels == 0) return;

        if (_swipe is null || _swipeView != view)
        {
            EndSwipe(letGo: false);
            _swipeView = view;
            StartSwipe(source, view, way: pixels < 0 ? -1 : 1);
        }
        // In case "end" never comes (the page went away mid-swipe).
        if (_swipeLost is null)
        {
            _swipeLost = DispatcherQueue.CreateTimer();
            _swipeLost.Interval = TimeSpan.FromMilliseconds(600);
            _swipeLost.IsRepeating = false;
            _swipeLost.Tick += (_, _) => EndSwipe(letGo: false);
        }
        _swipeLost.Start();

        if (_swipeNowhere || _swipe is null) return;
        var ms = _libraryClock.Elapsed.TotalMilliseconds;
        _swipe.Move(pixels / (view.XamlRoot?.RasterizationScale ?? 1), ms);
        SwipeLog($"page swipe {pixels:0.#} moved {_swipe.Progress:0.###} speed {_swipe.Speed:0}{(_swipe.MovesPage ? " page" : "")}");
        if (_swipe.Direction == 0) return;
        var closes = _swipe.Direction < 0 && !view.CanGoBack && _swipeCloses is not null;
        if (!(_swipe.Direction < 0 ? view.CanGoBack || closes : view.CanGoForward))
        {
            _swipeNowhere = true;
            return;
        }
        if (_swipeCard is { } card) card.FollowSwipe(_swipe.PageOffset);
        else _swipeArrow?.Follow(_swipe.Direction, _swipe.Progress, _swipe.GoesThrough, closes);
    }

    /// <summary>
    /// A swipe's first movement: it moves the page if that's the setting and there's a picture of the page it's going
    /// to (only pages left while Drift was running have one); otherwise it brings in the arrow.
    /// </summary>
    private void StartSwipe(BrowserTab? tab, WebView2 view, int way)
    {
        _swipeTab = tab;
        _swipeCloses = way < 0 && !view.CanGoBack ? OpenerToSwipeBackTo(tab) : null;
        var card = CardOf(view);
        var canGo = way < 0 ? view.CanGoBack : view.CanGoForward;
        var pictures = tab is null ? null : PicturesOf(tab);
        var bytes = pictures?.Of(way < 0 ? pictures.Back : pictures.Forward);
        if (Settings.Current.SwipeMovesPage && canGo && bytes is not null && card is { PageWidth: > 0 })
        {
            _swipeCount++;
            _swipe = SwipeNav.ForPage(card.PageWidth);
            _swipeCard = card;
            card.BeginSwipe(way, null);
            ShowSwipePicture(card, bytes, _swipeCount);
            return;
        }
        _swipe = new SwipeNav(ScreenLongSide());
        _swipeArrow = card?.Swipe;
    }

    /// <summary>
    /// With Settings › Tabs' "Swipe back closes a tab a link opened": the tab whose link opened <paramref name="tab"/>, if
    /// it's still open, for a back swipe from its first page to close it and go back to.
    /// </summary>
    private BrowserTab? OpenerToSwipeBackTo(BrowserTab? tab)
    {
        if (!Settings.Current.SwipeBackClosesOpened || tab is not { Place: TabPlace.Regular }) return null;
        if (tab.OpenedFrom is not { } weak || !weak.TryGetTarget(out var from)) return null;
        return AllTabs.Contains(from) ? from : null;
    }

    /// <summary>Closes a tab a link opened, and the tab whose link it was comes back (not if you keep its page's changes).</summary>
    private async void CloseToOpener(BrowserTab tab, BrowserTab opener)
    {
        if (!AllTabs.Contains(tab) || !await MayLeaveAsync([tab])) return;
        // First, so closing it doesn't bring its neighbour forward on the way.
        if (AllTabs.Contains(opener)) Activate(opener);
        RemoveTab(tab);
    }

    /// <summary>The picture, once it's ready to draw (a frame or two), if it's still the same swipe.</summary>
    private async void ShowSwipePicture(PageCardView card, byte[] bytes, int swipe)
    {
        if (await DrawableAsync(bytes) is { } picture && swipe == _swipeCount) card.SwipePictureReady(picture);
    }

    /// <summary>
    /// Let go far enough or flicked, the page goes back or forward; otherwise the arrow springs back out, or the page
    /// back into place.
    /// </summary>
    private void EndSwipe(bool letGo)
    {
        _swipeLost?.Stop();
        var (swipe, view, arrow, card) = (_swipe, _swipeView, _swipeArrow, _swipeCard);
        var nowhere = _swipeNowhere;
        var (tab, closesTo) = (_swipeTab, _swipeCloses);
        _swipeTab = _swipeCloses = null;
        _swipe = null;
        _swipeView = null;
        _swipeArrow = null;
        _swipeCard = null;
        _swipeNowhere = false;
        if (swipe is null) return;
        var through = letGo && !nowhere && swipe.GoesThrough && view is not null;
        SwipeLog($"page swipe let go {letGo} at {swipe.Progress:0.###} speed {swipe.Speed:0}: {(through ? "through" : "back")}");
        FlushSwipeLog();
        if (through)
        {
            // At once, as WebKit does, while the page is still moving.
            if (swipe.Direction < 0 && view!.CanGoBack) view.GoBack();
            else if (swipe.Direction < 0 && tab is not null && closesTo is not null) CloseToOpener(tab, closesTo);
            else if (swipe.Direction > 0 && view!.CanGoForward) view.GoForward();
        }
        if (card is not null)
        {
            card.LetGoSwipe(through, swipe.Speed);
            SettleSwipe(card, through ? view : null, _swipeCount);
        }
        else if (through) arrow?.GoThrough();
        else arrow?.Retract(swipe.ArrowSpeed);
    }

    /// <summary>
    /// Once the page has got where it's going, and, if it went through, the page it went to has drawn (WebKit waits
    /// for that too, at most 3s), the picture goes and the page's back in place.
    /// </summary>
    private async void SettleSwipe(PageCardView card, WebView2? wentFrom, int swipe)
    {
        var moving = Task.Delay(TimeSpan.FromMilliseconds(350));
        if (wentFrom?.CoreWebView2 is { } core)
        {
            var arrived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Completed(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs e) => arrived.TrySetResult(true);
            // A page of the same document (sites that change page without loading one) has no load to wait for.
            void Changed(CoreWebView2 sender, CoreWebView2SourceChangedEventArgs e)
            {
                if (!e.IsNewDocument) arrived.TrySetResult(true);
            }
            core.NavigationCompleted += Completed;
            core.SourceChanged += Changed;
            await Deadline.Within(arrived.Task, TimeSpan.FromSeconds(3), fallback: false);
            // After the wait, not from inside the engine's call.
            core.NavigationCompleted -= Completed;
            core.SourceChanged -= Changed;
            // Its first frame drawn.
            await Deadline.Within(EvaluateAsync(core, "new Promise((done) => requestAnimationFrame(() => requestAnimationFrame(() => done(1))))", gesture: false),
                TimeSpan.FromMilliseconds(400), fallback: null);
        }
        await moving;
        // Unless another swipe has started on it since.
        if (swipe == _swipeCount) card.EndSwipe();
    }

    /// <summary>The longer side of the screen the window is on, in DIPs: a swipe goes through at a share of it.</summary>
    private double ScreenLongSide()
    {
        var bounds = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).OuterBounds;
        var scale = Content.XamlRoot?.RasterizationScale ?? 1;
        return Math.Max(bounds.Width, bounds.Height) / scale;
    }

    private static PageCardView? CardOf(DependencyObject element)
    {
        for (var at = VisualTreeHelper.GetParent(element); at is not null; at = VisualTreeHelper.GetParent(at))
            if (at is PageCardView card) return card;
        return null;
    }
}
