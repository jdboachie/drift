using System;
using System.Collections.Generic;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics;

namespace Drift;

/// <summary>
/// A page's own popup: window.open with a size or without browser bars, like "Sign in with Google".
/// It has to stay a real popup, still linked to the page that opened it: sign-in flows hand their
/// result back through window.opener and then close themselves. Opening it in Peek or a tab instead
/// would cut that link, and the sign-in would never finish.
/// </summary>
internal sealed class PopupWindow : Window
{
    // Popups close with the main window rather than keeping the app running on their own.
    private static readonly List<PopupWindow> Open = new();

    private readonly WebView2 _view = new();

    private PopupWindow()
    {
        Content = _view;
        SystemBackdrop = new MicaBackdrop();
        // Follow light/dark mode in the title bar too; by default it stays light.
        AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;
        Closed += (_, _) =>
        {
            Open.Remove(this);
            _view.Close();
        };
    }

    /// <summary>
    /// Chromium's rule for window.open: any window features (a size, a position, or no toolbar,
    /// menu bar or status bar) make a popup; none make a tab.
    /// </summary>
    public static bool IsPopup(CoreWebView2NewWindowRequestedEventArgs e)
    {
        var features = e.WindowFeatures;
        return features.HasSize || features.HasPosition
            || !features.ShouldDisplayToolbar || !features.ShouldDisplayMenuBar || !features.ShouldDisplayStatus;
    }

    /// <param name="owner">The window it opens over, centred.</param>
    /// <param name="openTab">Where links from the popup that ask for an ordinary new window go.</param>
    /// <param name="isPrivate">Opened from a private window: it has to share that window's profile.</param>
    public static async void Show(CoreWebView2NewWindowRequestedEventArgs e, Window owner, Action<Uri> openTab, bool isPrivate)
    {
        // The page waits for the new window until the deferral completes.
        var deferral = e.GetDeferral();
        var popup = new PopupWindow
        {
            // Until the page has a title of its own, the site it's opening (rather than WinUI's "WinUI Desktop").
            Title = Uri.TryCreate(e.Uri, UriKind.Absolute, out var opening) && !string.IsNullOrEmpty(opening.Host) ? opening.Host : "Drift",
        };
        Open.Add(popup);
        popup.Place(e.WindowFeatures, owner);
        popup.Activate();

        try
        {
            // The opener's environment: required for the handover below, and it brings the same overlay scroll bars.
            var environment = await MainWindow.EnvironmentAsync();
            if (isPrivate) await popup._view.EnsureCoreWebView2Async(environment, MainWindow.PrivateOptions(environment));
            else await popup._view.EnsureCoreWebView2Async(environment);
            var core = popup._view.CoreWebView2;
            core.DownloadStarting += (_, d) => Downloads.Track(d, isPrivate);
            // Nothing to come back to if its page dies: the sign-in can be started again.
            core.ProcessFailed += (_, f) =>
            {
                if (f.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.BrowserProcessExited)
                    popup.Close();
            };
            core.DocumentTitleChanged += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(core.DocumentTitle)) popup.Title = core.DocumentTitle;
            };
            // window.close() from the page, which sign-in popups do when they're done.
            core.WindowCloseRequested += (_, _) => popup.Close();
            core.NewWindowRequested += (_, inner) =>
            {
                if (IsPopup(inner))
                {
                    Show(inner, popup, openTab, isPrivate);
                    return;
                }
                inner.Handled = true;
                if (Uri.TryCreate(inner.Uri, UriKind.Absolute, out var target)) openTab(target);
            };

            // Handing over the new CoreWebView2 keeps the opener link; the page loads into it.
            e.NewWindow = core;
            e.Handled = true;
        }
        catch
        {
            popup.Close();
        }
        finally
        {
            deferral.Complete();
        }
    }

    public static void CloseAll()
    {
        foreach (var popup in Open.ToArray()) popup.Close();
    }

    /// <summary>The size the page asked for (500×600 if none), centred over the owner window.</summary>
    private void Place(CoreWebView2WindowFeatures features, Window owner)
    {
        var scale = owner.Content?.XamlRoot?.RasterizationScale ?? 1;
        var width = features.HasSize && features.Width > 0 ? features.Width : 500;
        var height = features.HasSize && features.Height > 0 ? features.Height : 600;
        AppWindow.ResizeClient(new SizeInt32((int)Math.Round(width * scale), (int)Math.Round(height * scale)));

        var ownerPos = owner.AppWindow.Position;
        var ownerSize = owner.AppWindow.Size;
        var size = AppWindow.Size;
        AppWindow.Move(new PointInt32(
            ownerPos.X + (ownerSize.Width - size.Width) / 2,
            ownerPos.Y + (ownerSize.Height - size.Height) / 2));
    }
}
