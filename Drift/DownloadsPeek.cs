using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Numerics;
using System.ComponentModel;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;
using System.Text.Json;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace Drift;

/// <summary>
/// Downloads as Arc has them, on the Library button: a download flies into it as it starts, a ring
/// around it shows their progress, and a finished file's picture sits on it till you've looked.
/// Hover it and your newest downloads rise above it (DownloadPeek, in Core, picks them), each with
/// the file's own picture. Click one to open it, or drag it out: onto a page's upload box, Explorer,
/// or another app. The library's list drags out the same way.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Arc's: long enough that passing over the button on the way somewhere doesn't open it.</summary>
    private static readonly TimeSpan DownloadPeekDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>Time to cross the gap between the button and the card.</summary>
    private static readonly TimeSpan DownloadPeekLinger = TimeSpan.FromMilliseconds(200);

    private DispatcherQueueTimer? _downloadPeekTimer;
    private bool _downloadPeekShown;

    private static DownloadItem[] PeekDownloads() =>
        DownloadPeek.Pick(Downloads.Items, i => (i.IsRunning, i.State == DownloadState.Completed, i.IsOnDisk),
            i => !Settings.Current.PeekHides.Contains(DownloadKinds.Of(i.FileName))).ToArray();

    private void DownloadPeekAnchor_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (_downloadPeekShown) StopDownloadPeekTimer();
        else if (!_libraryOpen) RunDownloadPeekTimer(DownloadPeekDelay, ShowDownloadPeek);
    }

    private void DownloadPeek_PointerEntered(object sender, PointerRoutedEventArgs e) => StopDownloadPeekTimer();

    // A download's menu is open, from the peek: it stays while the menu does (the pointer's on the menu).
    private bool _downloadMenuOpen;

    private void DownloadPeek_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_downloadDragging || _downloadMenuOpen) return;
        if (_downloadPeekShown) RunDownloadPeekTimer(DownloadPeekLinger, HideDownloadPeek);
        else StopDownloadPeekTimer();
    }

    private void RunDownloadPeekTimer(TimeSpan after, Action then)
    {
        StopDownloadPeekTimer();
        _downloadPeekTimer = DispatcherQueue.CreateTimer();
        _downloadPeekTimer.Interval = after;
        _downloadPeekTimer.IsRepeating = false;
        _downloadPeekTimer.Tick += (_, _) =>
        {
            _downloadPeekTimer = null;
            then();
        };
        _downloadPeekTimer.Start();
    }

    private void StopDownloadPeekTimer()
    {
        _downloadPeekTimer?.Stop();
        _downloadPeekTimer = null;
    }

    private void ShowDownloadPeek()
    {
        var items = PeekDownloads();
        if (items.Length == 0 || _libraryOpen) return;
        // Newest nearest the button, as in Arc.
        DownloadPeekList.ItemsSource = items.Reverse().ToArray();
        _downloadPeekShown = true;
        DownloadPeekCard.IsHitTestVisible = true;
        DownloadPeekCard.Opacity = 1;
        DownloadPeekCard.Translation = Vector3.Zero;
        foreach (var item in items) item.Refresh();
        Downloads.MarkSeen();
    }

    private void HideDownloadPeek()
    {
        StopDownloadPeekTimer();
        if (!_downloadPeekShown) return;
        _downloadPeekShown = false;
        DownloadPeekCard.IsHitTestVisible = false;
        DownloadPeekCard.Opacity = 0;
        DownloadPeekCard.Translation = _uiSettings.AnimationsEnabled ? new Vector3(0, 8, 0) : Vector3.Zero;
    }

    /// <summary>A download started, moved on or finished while the peek's up: it shows the newest again.</summary>
    private void RefreshDownloadPeek()
    {
        ToolTipService.SetToolTip(LibraryButton, PeekDownloads().Length > 0 ? null : "Library");
        if (!_downloadPeekShown) return;
        var items = PeekDownloads();
        if (items.Length == 0) HideDownloadPeek();
        else if (DownloadPeekList.ItemsSource is not DownloadItem[] shown || !shown.SequenceEqual(items.Reverse()))
            DownloadPeekList.ItemsSource = items.Reverse().ToArray();
    }

    private void DownloadPeek_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not DownloadItem item) return;
        if (item.IsOnDisk)
        {
            HideDownloadPeek();
            Downloads.Open(item);
        }
        else
        {
            // Still on its way: the library's list has its progress and Cancel.
            HideDownloadPeek();
            OpenLibrary("downloads");
        }
    }

    /// <summary>
    /// A saved file dragged out of the peek or the library's list. Explorer and other apps take it from
    /// the drag itself; a page in Drift doesn't see a drag that starts in Drift, so Drift passes it on
    /// (DownloadDrop_DragOver). One still downloading (or gone from disk) stays put. The peek stays up
    /// till the file's dropped.
    /// </summary>
    private void DownloadItems_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.FirstOrDefault() is not DownloadItem item || !item.IsOnDisk)
        {
            e.Cancel = true;
            return;
        }
        var path = item.FilePath;
        _draggedDownload = path;
        PagesTakeDrops(false);
        e.Data.RequestedOperation = DataPackageOperation.Copy;
        e.Data.SetDataProvider(StandardDataFormats.StorageItems, async request =>
        {
            var deferral = request.GetDeferral();
            try { request.SetData(new IStorageItem[] { await StorageFile.GetFileFromPathAsync(path) }); }
            catch { }
            finally { deferral.Complete(); }
        });
        if (sender == DownloadPeekList)
        {
            StopDownloadPeekTimer();
            _downloadDragging = true;
        }
    }

    /// <summary>The file being dragged out of the downloads, while it is.</summary>
    private string? _draggedDownload;

    private bool _downloadDragging;

    private void DownloadItems_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs e)
    {
        LeaveDragPage();
        _draggedDownload = null;
        PagesTakeDrops(true);
        if (sender != DownloadPeekList) return;
        _downloadDragging = false;
        HideDownloadPeek();
    }

    /// <summary>The pages (tabs, split view, Peek) take a download dragged over and dropped on them.</summary>
    private void WireDownloadDrops()
    {
        foreach (var host in new[] { WebHost, PeekWebHost })
        {
            host.AllowDrop = true;
            host.AddHandler(UIElement.DragOverEvent, new DragEventHandler(DownloadDrop_DragOver), handledEventsToo: true);
            host.AddHandler(UIElement.DragLeaveEvent, new DragEventHandler(DownloadDrop_DragLeave), handledEventsToo: true);
            host.AddHandler(UIElement.DropEvent, new DragEventHandler(DownloadDrop_Drop), handledEventsToo: true);
        }
    }

    /// <summary>
    /// While a download's dragged out of Drift, the pages leave the drag to Drift (they'd otherwise
    /// take it as WinUI hands it over too, and get it twice); afterwards they take drops from other
    /// apps again.
    /// </summary>
    private void PagesTakeDrops(bool take)
    {
        foreach (var host in new[] { WebHost, PeekWebHost })
            foreach (var view in Descendants(host).OfType<WebView2>()) view.AllowDrop = take;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }

    /// <summary>The page a download's being dragged over, and its CSS pixels per view pixel (zoom).</summary>
    private WebView2? _dragPage;
    private double _dragPageScale = 1;

    /// <summary>Off the page, unless it's back over it by the time this runs (DragLeave also fires between its parts).</summary>
    private DispatcherQueueTimer? _dragLeaveTimer;

    /// <summary>
    /// A page in Drift doesn't see a drag that starts in Drift, so Drift passes it on as the browser's
    /// own input (DevTools' Input.dispatchDragEvent): to the page it's a file dragged in from Explorer,
    /// so it shows its drop zone, and a drop lands on whatever's there, iframes included.
    /// </summary>
    private void DownloadDrop_DragOver(object sender, DragEventArgs e)
    {
        if (_draggedDownload is not { } path || sender is not UIElement host) return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.Handled = true;
        var view = PageUnder(e, host);
        if (view is not null && view == _dragPage) StopDragLeaveTimer();
        if (view != _dragPage)
        {
            LeaveDragPage();
            if (view is null) return;
            _dragPage = view;
            _dragPageScale = 1;
            _ = MeasureDragPageAsync(view);
            SendPageDrag(view, "dragEnter", e, path);
        }
        if (view is not null) SendPageDrag(view, "dragOver", e, path);
    }

    private void DownloadDrop_DragLeave(object sender, DragEventArgs e)
    {
        if (_draggedDownload is null || _dragPage is null) return;
        StopDragLeaveTimer();
        _dragLeaveTimer = DispatcherQueue.CreateTimer();
        _dragLeaveTimer.Interval = TimeSpan.FromMilliseconds(80);
        _dragLeaveTimer.IsRepeating = false;
        _dragLeaveTimer.Tick += (_, _) =>
        {
            _dragLeaveTimer = null;
            LeaveDragPage();
        };
        _dragLeaveTimer.Start();
    }

    private void StopDragLeaveTimer()
    {
        _dragLeaveTimer?.Stop();
        _dragLeaveTimer = null;
    }

    private void DownloadDrop_Drop(object sender, DragEventArgs e)
    {
        if (_draggedDownload is not { } path || sender is not UIElement host) return;
        e.Handled = true;
        StopDragLeaveTimer();
        var view = PageUnder(e, host);
        if (view is not null && view == _dragPage) SendPageDrag(view, "drop", e, path);
        else LeaveDragPage();
        _dragPage = null;
    }

    /// <summary>The drag went somewhere else, or was called off: the page drops its drop zone.</summary>
    private void LeaveDragPage()
    {
        StopDragLeaveTimer();
        if (_dragPage is { } page && _draggedDownload is { } path)
        {
            // DevTools' dragCancel ends the drag without telling the page, which would keep its drop zone up;
            // a move off the page has it see the file leave, as a real pointer leaving does.
            SendPageDrag(page, "leave", null, path);
            SendPageDrag(page, "dragCancel", null, path);
        }
        _dragPage = null;
    }

    private WebView2? PageUnder(DragEventArgs e, UIElement host) =>
        VisualTreeHelper.FindElementsInHostCoordinates(e.GetPosition((UIElement)Content), host).OfType<WebView2>()
            .FirstOrDefault(v => v.CoreWebView2 is not null && v.Visibility == Visibility.Visible && v.Opacity > 0 && v.IsHitTestVisible);

    /// <summary>The page's zoom: DevTools takes points in its CSS pixels.</summary>
    private async Task MeasureDragPageAsync(WebView2 view)
    {
        try
        {
            var width = await view.CoreWebView2.ExecuteScriptAsync("innerWidth");
            if (double.TryParse(width, NumberStyles.Float, CultureInfo.InvariantCulture, out var css) && css > 0 && view.ActualWidth > 0)
                _dragPageScale = css / view.ActualWidth;
        }
        catch { }
    }

    /// <summary>Drag events on their way to pages, sent one at a time, in order.</summary>
    private readonly List<(WebView2 View, string Type, string Message)> _pageDrags = new();
    private bool _sendingPageDrags;

    /// <summary>
    /// Queues one for the page. A move only matters where the pointer is now, so it replaces a move
    /// still waiting: a busy page gets fewer of them, not a backlog it's still working through after
    /// the drag has left.
    /// </summary>
    private void SendPageDrag(WebView2 view, string type, DragEventArgs? e, string path)
    {
        var at = e?.GetPosition(view) ?? new Point(-10000, -10000);
        var message = JsonSerializer.Serialize(new
        {
            type = type == "leave" ? "dragOver" : type,
            x = at.X * _dragPageScale,
            y = at.Y * _dragPageScale,
            data = new { items = Array.Empty<object>(), files = new[] { path }, dragOperationsMask = 1 },
        });
        if (type is "dragCancel" or "drop") _pageDrags.RemoveAll(d => d.View == view && d.Type == "dragOver");
        else if (type == "dragOver" && _pageDrags.Count > 0 && _pageDrags[^1] is { Type: "dragOver" } last && last.View == view)
            _pageDrags.RemoveAt(_pageDrags.Count - 1);
        _pageDrags.Add((view, type, message));
        if (!_sendingPageDrags) SendPageDragsAsync();
    }

    private async void SendPageDragsAsync()
    {
        _sendingPageDrags = true;
        while (_pageDrags.Count > 0)
        {
            var (view, _, message) = _pageDrags[0];
            _pageDrags.RemoveAt(0);
            try { await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchDragEvent", message); }
            catch { }
        }
        _sendingPageDrags = false;
    }

    // ───────────────────────── The Library button ─────────────────────────

    /// <summary>The download whose file is on the Library button's badge.</summary>
    private DownloadItem? _libraryBadgeFor;

    /// <summary>
    /// As in Zen: the newest download's file sits on a badge at the top right of the Library button,
    /// ringed with the progress of everything downloading (DownloadRing); once they're done it stays
    /// till you've hovered the button or opened the library.
    /// </summary>
    private void ShowLibraryButtonDownloads(IReadOnlyCollection<DownloadItem> running)
    {
        if (DownloadRing.Of(running.Select(i => (i.Received, i.Total, i.IsPaused)).ToList()) is { } ring)
        {
            DownloadsRing.Visibility = Visibility.Visible;
            DownloadsRing.IsActive = true;
            DownloadsRing.IsIndeterminate = ring.Filled is null;
            if (ring.Filled is { } filled) DownloadsRing.Value = filled;
            // Greyed while everything's paused.
            DownloadsRing.Opacity = ring.Paused ? 0.4 : 1;
        }
        else
        {
            DownloadsRing.IsActive = false;
            DownloadsRing.Visibility = Visibility.Collapsed;
        }

        var shown = running.FirstOrDefault()
            ?? (Downloads.HasUnseen && Downloads.Items.FirstOrDefault() is { IsOnDisk: true } latest ? latest : null);
        if (shown != _libraryBadgeFor)
        {
            if (_libraryBadgeFor is not null) _libraryBadgeFor.PropertyChanged -= LibraryBadge_PropertyChanged;
            _libraryBadgeFor = shown;
            if (shown is not null) shown.PropertyChanged += LibraryBadge_PropertyChanged;
        }
        LibraryBadgeImage.Source = shown?.Picture;
        LibraryBadge.Visibility = shown is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Its picture came (FileThumbnails is asked as it's shown).</summary>
    private void LibraryBadge_PropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        LibraryBadgeImage.Source = _libraryBadgeFor?.Picture;

    // ───────────────────────── A download starting ─────────────────────────

    /// <summary>Above everything in the window: the icons of downloads on their way to the Library button.</summary>
    private Canvas? _flightLayer;

    private const float FlightIconSize = 32;

    /// <summary>
    /// A download's started: as in Zen, its file's icon pops up where you clicked (the pointer's still
    /// there: a link, a menu's Download, a button) and curves into the badge on the Library button,
    /// across first and then down, shrinking as it lands, and the badge takes it with a bump. From the
    /// middle of the page if the pointer's elsewhere (a download from the keyboard). Not with reduced
    /// motion, nor while the button's out of sight.
    /// </summary>
    private async void FlyDownloadIn(WebView2 view, string path)
    {
        if (!_uiSettings.AnimationsEnabled || _sidebarHidden || _libraryOpen || LibraryButton.ActualWidth == 0 || view.ActualWidth == 0) return;
        var root = (UIElement)Content;
        // Now, before anything's awaited: the pointer's where the click was.
        var pointer = PointerOverWindow();
        Point from;
        try
        {
            var page = view.TransformToVisual(root).TransformBounds(new Rect(0, 0, view.ActualWidth, view.ActualHeight));
            from = pointer is { } p && page.Contains(p) ? p : new Point(page.X + page.Width / 2, page.Y + page.Height / 2);
        }
        catch (ArgumentException)
        {
            return;
        }
        var icon = await FileIcons.ForAsync(path, exists: false);
        if (icon is null || _sidebarHidden || _libraryOpen || LibraryBadge.Visibility != Visibility.Visible) return;
        // The badge shows as the download's tracked; it may not be laid out yet.
        if (LibraryBadge.ActualWidth == 0) LibraryBadge.UpdateLayout();
        Point to;
        try
        {
            to = LibraryBadge.TransformToVisual(root).TransformPoint(new Point(LibraryBadge.ActualWidth / 2, LibraryBadge.ActualHeight / 2));
        }
        catch (ArgumentException)
        {
            return;
        }

        if (_flightLayer is null)
        {
            _flightLayer = new Canvas { IsHitTestVisible = false };
            Grid.SetRowSpan(_flightLayer, 100);
            Grid.SetColumnSpan(_flightLayer, 100);
            Root.Children.Add(_flightLayer);
        }
        var image = new Image { Source = icon, Width = FlightIconSize, Height = FlightIconSize };
        Canvas.SetLeft(image, from.X - FlightIconSize / 2);
        Canvas.SetTop(image, from.Y - FlightIconSize / 2);
        _flightLayer.Children.Add(image);

        ElementCompositionPreview.SetIsTranslationEnabled(image, true);
        var visual = ElementCompositionPreview.GetElementVisual(image);
        var compositor = visual.Compositor;
        visual.CenterPoint = new Vector3(FlightIconSize / 2, FlightIconSize / 2, 0);
        var duration = TimeSpan.FromMilliseconds(750);
        const float Popped = 0.12f;
        var pop = compositor.CreateCubicBezierEasingFunction(new Vector2(0.23f, 1), new Vector2(0.32f, 1));
        var land = compositor.CreateCubicBezierEasingFunction(new Vector2(0.77f, 0), new Vector2(0.175f, 1));
        // Measured from Zen: across leads and down follows, so the path bows instead of cutting the corner.
        var across = compositor.CreateCubicBezierEasingFunction(new Vector2(0.3f, 0.1f), new Vector2(0.65f, 1));
        var down = compositor.CreateCubicBezierEasingFunction(new Vector2(0.35f, 0), new Vector2(0.75f, 1));

        var x = compositor.CreateScalarKeyFrameAnimation();
        x.InsertKeyFrame(Popped, 0);
        x.InsertKeyFrame(1, (float)(to.X - from.X), across);
        x.Duration = duration;
        var y = compositor.CreateScalarKeyFrameAnimation();
        y.InsertKeyFrame(Popped, 0);
        y.InsertKeyFrame(1, (float)(to.Y - from.Y), down);
        y.Duration = duration;
        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0, new Vector3(0.6f, 0.6f, 1));
        scale.InsertKeyFrame(Popped, Vector3.One, pop);
        scale.InsertKeyFrame(0.7f, Vector3.One);
        scale.InsertKeyFrame(1, new Vector3(15f / FlightIconSize, 15f / FlightIconSize, 1), land);
        scale.Duration = duration;
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(Popped, 1, pop);
        fade.InsertKeyFrame(0.9f, 1);
        fade.InsertKeyFrame(1, 0);
        fade.Duration = duration;

        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        visual.StartAnimation("Translation.X", x);
        visual.StartAnimation("Translation.Y", y);
        visual.StartAnimation("Scale", scale);
        visual.StartAnimation("Opacity", fade);
        batch.End();
        batch.Completed += (_, _) =>
        {
            _flightLayer?.Children.Remove(image);
            LibraryBadge.CenterPoint = new Vector3((float)LibraryBadge.ActualWidth / 2, (float)LibraryBadge.ActualHeight / 2, 0);
            Spring.To(LibraryBadge, "Scale", Vector3.One, 0.35, 0.45, from: new Vector3(1.3f, 1.3f, 1));
        };
    }

    // ───────────────────────── Right-click menus ─────────────────────────

    /// <summary>
    /// The Library button's menu, as in Arc: which kinds of file the downloads peek shows.
    /// </summary>
    private void LibraryButton_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
        HideDownloadPeek();
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = "Show on hover", IsEnabled = false });
        foreach (var kind in DownloadKinds.All)
        {
            var item = new ToggleMenuFlyoutItem { Text = DownloadKinds.Name(kind), IsChecked = !Settings.Current.PeekHides.Contains(kind) };
            item.Click += (_, _) => Settings.Update(s =>
            {
                s.PeekHides.Remove(kind);
                if (!item.IsChecked) s.PeekHides.Add(kind);
            });
            menu.Items.Add(item);
        }
        var options = new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions();
        if (e.TryGetPosition(LibraryButton, out var at)) options.Position = at;
        menu.ShowAt(LibraryButton, options);
    }

    /// <summary>A download's menu in the library: open, show, copy its link, take it off the list, or bin the file.</summary>
    private void DownloadRow_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DownloadItem download) return;
        e.Handled = true;
        var menu = NewMenu();
        var copy = Uri.TryCreate(download.Url, UriKind.Absolute, out var link) && link.Scheme is "http" or "https"
            ? Command("Copy download link", () =>
            {
                var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
                data.SetText(link.AbsoluteUri);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
            })
            : null;
        if (download.IsFinishing) FillMenu(menu, [copy]);
        else if (download.IsRunning)
            FillMenu(menu,
                [Command(download.PauseLabel, () => Downloads.PauseOrResume(download)), Command("Cancel", () => Downloads.Cancel(download))],
                [copy]);
        else
            FillMenu(menu,
            [
                Command("Open", () => Downloads.Open(download), download.IsOnDisk),
                Command("Show in folder", () => Downloads.ShowInFolder(download), download.IsOnDisk),
            ],
            [copy],
            [
                download.IsOnDisk ? Command("Move to Recycle Bin", () =>
                {
                    if (Downloads.Recycle(download)) RefreshDownloads();
                }) : null,
                Command("Remove from list", () =>
                {
                    Downloads.Remove(download);
                    RefreshDownloads();
                }),
            ]);
        if (_downloadPeekShown)
        {
            menu.Opened += (_, _) =>
            {
                _downloadMenuOpen = true;
                StopDownloadPeekTimer();
            };
            menu.Closed += (_, _) =>
            {
                _downloadMenuOpen = false;
                if (_downloadPeekShown) RunDownloadPeekTimer(DownloadPeekLinger, HideDownloadPeek);
            };
        }
        var options = new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions();
        if (e.TryGetPosition(sender, out var at)) options.Position = at;
        menu.ShowAt(sender, options);
    }
}
