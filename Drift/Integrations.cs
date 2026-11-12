using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Drift.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;

namespace Drift;

/// <summary>
/// What the site integrations share (GoogleCalendar.cs, Gmail.cs): a hidden view on each site, signed
/// in as your tabs are, for asking it for your data; and the card that hovering a pinned tab for one
/// of them shows, hung from the tab.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// A hidden view, its engine object held here for as long as the view lives: WebView2 calls back
    /// into the handler subscribed on it, so neither may be let go while it's open.
    /// </summary>
    private sealed class HiddenSite(CoreWebView2Controller controller, CoreWebView2 core)
    {
        public CoreWebView2Controller Controller { get; } = controller;
        public CoreWebView2 Core { get; } = core;

        /// <summary>The page being waited for, done when it's loaded (or the next one's asked for).</summary>
        public TaskCompletionSource<bool>? Loading { get; set; }
    }

    private readonly Dictionary<string, HiddenSite> _hiddenSites = new(StringComparer.OrdinalIgnoreCase);
    private DispatcherQueueTimer? _hoverShowTimer;
    private DispatcherQueueTimer? _hoverHideTimer;
    private DispatcherQueueTimer? _hoverGoneTimer;
    private FrameworkElement? _hoverAnchor;
    private BrowserTab? _hoverTab;
    private Border? _hoverCard;
    private Border? _hoverWanted;

    // ---- Hidden views ----

    /// <summary>
    /// The hidden view on <paramref name="start"/>'s site, started (at <paramref name="start"/>) the first time,
    /// <paramref name="setUp"/> first having its say.
    /// </summary>
    private async Task<CoreWebView2> HiddenSiteAsync(Uri start, Func<CoreWebView2, Task>? setUp = null)
    {
        if (_hiddenSites.TryGetValue(start.Host, out var open)) return open.Core;
        var environment = await EnvironmentAsync();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var controller = await environment.CreateCoreWebView2ControllerAsync(CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)hwnd));
        controller.IsVisible = false;
        controller.Bounds = new Windows.Foundation.Rect(0, 0, 0, 0);
        var core = controller.CoreWebView2;
        var site = new HiddenSite(controller, core);
        core.IsMuted = true;
        // A site asking for a password (Gmail's feed, signed out) gets no answer rather than waiting for one.
        core.BasicAuthenticationRequested += (_, e) => e.Cancel = true;
        core.NewWindowRequested += (_, e) => e.Handled = true;
        // Subscribed once, for the view's life. Taking a handler off while WebView2 is calling its
        // handlers left it calling one already freed, which crashed Drift.
        core.NavigationCompleted += (_, _) => site.Loading?.TrySetResult(true);
        // Its engine gone: opened afresh next time.
        core.ProcessFailed += (_, e) =>
        {
            if (e.ProcessFailedKind != CoreWebView2ProcessFailedKind.BrowserProcessExited &&
                e.ProcessFailedKind != CoreWebView2ProcessFailedKind.RenderProcessExited &&
                e.ProcessFailedKind != CoreWebView2ProcessFailedKind.RenderProcessUnresponsive) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_hiddenSites.TryGetValue(start.Host, out var now) && now == site) CloseHiddenSite(start.Host);
            });
        };
        if (setUp is not null) await setUp(core);
        await GoToAsync(site, start, TimeSpan.FromSeconds(20));
        // Opened twice at once: the newer one kept.
        CloseHiddenSite(start.Host);
        _hiddenSites[start.Host] = site;
        return core;
    }

    /// <summary>Goes to <paramref name="uri"/> in a hidden view and waits for it, for a while.</summary>
    private Task GoToAsync(CoreWebView2 core, Uri uri, TimeSpan time)
    {
        foreach (var site in _hiddenSites.Values)
            if (ReferenceEquals(site.Core, core)) return GoToAsync(site, uri, time);
        return Task.CompletedTask; // Closed meanwhile.
    }

    private static async Task GoToAsync(HiddenSite site, Uri uri, TimeSpan time)
    {
        var loaded = new TaskCompletionSource<bool>();
        site.Loading?.TrySetResult(false);
        site.Loading = loaded;
        try { site.Core.Navigate(uri.ToString()); }
        catch { return; } // Closed meanwhile.
        await Deadline.Within(loaded.Task, time, fallback: false);
        if (site.Loading == loaded) site.Loading = null;
    }

    private void CloseHiddenSite(string host)
    {
        if (!_hiddenSites.Remove(host, out var site)) return;
        site.Loading?.TrySetResult(false);
        // Closed once whatever WebView2 is in the middle of has finished, never from inside it.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            try { site.Controller.Close(); }
            catch { /* Already gone with its engine. */ }
            GC.KeepAlive(site.Core);
        });
    }

    private void CloseHiddenSites()
    {
        foreach (var host in new List<string>(_hiddenSites.Keys)) CloseHiddenSite(host);
    }

    /// <summary>A pinned tab, in front, at <paramref name="uri"/>.</summary>
    private void OpenInTab(BrowserTab? tab, Uri uri)
    {
        HideHoverCard();
        if (tab is null) return;
        if (tab.View.CoreWebView2 is null)
        {
            tab.PendingUri = uri;
            Activate(tab);
        }
        else
        {
            Activate(tab);
            Navigate(tab, uri);
        }
    }

    // ---- The hover card ----

    /// <summary>The card hovering this tab shows, if it's one an integration is following.</summary>
    private Border? HoverCardFor(BrowserTab tab) =>
        tab == _calendarTab || tab == _outlookCalendarTab ? CalendarCard
        : tab == _mailTab || tab == _outlookMailTab ? MailCard
        : null;

    private void IntegrationAnchor_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: BrowserTab tab } anchor || HoverCardFor(tab) is not { } card) return;
        _hoverHideTimer?.Stop();
        _hoverAnchor = anchor;
        if (_hoverCard == card && _hoverTab == tab) { PlaceHoverCard(card); return; }
        _hoverTab = tab;
        // From one integration's tab straight to another's: its card, at once.
        var switching = _hoverCard is not null;
        HideHoverCard();
        _hoverWanted = card;
        if (switching) { ShowHoverCard(); return; }
        _hoverShowTimer ??= OneShot(TimeSpan.FromMilliseconds(350), ShowHoverCard);
        _hoverShowTimer.Start();
    }

    private void IntegrationAnchor_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _hoverShowTimer?.Stop();
        if (_hoverCard is not null) HideHoverCardSoon();
    }

    private void HoverCard_PointerEntered(object sender, PointerRoutedEventArgs e) => _hoverHideTimer?.Stop();

    private void HoverCard_PointerExited(object sender, PointerRoutedEventArgs e) => HideHoverCardSoon();

    private void HideHoverCardSoon()
    {
        _hoverHideTimer ??= OneShot(TimeSpan.FromMilliseconds(250), () => HideHoverCard(animate: true));
        _hoverHideTimer.Start();
    }

    private DispatcherQueueTimer OneShot(TimeSpan delay, Action action)
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = delay;
        timer.IsRepeating = false;
        timer.Tick += (_, _) => Safely(action);
        return timer;
    }

    private void ShowHoverCard()
    {
        if (_hoverAnchor is null || _hoverWanted is not { } card) return;
        _hoverCard = card;
        if (card == CalendarCard) ShowingCalendarCard();
        else if (card == MailCard) ShowingMailCard();
        PlaceHoverCard(card);
        card.Scale = new System.Numerics.Vector3(0.96f, 0.96f, 1);
        card.IsHitTestVisible = true;
        card.Visibility = Visibility.Visible;
        card.Opacity = 1;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => card.Scale = System.Numerics.Vector3.One);
    }

    /// <param name="animate">
    /// Shrinks back into the tab's corner as it fades, the way it grew out of it (the card's own
    /// scale and opacity transitions), then goes. Otherwise (another card replacing it) at once.
    /// </param>
    private void HideHoverCard(bool animate = false)
    {
        _hoverShowTimer?.Stop();
        _hoverHideTimer?.Stop();
        if (_hoverCard is not { } card) return;
        _hoverCard = null;
        card.Opacity = 0;
        if (!animate)
        {
            card.Visibility = Visibility.Collapsed;
            return;
        }
        card.IsHitTestVisible = false;
        card.Scale = new System.Numerics.Vector3(0.96f, 0.96f, 1);
        _hoverGoneTimer ??= OneShot(TimeSpan.FromMilliseconds(160), () =>
        {
            foreach (var gone in new[] { CalendarCard, MailCard })
                if (gone != _hoverCard) gone.Visibility = Visibility.Collapsed;
        });
        _hoverGoneTimer.Start();
    }

    /// <summary>
    /// Hung from the tab, as in Arc: from an Essentials tile's bottom-right corner, or beside a row,
    /// level with it; kept inside the window. It grows out of that corner.
    /// </summary>
    private void PlaceHoverCard(Border card)
    {
        if (_hoverAnchor is null) return;
        var bounds = _hoverAnchor.TransformToVisual(Root).TransformBounds(new Windows.Foundation.Rect(0, 0, _hoverAnchor.ActualWidth, _hoverAnchor.ActualHeight));
        card.UpdateLayout();
        var height = card.ActualHeight > 0 ? card.ActualHeight : 380;
        var tile = _hoverAnchor.DataContext is BrowserTab { Place: TabPlace.Essential };
        var left = tile ? bounds.Right - 6 : bounds.Right + 6;
        var wanted = tile ? bounds.Bottom - 6 : bounds.Top - 8;
        var top = Math.Clamp(wanted, 8, Math.Max(8, Root.ActualHeight - height - 8));
        left = Math.Min(left, Math.Max(8, Root.ActualWidth - card.Width - 8));
        card.Margin = new Thickness(left, top, 0, 0);
        // Grows from the tab's corner: where that is on the card, if pushed up to fit.
        card.CenterPoint = new System.Numerics.Vector3(0, (float)Math.Clamp(wanted - top, 0, height), 0);
    }

    private static bool IsInsideButton(DependencyObject element)
    {
        for (var e = element; e is not null; e = VisualTreeHelper.GetParent(e))
            if (e is Button) return true;
        return false;
    }
}
