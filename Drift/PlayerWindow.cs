using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics;

namespace Drift;

/// <summary>
/// The window a page floating itself lives in (a meeting's call, Pip.cs). The page asks for a
/// Document Picture-in-Picture window, which WebView2 turns into an ordinary new-window request
/// (left to itself, it opens a full browser window with an address bar); Drift hands it this
/// instead: small, always on top, out of Alt+Tab, in the bottom-right corner the first time and
/// wherever it was left after that (PlayerPlacement). It opens without taking focus from Drift.
/// It's dragged by its top strip and resized from its edges; the page draws everything inside.
/// Videos float in Chromium's own picture-in-picture window, not this.
/// </summary>
internal sealed class PlayerWindow : Window
{
    private static readonly List<PlayerWindow> Open = new();
    private static Box? _lastPlace;

    private const int DragStripHeight = 40;   // the top strip, where the site's name is
    private const int ButtonsWidth = 72;      // Back to tab and Close, at its right end

    private readonly WebView2 _view = new() { DefaultBackgroundColor = Colors.Black };

    private PlayerWindow()
    {
        Title = "Picture in picture";
        Content = new Grid { Background = new SolidColorBrush(Colors.Black), Children = { _view } };
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
        AppWindow.IsShownInSwitchers = false;
        Taskbar.Hide(Win32Interop.GetWindowFromWindowId(AppWindow.Id));
        AppWindow.Changed += (_, change) =>
        {
            if (change.DidSizeChange) SetDragStrip();
        };
        RoundCorners();
        Closed += (_, _) =>
        {
            _lastPlace = new Box(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
            Open.Remove(this);
            _view.Close();
        };
    }

    /// <param name="videoWidth">The video's shown size on the page, in CSS pixels, for the window's shape.</param>
    public static async void Show(CoreWebView2NewWindowRequestedEventArgs e, Window owner, double videoWidth, double videoHeight, bool isPrivate)
    {
        // The page waits for its window until the deferral completes.
        var deferral = e.GetDeferral();
        var player = new PlayerWindow();
        Open.Add(player);
        player.Place(owner, videoWidth, videoHeight);
        player.AppWindow.Show(activateWindow: false);
        Taskbar.Hide(Win32Interop.GetWindowFromWindowId(player.AppWindow.Id));

        try
        {
            var environment = await MainWindow.EnvironmentAsync();
            if (isPrivate) await player._view.EnsureCoreWebView2Async(environment, MainWindow.PrivateOptions(environment));
            else await player._view.EnsureCoreWebView2Async(environment);
            var core = player._view.CoreWebView2;
            // The player's Back to tab and Close, Drift coming back to the tab, or the page going.
            core.WindowCloseRequested += (_, _) => player.Close();
            core.ProcessFailed += (_, _) => player.Close();
            e.NewWindow = core;
            e.Handled = true;
        }
        catch
        {
            player.Close();
        }
        finally
        {
            deferral.Complete();
        }
    }

    public static void CloseAll()
    {
        foreach (var player in Open.ToArray()) player.Close();
    }

    private void Place(Window owner, double videoWidth, double videoHeight)
    {
        var scale = owner.Content?.XamlRoot?.RasterizationScale ?? 1;
        var (width, height) = PlayerPlacement.Size(videoWidth, videoHeight);
        var area = DisplayArea.GetFromWindowId(owner.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var place = PlayerPlacement.Place(_lastPlace, (int)Math.Round(width * scale), (int)Math.Round(height * scale),
            new Box(area.X, area.Y, area.Width, area.Height));
        AppWindow.MoveAndResize(new RectInt32(place.X, place.Y, place.Width, place.Height));
        SetDragStrip();
    }

    /// <summary>The top strip drags the window, apart from the buttons at its right end.</summary>
    private void SetDragStrip()
    {
        try
        {
            var scale = Content?.XamlRoot?.RasterizationScale ?? 1;
            var width = AppWindow.ClientSize.Width;
            var strip = new RectInt32(0, 0, Math.Max(0, width - (int)(ButtonsWidth * scale)), (int)(DragStripHeight * scale));
            InputNonClientPointerSource.GetForWindowId(AppWindow.Id).SetRegionRects(NonClientRegionKind.Caption, [strip]);
        }
        catch
        {
            // Not draggable, then; it still works.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Windows 11's rounded corners, which a window without a title bar doesn't always get.</summary>
    private void RoundCorners()
    {
        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2;
        var round = DWMWCP_ROUND;
        _ = DwmSetWindowAttribute(Win32Interop.GetWindowFromWindowId(AppWindow.Id), DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
    }
}
