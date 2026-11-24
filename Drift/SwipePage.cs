using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using Drift.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace Drift;

/// <summary>
/// Pictures of pages (Core/HistoryPictures.cs). Each page a tab leaves is pictured as it goes, by its place in the
/// tab's history, so a two-finger swipe can move the page aside over the one it's going to, as Safari does
/// (Settings › Tabs; the page's card draws it, PageCardView). And a tab's page as it last looked shows, faded, while
/// the tab loads it again after being unloaded. Kept in memory only, while Drift runs.
/// </summary>
public sealed partial class MainWindow
{
    private readonly ConditionalWeakTable<BrowserTab, HistoryPictures> _historyPictures = new();

    private HistoryPictures PicturesOf(BrowserTab tab) => _historyPictures.GetValue(tab, _ => new HistoryPictures());

    /// <summary>A tab's engine: its pages pictured as they're left, and its place in its history kept up.</summary>
    private void WireHistoryPictures(BrowserTab tab, CoreWebView2 core)
    {
        var pictures = PicturesOf(tab);
        pictures.ForgetPages();
        core.NavigationStarting += (sender, e) =>
        {
            // Cancelled (a link Peek took), or a redirect of one already under way: nothing's left yet.
            if (e.Cancel || e.IsRedirected || tab.View.CoreWebView2 != core) return;
            if (pictures.Current is { } leaving && tab.View.Visibility == Visibility.Visible)
                _ = PictureLeavingAsync(core, pictures, leaving);
        };
        core.NavigationCompleted += (_, _) => _ = ReadHistoryAsync(core, pictures);
        core.HistoryChanged += (_, _) => _ = ReadHistoryAsync(core, pictures);
    }

    private static async Task ReadHistoryAsync(CoreWebView2 core, HistoryPictures pictures)
    {
        try
        {
            pictures.Read(await core.CallDevToolsProtocolMethodAsync("Page.getNavigationHistory", "{}"));
        }
        catch
        {
            // The engine's going: it has no history to read.
        }
    }

    /// <summary>The page as it's leaving (still drawn until the next one comes), for the swipe back to it.</summary>
    private static async Task PictureLeavingAsync(CoreWebView2 core, HistoryPictures pictures, int leaving)
    {
        if (await PictureOfAsync(core) is { } bytes) pictures.Keep(leaving, bytes);
    }

    /// <summary>The page as it's drawn now, full size. Through DevTools, which doesn't blank the page.</summary>
    private static async Task<byte[]?> PictureOfAsync(CoreWebView2 core)
    {
        try
        {
            var asked = core.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", """{"format":"jpeg","quality":80}""").AsTask();
            // A page that isn't drawing never answers.
            var reply = await Deadline.Within(asked, TimeSpan.FromSeconds(2), fallback: null);
            if (reply is null) return null;
            using var document = JsonDocument.Parse(reply);
            return Convert.FromBase64String(document.RootElement.GetProperty("data").GetString()!);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>A picture's bytes made ready to draw, at full size.</summary>
    private static async Task<BitmapImage?> DrawableAsync(byte[] bytes)
    {
        try
        {
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            using (var writer = new Windows.Storage.Streams.DataWriter(stream))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            var picture = new BitmapImage();
            await picture.SetSourceAsync(stream);
            return picture;
        }
        catch
        {
            return null;
        }
    }

    // ───────────────────────── An unloaded tab's last look ─────────────────────────

    /// <summary>
    /// The shown pages that are loading again after being unloaded: each shows its page as it last looked, faded,
    /// under the page until it's drawn (RevealWhenLoaded fades the page in over it). Only if that's the page it's
    /// loading: a pinned tab unloaded goes back to its own page.
    /// </summary>
    private async void ShowLastLooks()
    {
        foreach (var pane in _panes) if (!pane.Card.Swiping) pane.Card.HideLastLook();
        // Now, before they start loading.
        var waiting = _shownPages
            .Where(p => p.View.Opacity <= 0 && p.IsUnloaded)
            .Select(p => (Page: p, Bytes: PicturesOf(p).LastLookAt(p.PendingUri?.AbsoluteUri)))
            .Where(w => w.Bytes is not null)
            .ToList();
        foreach (var (page, bytes) in waiting)
        {
            var view = page.View;
            if (await DrawableAsync(bytes!) is not { } picture) continue;
            // Since: shown already, or another tab in front.
            if (view.Opacity > 0 || !_shownPages.Contains(page) || CardOf(view) is not { } card) continue;
            card.ShowLastLook(picture);
            long watch = 0;
            watch = view.RegisterPropertyChangedCallback(UIElement.OpacityProperty, (_, _) =>
            {
                if (view.Opacity <= 0) return;
                view.UnregisterPropertyChangedCallback(UIElement.OpacityProperty, watch);
                // The page fades in over it (150ms), then it goes.
                card.HideLastLookSoon();
            });
        }
    }
}
