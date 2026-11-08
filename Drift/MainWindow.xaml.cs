using System;
using Windows.ApplicationModel.DataTransfer;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Drift.Models;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using Windows.UI.ViewManagement;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace Drift;

public sealed partial class MainWindow : Window
{
    // Pinned tabs on first launch. Hacker News is the easiest place to try Peek:
    // its links go to other sites, so every story opens in the overlay.
    private static readonly string[] DefaultPinned =
    {
        "https://news.ycombinator.com",
        "https://github.com",
        "https://mail.google.com",
    };

    private const double SidebarWidth = 208;

    // The floating sidebar card's gap from the window's edges, matching the page's.
    private const double FloatingInset = 8;

    // How far the floating sidebar's hit area runs past the window's left edge, so the spring's
    // overshoot never pulls it off the edge the pointer is on.
    private const double FloatingBleed = 16;

    // Makes the floating sidebar's empty areas, the gap around the card included, hit-testable.
    private static readonly Brush FloatingHitArea = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    // Proves a message came from the script below rather than from the page.
    private static readonly string ShortcutToken = Guid.NewGuid().ToString("N");

    // Injected into every page. While a page has focus, WebView2 receives the keystrokes
    // instead of the app, so the page forwards them: Esc, and every press with Ctrl or Alt, or of a
    // function key, as "key:code,ctrl,shift,alt" (code is the Windows virtual-key code). The app
    // decides which of those are shortcuts, from the current settings, so a changed shortcut works
    // in pages already open. The ones that were shortcuts when the page loaded are also kept from
    // the page (preventDefault), so a page's own Ctrl+S doesn't fire as well.
    // Alt+click on a link opens it in Peek, from any tab, as "peek:url" (instead of Chromium's
    // Alt+click, which downloads the link; the page doesn't see the click either).
    // It also reports where links are pressed, so Peek can grow out of the link that opened it:
    // on pointerdown, which arrives well before the click navigates; for keyboard activation
    // (a click with no pointer), the link's centre. Positions are CSS pixels, plus the page width
    // so the app can allow for zoom.
    // Zooming is Chromium's own (Ctrl+plus/minus/0, Ctrl+wheel), which WinUI's WebView2 doesn't
    // report, so the page does: its pixel ratio, which zoom multiplies, as "zoom:ratio" when it
    // loads and whenever it changes.
    // The script runs before any page script, so it keeps its own reference to postMessage
    // (a page can't wrap it to read the token) and ignores synthetic events.
    private static string InputScript()
    {
        var keys = string.Join(",", Settings.Actions.Select(Settings.ShortcutFor).Concat(TabNumberKeys).Select(k =>
            $"[{(int)k.Key},{(k.Ctrl ? "true" : "false")},{(k.Shift ? "true" : "false")},{(k.Alt ? "true" : "false")}]"));
        return """
            (() => {
              if (window.top !== window || !window.chrome || !window.chrome.webview) return;
              const post = window.chrome.webview.postMessage.bind(window.chrome.webview);
              const token = '__TOKEN__';
              const shortcuts = [__KEYS__];
              const linkOf = (e) => e.composedPath().find((n) => n instanceof Element && n.matches('a[href], area[href]'));
              const postLink = (x, y) => post(token + ':link:' + x + ',' + y + ',' + window.innerWidth);
              window.addEventListener('pointerdown', (e) => {
                if (e.isTrusted && e.button <= 1 && linkOf(e)) postLink(e.clientX, e.clientY);
              }, true);
              window.addEventListener('click', (e) => {
                if (e.isTrusted && e.altKey && !e.ctrlKey && !e.shiftKey && !e.metaKey && e.button === 0) {
                  const link = linkOf(e);
                  if (link && link.href) {
                    e.preventDefault(); e.stopImmediatePropagation();
                    post(token + ':peek:' + link.href);
                    return;
                  }
                }
                if (!e.isTrusted || e.detail !== 0) return;
                const link = linkOf(e);
                if (!link) return;
                const r = link.getBoundingClientRect();
                postLink(r.left + r.width / 2, r.top + r.height / 2);
              }, true);
              window.addEventListener('keydown', (e) => {
                if (!e.isTrusted) return;
                if (e.key === 'Escape') { post(token + ':escape'); return; }
                const fn = e.keyCode >= 112 && e.keyCode <= 123;
                if (!e.ctrlKey && !e.altKey && !fn) return;
                if (e.key === 'Control' || e.key === 'Alt' || e.key === 'Shift') return;
                if (shortcuts.some((k) => k[0] === e.keyCode && k[1] === e.ctrlKey && k[2] === e.shiftKey && k[3] === e.altKey)) {
                  e.preventDefault(); e.stopPropagation();
                }
                post(token + ':key:' + e.keyCode + ',' + (+e.ctrlKey) + ',' + (+e.shiftKey) + ',' + (+e.altKey));
              }, true);
              const zoom = () => post(token + ':zoom:' + devicePixelRatio);
              // A range, not the exact ratio: it carries float noise (1.100000023…) that an exact query never matches.
              const watchZoom = () => matchMedia('(min-resolution: ' + (devicePixelRatio - 0.001) + 'dppx) and (max-resolution: ' + (devicePixelRatio + 0.001) + 'dppx)')
                .addEventListener('change', () => { zoom(); watchZoom(); }, { once: true });
              zoom();
              watchZoom();
            __PIP__
            })();
            """.Replace("__PIP__", PipScript() + MediaScript()).Replace("__TOKEN__", ShortcutToken).Replace("__KEYS__", keys);
    }

    // Each view's copy of the script, so it can be swapped when shortcuts change.
    private static readonly ConditionalWeakTable<CoreWebView2, string> InputScriptIds = new();

    // Overlay scroll bars: Chromium draws them over the page, so they take up none of its width.
    // They're a thin thumb with no arrow buttons or track, widen while hovered, and fade when the
    // page isn't scrolling. (Styled ::-webkit-scrollbar scroll bars always reserve layout space,
    // which is why Drift no longer injects scroll bar CSS: a page that styles its own still gets
    // those, at the width it chose.) Every view shares this one environment, since a popup's
    // view must come from the same environment as the page that opened it.
    private static Task<CoreWebView2Environment>? _environment;

    internal static Task<CoreWebView2Environment> EnvironmentAsync() =>
        _environment ??= CoreWebView2Environment.CreateWithOptionsAsync(null, ProfileFolder(), new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = "--enable-features=OverlayScrollbar",
            AreBrowserExtensionsEnabled = true,
        }).AsTask();

    /// <summary>
    /// The web profile (logins, cookies, site data, cache) lives with the rest of Drift's data in
    /// %LOCALAPPDATA%\Drift\Profile: it belongs to your Windows account and survives clean builds.
    /// It used to be WebView2's default, Drift.exe.WebView2 beside the app; if that's still there and
    /// the new one isn't, it's moved over first, so you stay signed in.
    /// </summary>
    private static string ProfileFolder()
    {
        var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Drift", "Profile");
        var legacy = Path.Combine(AppContext.BaseDirectory, "Drift.exe.WebView2");
        try
        {
            if (!Directory.Exists(profile) && Directory.Exists(legacy))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(profile)!);
                Directory.Move(legacy, profile);
            }
        }
        catch
        {
            // Couldn't move it (another drive, or files in use): start with a fresh profile instead.
        }
        return profile;
    }

    private BrowserTab? _active;
    private WebView2? _peekView;
    private Uri? _peekPending;
    private BrowserTab? _peekOwner;
    private WebView2? _closingPeekView;
    private readonly UISettings _uiSettings = new();
    private bool _sidebarHidden;
    private bool _sidebarDismissing;
    private int _openSidebarMenus;
    // Elements already wired by AddPressFeedback (list containers are recycled, so they come round again).
    private readonly HashSet<UIElement> _pressFeedback = new();
    // Where the Peek card grows from and shrinks back to (AimPeekCard).
    private float _cardFromScale = 0.5f, _cardFromY, _cardToScale = 0.6f, _cardToY;
    private readonly DispatcherQueueTimer _sidebarDismissTimer;
    private readonly DispatcherQueueTimer _sidebarWatch;
    private readonly DispatcherQueueTimer _sessionTimer;
    private long? _pointerAtEdgeSince;
    private long? _pointerOffSidebarSince;
    private bool _syncingSelection;

    public ObservableCollection<BrowserTab> EssentialTabs { get; } = new();
    public ObservableCollection<BrowserTab> PinnedTabs { get; } = new();
    public ObservableCollection<BrowserTab> Tabs { get; } = new();

    /// <summary>Every tab, top of the sidebar to bottom.</summary>
    private IEnumerable<BrowserTab> AllTabs => EssentialTabs.Concat(FolderTabs).Concat(PinnedTabs).Concat(Tabs.SelectMany(RegularTabsOf));

    /// <summary>
    /// A row of the tab list: its tab, or for a split's row (Split.cs), the split's regular tabs, left
    /// first (its Essentials, pinned and folder tabs are in their own places).
    /// </summary>
    private static IEnumerable<BrowserTab> RegularTabsOf(BrowserTab row) =>
        row is { IsSplitRow: true, Split: { } split } ? new[] { split.Left, split.Right }.Where(t => t.Place == TabPlace.Regular) : [row];

    /// <summary>The list a group's tabs are in. Folder tabs are in their folders instead (TabFolder.Children).</summary>
    private ObservableCollection<BrowserTab> CollectionFor(TabPlace place) => place switch
    {
        TabPlace.Essential => EssentialTabs,
        TabPlace.Pinned => PinnedTabs,
        TabPlace.Regular => Tabs,
        _ => throw new ArgumentException("Folder tabs are in their folders", nameof(place)),
    };

    // A private window keeps nothing: no session, no history, no closed tabs, and its pages use a
    // profile of their own whose cookies and storage go when the window closes.
    private readonly bool _private;
    private static readonly List<MainWindow> PrivateWindows = new();

    // Menus close before the window minimizes, and none is left showing after (GhostPopups.cs).
    private readonly GhostPopups _ghostPopups;

    /// <summary>The downloads list (Downloads.Items), for the flyout's binding.</summary>
    public ObservableCollection<DownloadItem> DownloadItems => Downloads.Items;

    /// <param name="launchUris">Pages Drift was started to open (a link from another app); they open
    /// once the tabs from last time are back.</param>
    /// <param name="isPrivate">A private window (Ctrl+Shift+N).</param>
    /// <summary>False once the window has closed.</summary>
    internal bool IsOpen { get; private set; } = true;

    public MainWindow(IEnumerable<Uri>? launchUris = null, bool isPrivate = false)
    {
        _private = isPrivate;
        if (launchUris is not null) _launchUris.AddRange(launchUris);
        InitializeComponent();
        _ghostPopups = new GhostPopups(this);
        // The site settings button shows while the pointer is anywhere in the sidebar, over its buttons too.
        Sidebar.AddHandler(UIElement.PointerEnteredEvent, new PointerEventHandler(Sidebar_PointerEntered), true);
        Sidebar.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler(Sidebar_PointerExited), true);
        if (_private)
        {
            PrivateWindows.Add(this);
            PrivateTint.Visibility = Visibility.Visible;
            PrivateBadge.Visibility = Visibility.Visible;
            Title = "Drift Private";
        }

        // No system title bar or caption buttons, like Arc: the content fills the window,
        // window controls live in the menu, and UpdateDragRegions() says where to drag from.
        ExtendsContentIntoTitleBar = true;
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.SetBorderAndTitleBar(true, false);
        // Acrylic, like the Start menu: the windows behind show through, blurred. (Mica only takes
        // its colour from the wallpaper.) Windows turns it solid while the window isn't active.
        SystemBackdrop = new DesktopAcrylicBackdrop();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1400, 900));

        History.Load();
        Closed += (_, _) =>
        {
            IsOpen = false;
            _adBlock?.Close();
            StopCalendar();
            StopMail();
            StopOutlook();
            Settings.Changed -= Settings_Changed;
            Downloads.Changed -= ShowDownloads;
            if (_private)
            {
                // Only this window goes; its downloads leave the list with the last private window.
                PrivateWindows.Remove(this);
                if (PrivateWindows.Count == 0) Downloads.ForgetPrivate();
                return;
            }
            SaveSession(wait: true);
            foreach (var window in PrivateWindows.ToList()) window.Close();
            PopupWindow.CloseAll();
            PlayerWindow.CloseAll();
            SettingsWindow.CloseIfOpen();
            History.SaveNow(wait: true);
        };
        Downloads.Changed += ShowDownloads;
        _sessionTimer = DispatcherQueue.CreateTimer();
        _sessionTimer.Interval = TimeSpan.FromSeconds(2);
        _sessionTimer.IsRepeating = false;
        _sessionTimer.Tick += (_, _) => SaveSession(wait: false);
        Settings.Changed += Settings_Changed;
        ApplyShortcuts();
        ShowNewTabHint();
        PeekExitAnimation.Completed += (_, _) => FinishPeekExit();
        PeekFadeOutAnimation.Completed += (_, _) => FinishPeekExit();
        _sidebarDismissTimer = DispatcherQueue.CreateTimer();
        _sidebarDismissTimer.Interval = TimeSpan.FromMilliseconds(300);
        _sidebarDismissTimer.IsRepeating = false;
        _sidebarDismissTimer.Tick += SidebarDismissTimer_Tick;
        _sidebarWatch = DispatcherQueue.CreateTimer();
        _sidebarWatch.Interval = TimeSpan.FromMilliseconds(20);
        _sidebarWatch.Tick += SidebarWatch_Tick;

        foreach (var button in new[] { MenuButton, SidebarToggleButton, BackButton, ForwardButton, ReloadButton })
            AddPressFeedback(button, 0.95f);
        AddPressFeedback(NewTabButton, 0.98f);
        AddPressFeedback(AddressPill, 0.98f);
        AddPressFeedback(PeekCloseButton, 0.95f);
        AddPressFeedback(PeekToTabButton, 0.95f);
        EssentialList.ContainerContentChanging += ListItem_ContainerContentChanging;
        EssentialTabs.CollectionChanged += (_, _) => LayoutEssentials();
        // The lists handle drags themselves (to reorder), marking drops from other lists handled too,
        // so these listen to handled events as well.
        foreach (var list in new ListViewBase[] { EssentialList, FolderList, PinnedList, TabList })
        {
            list.AddHandler(UIElement.DragOverEvent, new DragEventHandler(Tabs_DragOver), handledEventsToo: true);
            list.AddHandler(UIElement.DropEvent, new DragEventHandler(Tabs_Drop), handledEventsToo: true);
            list.AddHandler(UIElement.DragLeaveEvent, new DragEventHandler(Tabs_DragLeave), handledEventsToo: true);
        }
        PinnedList.ContainerContentChanging += ListItem_ContainerContentChanging;
        FolderList.ContainerContentChanging += ListItem_ContainerContentChanging;
        TabList.ContainerContentChanging += ListItem_ContainerContentChanging;
        WatchForSlides();
        SetUpLibrary();
    }

    private void EssentialList_SizeChanged(object sender, SizeChangedEventArgs e) => LayoutEssentials();

    /// <summary>Essentials fill the sidebar's width, as many across as EssentialsGrid says.</summary>
    private void LayoutEssentials()
    {
        if (EssentialList.ItemsPanelRoot is not ItemsWrapGrid grid) return;
        var width = EssentialList.ActualWidth - EssentialList.Padding.Left - EssentialList.Padding.Right;
        if (width <= 0) return;
        // Counting a tab that's being dragged in, so the grid shows the layout it would get.
        var columns = EssentialsGrid.Columns(EssentialTabs.Count + _essentialsArriving);
        // Not rounded down: the last tile's right edge stays on the address bar's.
        var cell = width / columns;
        if (grid.MaximumRowsOrColumns != columns) grid.MaximumRowsOrColumns = columns;
        if (grid.ItemWidth != cell) grid.ItemWidth = cell;
    }

    private async void Root_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateDragRegions();
        ShowDownloads();
        if (_private)
        {
            // Nothing to bring back: it opens on a new tab.
            _restored = true;
            OpenCommandBar(newTab: true, animate: false);
            return;
        }
        try
        {
            // The tabs from last time, or on first launch the default pinned ones. Restored tabs
            // show their saved title and icon and load when first shown, so many tabs start quickly.
            var session = Session.Load();
            if (session?.Closed is { } closed) _closedTabs.AddRange(closed);
            var opening = new List<Task<BrowserTab>>();
            if (session is null)
            {
                foreach (var url in DefaultPinned)
                    opening.Add(CreateTabAsync(new Uri(url), TabPlace.Essential, activate: false));
            }
            else
            {
                var folders = _folderTree.Load(session.Folders);
                // Each saved tab's tab, by position, for putting splits back together.
                var restoring = new Task<BrowserTab>?[session.Tabs.Count];
                for (var i = 0; i < session.Tabs.Count; i++)
                {
                    var saved = session.Tabs[i];
                    if (!Uri.TryCreate(saved.Url, UriKind.Absolute, out var uri)) continue;
                    Uri.TryCreate(saved.Home, UriKind.Absolute, out var home);
                    var place = Enum.TryParse<TabPlace>(saved.Place, out var p) ? p
                        : saved.Pinned ? TabPlace.Essential : TabPlace.Regular;
                    TabFolder? folder = null;
                    if (place == TabPlace.Folder && (saved.Folder is null || !folders.TryGetValue(saved.Folder, out folder)))
                        place = TabPlace.Pinned;
                    opening.Add(restoring[i] = CreateTabAsync(uri, place, activate: i == session.Active,
                        deferLoad: true, title: saved.Title, home: home, folder: folder));
                    // It's in its folder already (added before the first await): note where it goes.
                    if (folder is not null) _folderTree.AddSaved((IFolderTab)folder.Children[^1], folder, saved.Position);
                }
                _folderTree.Arrange();
                RefreshFolderRows();
                await Task.WhenAll(opening);
                RestoreSplits(restoring.Select(t => t?.Result).ToList(), session);
            }
            await Task.WhenAll(opening);
            _restored = true;
            StartCalendar();
            StartMail();
            StartOutlook();
            // Started to open a link: that's what's in front, not the start page.
            if (_launchUris.Count > 0)
            {
                foreach (var uri in _launchUris) OpenInNewTab(uri);
                _launchUris.Clear();
            }
            else OpenStartupPage();
        }
        catch (Exception ex)
        {
            ShowStartupError(ex);
        }
    }

    /// <summary>What Settings says to show first: the last tab (already in front), a new tab, or a page.</summary>
    private void OpenStartupPage()
    {
        switch (Settings.Current.OnStartup)
        {
            case StartupBehavior.NewTab:
                OpenCommandBar(newTab: true, animate: false);
                break;
            case StartupBehavior.Page when TryParseAddress(Settings.Current.StartupPage.Trim(), out var page):
                var open = AllTabs.FirstOrDefault(t =>
                    string.Equals(t.Url?.TrimEnd('/'), page.ToString().TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
                if (open is not null) Activate(open);
                else OpenInNewTab(page);
                break;
        }
    }

    // ───────────────────────── Session ─────────────────────────

    private void SaveSessionSoon()
    {
        _sessionTimer.Stop();
        _sessionTimer.Start();
    }

    /// <summary>
    /// The tabs to bring back next time. With "keep pinned tabs" only those, on their pinned pages;
    /// otherwise every tab where it is now.
    /// </summary>
    private void SaveSession(bool wait)
    {
        _sessionTimer.Stop();
        if (_private) return;
        var keepAll = Settings.Current.OnClose == CloseBehavior.KeepTabs;
        var data = new SessionData();
        var savedAt = new Dictionary<BrowserTab, int>();
        foreach (var tab in AllTabs.Where(t => keepAll || t.IsPinned))
        {
            var home = tab.HomeUri?.ToString();
            var url = keepAll ? tab.Url : home;
            if (string.IsNullOrEmpty(url) || url == "about:blank") url = home;
            if (string.IsNullOrEmpty(url)) continue;
            if (tab == _active) data.Active = data.Tabs.Count;
            savedAt[tab] = data.Tabs.Count;
            data.Tabs.Add(new SessionTab
            {
                Url = url,
                Title = url == tab.Url ? tab.Title : new Uri(url).Host,
                Pinned = tab.IsPinned,
                Place = tab.Place.ToString(),
                Home = home,
                Folder = tab.Folder?.Id,
                Position = _folderTree.PositionOf(tab),
            });
        }
        // Splits whose tabs were both kept, with where their rows are in the tab list.
        for (var row = 0; row < Tabs.Count; row++)
        {
            if (Tabs[row] is { IsSplitRow: true, Split: { } split }
                && savedAt.TryGetValue(split.Left, out var left) && savedAt.TryGetValue(split.Right, out var right))
                data.Splits.Add(new SessionSplit { Left = left, Right = right, Row = row, Ratio = split.Ratio });
        }
        data.Folders = _folderTree.Save();
        data.Closed = _closedTabs.TakeLast(MaxClosedTabs).ToList();
        Session.Save(data, wait);
    }

    // ───────────────────────── Tabs ─────────────────────────

    /// <param name="deferLoad">Restored tabs: don't load the page until the tab is first shown.</param>
    /// <param name="title">The title to show until the page gives its own.</param>
    /// <param name="home">For pinned tabs, the page they're pinned to, when it isn't <paramref name="uri"/>.</param>
    /// <param name="index">Where in its group (reopening a closed tab); by default, last.</param>
    /// <param name="folder">For a tab in a folder (Place Folder), the folder.</param>
    private async Task<BrowserTab> CreateTabAsync(Uri uri, TabPlace place, bool activate,
        bool deferLoad = false, string? title = null, Uri? home = null, int? index = null, TabFolder? folder = null)
    {
        if (place == TabPlace.Folder && folder is null) place = TabPlace.Pinned;
        var view = NewTabView();
        WebHost.Children.Add(view);

        // In the sidebar right away, in order, while the engine starts; it navigates once it has.
        var tab = new BrowserTab(view, place)
        {
            Title = string.IsNullOrWhiteSpace(title) ? uri.Host : title,
            HomeUri = place == TabPlace.Regular ? null : (home ?? uri),
            PendingUri = uri,
            IsUnloaded = deferLoad,
        };
        if (folder is not null && place == TabPlace.Folder)
        {
            _folderTree.Add(tab, folder, index);
            RefreshFolderRows();
        }
        else
        {
            var list = CollectionFor(place);
            if (index is { } at) list.Insert(Math.Clamp(at, 0, list.Count), tab);
            else list.Add(tab);
        }
        var starting = StartTabViewAsync(tab, load: !deferLoad);
        if (activate || _active is null) Activate(tab);
        if (deferLoad) ShowSavedIcon(tab, uri);
        await starting;
        return tab;
    }

    // Kept invisible while the engine starts so it doesn't flash over the current tab.
    private static WebView2 NewTabView() => new()
    {
        Opacity = 0,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
    };

    // Views whose engine is starting, so a tab opened meanwhile doesn't start it twice.
    private readonly HashSet<WebView2> _startingViews = new();

    /// <summary>Starts a tab's engine, and loads its page if it's to load now or is in front by then.</summary>
    private async Task StartTabViewAsync(BrowserTab tab, bool load)
    {
        var view = tab.View;
        _startingViews.Add(view);
        try
        {
            await InitWebViewAsync(view);
        }
        catch when (!AllTabs.Contains(tab) || tab.View != view)
        {
            // Closed, or unloaded, while its engine was starting.
            return;
        }
        finally
        {
            _startingViews.Remove(view);
        }
        if (tab.View != view) return;
        RoundCorners(view, 5);
        RevealWhenLoaded(view);
        view.IsHitTestVisible = true;
        WireTab(tab);
        if (load || tab == _active) LoadPending(tab);
        SaveSessionSoon();
    }

    /// <summary>Navigates a tab to where it's waiting to go, once its engine is ready.</summary>
    private static void LoadPending(BrowserTab tab)
    {
        if (tab.PendingUri is not { } uri || tab.View.CoreWebView2 is null) return;
        tab.PendingUri = null;
        tab.IsUnloaded = false;
        Navigate(tab, uri);
    }

    /// <summary>A restored tab that hasn't loaded shows its site's icon from last time.</summary>
    private static async void ShowSavedIcon(BrowserTab tab, Uri uri)
    {
        if (await History.IconAsync(uri.Authority) is { } icon && tab.Favicon is null) tab.Favicon = icon;
    }

    private async Task InitWebViewAsync(WebView2 view)
    {
        var environment = await EnvironmentAsync();
        if (_private) await view.EnsureCoreWebView2Async(environment, PrivateOptions(environment));
        else await view.EnsureCoreWebView2Async(environment);
        var core = view.CoreWebView2;
        if (_private) Extensions.AttachPrivate(core.Profile);
        else Extensions.Attach(core.Profile);
        core.DownloadStarting += (_, e) => Downloads.Track(e, _private);
        await AddInputScriptAsync(core);
    }

    /// <summary>
    /// Private pages: an InPrivate profile of their own (same engine), which keeps cookies, storage
    /// and cache only in memory, until the last private window closes.
    /// </summary>
    internal static CoreWebView2ControllerOptions PrivateOptions(CoreWebView2Environment environment)
    {
        var options = environment.CreateCoreWebView2ControllerOptions();
        options.ProfileName = "Private";
        options.IsInPrivateModeEnabled = true;
        return options;
    }

    private static async Task AddInputScriptAsync(CoreWebView2 core)
    {
        var id = await core.AddScriptToExecuteOnDocumentCreatedAsync(InputScript());
        InputScriptIds.AddOrUpdate(core, id);
    }

    /// <summary>
    /// Fades a new view in once its first page has content. Until then Chromium draws plain white,
    /// which flashes in dark mode; while the view is hidden, the themed page or card behind it shows.
    /// </summary>
    private static void RevealWhenLoaded(WebView2 view)
    {
        var core = view.CoreWebView2;
        view.Opacity = 0;
        view.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(150) };

        // The handlers stay on, doing nothing after the first: taking one off while WebView2 is
        // calling its handlers can leave it calling a freed one.
        var revealed = false;
        void Reveal()
        {
            if (revealed) return;
            revealed = true;
            view.Opacity = 1;
        }
        void OnContentLoaded(CoreWebView2 sender, CoreWebView2DOMContentLoadedEventArgs e) => Reveal();
        // Covers loads that fail or are cancelled before any document arrives.
        void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs e) => Reveal();
        core.DOMContentLoaded += OnContentLoaded;
        core.NavigationCompleted += OnNavigationCompleted;
    }

    private void OpenInNewTab(Uri uri) => _ = CreateTabAsync(uri, TabPlace.Regular, activate: true);

    private void WireTab(BrowserTab tab)
    {
        var core = tab.View.CoreWebView2;

        core.DocumentTitleChanged += (_, _) =>
        {
            tab.Title = string.IsNullOrWhiteSpace(core.DocumentTitle) ? DisplayUrl(core.Source) : core.DocumentTitle;
            if (!_private) History.SetTitle(core.Source, core.DocumentTitle);
            if (tab == _active) UpdateChrome();
        };
        if (!_private) RecordHistory(core);

        core.FaviconChanged += async (_, _) => tab.Favicon = await LoadFaviconAsync(core, remember: !_private);

        core.SourceChanged += (_, _) =>
        {
            tab.LastUrl = core.Source;
            if (tab == _active) UpdateChrome();
            SaveSessionSoon();
        };
        core.ProcessFailed += (_, e) => OnProcessFailed(tab, e);
        // The engine shows its own warning page; if you go on to the site anyway, the site info says so.
        core.ServerCertificateErrorDetected += (_, e) =>
        {
            if (Uri.TryCreate(e.RequestUri, UriKind.Absolute, out var bad)) tab.CertificateErrorHost = bad.Host;
        };
        core.HistoryChanged += (_, _) => { if (tab == _active) UpdateChrome(); };
        core.WebMessageReceived += (_, e) => OnWebMessage(e, tab);
        core.ContextMenuRequested += (_, e) => OpenLinkInNewTabItem(core, e);
        WireAudio(tab, core);
        WirePicture(tab, core);
        WireSplitFocus(tab, tab.View);

        // Links that ask for a new window (target="_blank", window.open).
        // Popups (sign-in windows and the like) stay popups. Otherwise, from a pinned tab they
        // open in Peek; from a normal tab, in a new tab.
        core.NewWindowRequested += (_, e) =>
        {
            // A page floating itself (Pip.cs), opening its window: a small window of its own, not Peek or a tab.
            if (_pipOpening == tab && e.Uri == "about:blank")
            {
                PlayerWindow.Show(e, this, 0, 0, _private);
                return;
            }
            if (PopupWindow.IsPopup(e))
            {
                PopupWindow.Show(e, this, OpenInNewTab, _private);
                return;
            }
            e.Handled = true;
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var target)) return;
            if (tab.IsPinned) OpenPeek(target, tab);
            else OpenInNewTab(target);
        };

        // Same-tab link clicks. In a pinned tab, a click that leaves the tab's site
        // is cancelled and opened in Peek instead, so the pinned tab stays where it was.
        core.NavigationStarting += (_, e) =>
        {
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var target)) return;
            if (target == tab.AppNavigation) { tab.AppNavigation = null; return; }
            if (!tab.IsPinned || !e.IsUserInitiated || e.IsRedirected) return;
            // Back/forward and reload revisit the tab's own history; they aren't link clicks.
            if (e.NavigationKind != CoreWebView2NavigationKind.NewDocument) return;
            if (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps) return;
            if (!Uri.TryCreate(core.Source, UriKind.Absolute, out var current)) return;
            if (SameSite(current, target)) return;

            e.Cancel = true;
            OpenPeek(target, tab);
        };
        // After the Peek interception, so it sees whether that cancelled the navigation.
        WireLoading(tab, core);
    }

    /// <summary>
    /// Every page a view shows goes into history: new documents, and same-page navigations
    /// (sites like YouTube change page without loading a new document). A new document's title
    /// isn't known yet when its address changes, so it's filled in as it arrives, and once more
    /// when loading finishes in case it's the same title as the page before (no change event then).
    /// </summary>
    /// <param name="stillPeek">For Peek's page: false once it has become a tab, which records its own.</param>
    private static void RecordHistory(CoreWebView2 core, Func<bool>? stillPeek = null)
    {
        core.SourceChanged += (_, e) =>
        {
            if (stillPeek?.Invoke() == false) return;
            History.Record(core.Source, e.IsNewDocument ? null : core.DocumentTitle);
        };
        core.NavigationCompleted += (_, _) =>
        {
            if (stillPeek?.Invoke() == false) return;
            History.SetTitle(core.Source, core.DocumentTitle);
        };
    }

    /// <summary>
    /// The right-click menu's "Open link in new window" opens a new tab instead (Drift has one window,
    /// and tabs in its sidebar), and says so. Left to the engine it would go wherever the page's own
    /// new-window links go: in Peek, Peek itself; from a pinned tab, Peek.
    /// </summary>
    private void OpenLinkInNewTabItem(CoreWebView2 core, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        var items = e.MenuItems;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Name != "openLinkInNewWindow") continue;
            if (!Uri.TryCreate(e.ContextMenuTarget.LinkUri, UriKind.Absolute, out var link)) return;
            var item = core.Environment.CreateContextMenuItem("Open link in new tab", null, CoreWebView2ContextMenuItemKind.Command);
            item.CustomItemSelected += (_, _) => DispatcherQueue.TryEnqueue(() => OpenInNewTab(link));
            items.RemoveAt(i);
            items.Insert(i, item);
            return;
        }
    }

    /// <summary>Navigates a tab from the app itself, so the Peek check lets it through.</summary>
    private static void Navigate(BrowserTab tab, Uri uri)
    {
        tab.AppNavigation = uri;
        tab.View.Source = uri;
    }

    /// <summary>A page that's showing loads, if it hasn't: an unloaded tab's view has no engine yet, so it starts.</summary>
    private void LoadShown(BrowserTab tab)
    {
        if (tab.View.CoreWebView2 is null && !_startingViews.Contains(tab.View)) _ = StartTabViewAsync(tab, load: true);
        else LoadPending(tab);
    }

    private void Activate(BrowserTab tab)
    {
        // A split's row isn't a tab of its own: its page you were last on is (Split.cs).
        if (tab is { IsSplitRow: true, Split: { } rowSplit }) tab = FrontOf(rowSplit);
        var left = _active;
        _active = tab;
        // Its page, and its split partner's beside it (Split.cs); every other page is hidden.
        ShowPages(tab);
        // Still showing (the other half of a split), it hasn't been left.
        if (left is not null && left != tab && left.View.Visibility != Visibility.Visible) _ = FloatOnLeaveAsync(left);
        if (tab == _pipTab)
        {
            // Back in front, drawn as usual, as its video comes back from the player.
            KeepDrawing(tab, false);
            _ = EndPipAsync(tab);
        }
        NoteRecentTab(tab);
        PictureActiveSoon();
        foreach (var page in _shownPages) LoadShown(page);
        SaveSessionSoon();

        // A tab in a closed folder shows under its header while it's in front, and after.
        NoteFolderTabShown(tab);
        _syncingSelection = true;
        // A split is its own row (as in Arc): that's the one marked, wherever its pages are kept.
        EssentialList.SelectedItem = tab is { Place: TabPlace.Essential, Split: null } ? tab : null;
        PinnedList.SelectedItem = tab is { Place: TabPlace.Pinned, Split: null } ? tab : null;
        TabList.SelectedItem = tab.Split?.Row ?? (tab.Place == TabPlace.Regular ? tab : null);
        _syncingSelection = false;

        // Peek belongs to the tab that opened it: it steps aside while another tab is showing
        // and comes back, without animating, when its tab does.
        if (_peekView is not null && _closingPeekView is null)
        {
            if (tab == _peekOwner) ShowPeekLayer(animate: false);
            else HidePeekLayer();
        }

        UpdateChrome();
    }

    /// <summary>
    /// Ctrl+W and a regular tab's ×. Essentials and pinned tabs aren't closed by it, only unloaded:
    /// they stay in the sidebar, back on their own pages.
    /// </summary>
    private void CloseTab(BrowserTab tab)
    {
        switch (TabCloses.First(hasPeek: tab == _peekOwner && _peekView is not null, tab.IsPinned))
        {
            case TabClose.ClosePeek: ClosePeek(animate: true); break;
            case TabClose.Unload: UnloadTab(tab); break;
            default: RemoveTab(tab); break;
        }
    }

    /// <summary>
    /// A tab's Peek goes first: "−", "×" (or Ctrl+W) on a tab with a page in Peek closes that page,
    /// and only the next press unloads or closes the tab.
    /// </summary>
    private bool ClosePeekOf(BrowserTab tab)
    {
        if (tab != _peekOwner || _peekView is null) return false;
        ClosePeek(animate: true);
        return true;
    }

    /// <summary>
    /// Frees an Essential's or pinned tab's page: its view (the page, its memory, its history) is
    /// closed and an empty one takes its place, waiting on the tab's own page until the tab is next
    /// opened. If it was in front, the tab you were on before comes back.
    /// </summary>
    private void UnloadTab(BrowserTab tab)
    {
        if (tab.IsUnloaded) return;
        // Its page goes, so it can't stay beside another (Split.cs).
        LeaveSplit(tab);
        if (tab == _peekOwner) ClosePeek(animate: false);
        tab.Load.Reset();

        if (tab == _active)
        {
            var next = _recentTabs.FirstOrDefault(t => t != tab && !t.IsUnloaded)
                ?? AllTabs.FirstOrDefault(t => t != tab);
            if (next is not null) Activate(next);
            else _active = null;
        }

        var old = tab.View;
        var view = NewTabView();
        WebHost.Children.Add(view);
        WebHost.Children.Remove(old);
        try { old.Close(); } catch { }
        tab.View = view;

        var home = tab.HomeUri ?? (Uri.TryCreate(tab.Url, UriKind.Absolute, out var here) ? here : null);
        tab.PendingUri = home;
        tab.Crash = TabCrash.None;
        tab.IsUnloaded = true;
        // Its page and its sound are gone (MediaControls.cs).
        tab.IsPlayingAudio = tab.IsMuted = false;
        tab.AudioSince = null;
        UpdateMediaBar();
        if (home is not null)
        {
            var saved = History.All.FirstOrDefault(h => string.Equals(h.Url.TrimEnd('/'), home.ToString().TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
            tab.Title = string.IsNullOrWhiteSpace(saved?.Title) ? home.Host : saved!.Title;
            ShowSavedIcon(tab, home);
        }
        if (_active is null) UpdateChrome();
        // Unloaded, it no longer stays under a closed folder.
        if (tab.Place == TabPlace.Folder)
        {
            _folderTree.Unloaded(tab);
            RefreshFolderRows();
        }
        SaveSessionSoon();
    }

    /// <summary>Takes a tab out of the sidebar altogether, whichever group it's in.</summary>
    private void RemoveTab(BrowserTab tab)
    {
        if (tab.Split is { } split)
        {
            // Out of its split first; the page beside it stays in front, on its own.
            var partner = split.Other(tab);
            SeparateTabs(split);
            if (_active == tab) Activate(partner);
        }
        if (tab == _peekOwner) ClosePeek(animate: false);
        BrowserTab? neighbour;
        if (tab.Place == TabPlace.Folder)
        {
            var index = tab.Folder?.Children.IndexOf(tab) ?? -1;
            if (index < 0) return;
            RememberClosed(tab, index);
            // The next tab showing in the folders, else the one before.
            var showing = FolderRows.OfType<BrowserTab>().ToList();
            var row = showing.IndexOf(tab);
            DetachTab(tab);
            showing.Remove(tab);
            neighbour = row >= 0 && showing.Count > 0 ? showing[Math.Min(row, showing.Count - 1)] : null;
            RefreshFolderRows();
        }
        else
        {
            var list = CollectionFor(tab.Place);
            var index = list.IndexOf(tab);
            if (index < 0) return;
            RememberClosed(tab, index);

            _syncingSelection = true;
            list.RemoveAt(index);
            _syncingSelection = false;
            neighbour = list.Count > 0 ? list[Math.Min(index, list.Count - 1)] : null;
        }

        (tab.View.Parent as Panel)?.Children.Remove(tab.View);
        try { tab.View.Close(); } catch { }
        _recentTabs.Remove(tab);
        _shownPages.Remove(tab);
        UpdateMediaBar();
        SaveSessionSoon();

        if (_active == tab)
        {
            _active = null;
            // The neighbour in the same list, else the tab you were on before.
            var next = neighbour ?? _recentTabs.FirstOrDefault() ?? AllTabs.FirstOrDefault();
            if (next is not null) Activate(next);
            else UpdateChrome();
        }
    }

    /// <summary>A pinned row's hover button: "−" unloads the tab; on an unloaded tab, "×" removes it.</summary>
    private void PinnedAction_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not BrowserTab tab) return;
        if (tab.IsUnloaded) RemoveTab(tab);
        else if (!ClosePeekOf(tab)) UnloadTab(tab);
    }

    /// <summary>
    /// Moves a tab between Essentials, pinned tabs and regular tabs. A tab that becomes an Essential or
    /// pinned belongs to the page it's on now; one moved between those two keeps its page.
    /// </summary>
    /// <param name="index">Where in its new group it goes (a drop); by default, regular tabs go first
    /// and the others last.</param>
    private void MoveTab(BrowserTab tab, TabPlace place, int? index = null)
    {
        if (tab.Place == place || place == TabPlace.Folder) return;
        var fromFolder = tab.Place == TabPlace.Folder;
        _syncingSelection = true;
        DetachTab(tab);
        if (place == TabPlace.Regular) tab.HomeUri = null;
        else if (!tab.IsPinned) tab.HomeUri = Uri.TryCreate(tab.Url, UriKind.Absolute, out var here) ? here : null;
        tab.Place = place;
        var list = CollectionFor(place);
        list.Insert(Math.Clamp(index ?? (place == TabPlace.Regular ? 0 : list.Count), 0, list.Count), tab);
        _syncingSelection = false;
        if (fromFolder) RefreshFolderRows();
        SaveSessionSoon();

        if (tab == _active) Activate(tab);
    }

    // ───────────────────────── Peek ─────────────────────────

    /// <summary>Peek is open and showing: it belongs to the active tab and isn't on its way out.</summary>
    private bool IsPeekShown => _peekView is not null && _active == _peekOwner;

    private async void OpenPeek(Uri uri, BrowserTab owner)
    {
        switch (PeekLink.Start(peekOpen: _peekView is not null, sameOwner: _peekOwner == owner))
        {
            case PeekStart.Navigate:
                _peekPending = uri;
                // While the engine is still starting, the navigation below picks up _peekPending.
                if (_peekView!.CoreWebView2 is not null) _peekView.Source = uri;
                return;
            case PeekStart.Replace:
                // Another tab's Peek, hidden with it: taking it over left this one out of sight too.
                ClosePeek(animate: false);
                break;
        }
        _peekPending = uri;
        _peekOwner = owner;
        owner.HasPeek = true;

        // A previous Peek may still be fading out; finish it now rather than fight its animation.
        FinishPeekExit();
        AimPeekCard(owner, TakeLinkPress(owner));
        ShowPeekLayer(animate: true);

        // Hidden until its page has content (RevealWhenLoaded): the card grows in its themed colour.
        var view = new WebView2 { Opacity = 0 };
        _peekView = view;
        _peekZoom = 1;
        PeekWebHost.Children.Add(view);

        try
        {
            await InitWebViewAsync(view);
        }
        catch
        {
            if (_peekView == view) ClosePeek(animate: false);
            return;
        }

        // Dismissed while the engine was starting.
        if (_peekView != view)
        {
            try { view.Close(); } catch { }
            return;
        }

        RoundCorners(view, 7);
        RevealWhenLoaded(view);
        var core = view.CoreWebView2;
        core.FaviconChanged += async (_, _) =>
        {
            var icon = await LoadFaviconAsync(core, remember: !_private);
            if (_peekView == view && _peekOwner is { } peekOwner) peekOwner.PeekFavicon = icon;
        };
        // These stand down once the page has become a tab (PromotePeekToTab), and WireTab's take over.
        core.WebMessageReceived += (_, e) => { if (_peekView == view) OnWebMessage(e, source: null); };
        if (!_private)
        {
            core.DocumentTitleChanged += (_, _) => { if (_peekView == view) History.SetTitle(core.Source, core.DocumentTitle); };
            RecordHistory(core, stillPeek: () => _peekView == view);
        }
        // A Peek whose page dies just closes; the link can be opened again.
        core.ProcessFailed += (_, e) =>
        {
            if (_peekView != view) return;
            if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.BrowserProcessExited)
                ClosePeek(animate: false);
            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited) EngineStopped();
        };
        core.ContextMenuRequested += (_, e) =>
        {
            if (_peekView == view) OpenLinkInNewTabItem(core, e);
        };
        // New-window links inside Peek stay inside Peek; popups stay popups. (Not the right-click
        // menu's "Open link in new tab", which opens one.)
        core.NewWindowRequested += (_, e) =>
        {
            if (_peekView != view) return;
            if (PopupWindow.IsPopup(e))
            {
                PopupWindow.Show(e, this, OpenInNewTab, _private);
                return;
            }
            e.Handled = true;
            core.Navigate(e.Uri);
        };

        view.Source = _peekPending ?? uri;
        if (IsPeekShown) view.Focus(FocusState.Programmatic);
    }

    /// <summary>The owner's last link press, if it was recent enough to be what opened this Peek.</summary>
    private static Point? TakeLinkPress(BrowserTab owner)
    {
        var press = owner.LinkPress;
        owner.LinkPress = null;
        // Press to navigation is a click's length; anything older belongs to some other link.
        return Environment.TickCount64 - owner.LinkPressTime < 1500 ? press : null;
    }

    /// <summary>
    /// Sets where the card grows from and shrinks back to. From a link, it scales about the press
    /// point from a small card (PeekGrowFrom): always towards its final place, and visibly out of what was clicked.
    /// With no link (a window a script opened), it rises from just below centre like a modal.
    /// </summary>
    private void AimPeekCard(BrowserTab owner, Point? press)
    {
        // The card frame and the page share the page column; the card is inset by its margin.
        // The page isn't transformed yet, since Peek is only now opening.
        var page = PageArea.TransformToVisual(Root).TransformPoint(new Point(0, 0));
        var cardLeft = page.X - PageArea.Margin.Left + PeekCardFrame.Margin.Left;
        var cardTop = page.Y - PageArea.Margin.Top + PeekCardFrame.Margin.Top;

        if (press is { } point && owner == _active)
        {
            var inRoot = owner.View.TransformToVisual(Root).TransformPoint(point);
            PeekCardFrame.CenterPoint = new Vector3((float)(inRoot.X - cardLeft), (float)(inRoot.Y - cardTop), 0);
            (_cardFromScale, _cardFromY, _cardToScale, _cardToY) = (PeekGrowFrom, 0, PeekShrinkTo, 0);
        }
        else
        {
            // Collapsed until now, so the card's size comes from the page it sits over.
            var margin = PeekCardFrame.Margin;
            var width = PageArea.ActualWidth + PageArea.Margin.Left + PageArea.Margin.Right - margin.Left - margin.Right;
            var height = PageArea.ActualHeight + PageArea.Margin.Top + PageArea.Margin.Bottom - margin.Top - margin.Bottom;
            PeekCardFrame.CenterPoint = new Vector3((float)width / 2, (float)height / 2, 0);
            (_cardFromScale, _cardFromY, _cardToScale, _cardToY) = (0.96f, 16, 0.98f, 8);
        }
    }

    /// <summary>
    /// The card springs out of the link with a little bounce and settles in about half a second;
    /// the buttons beside it pop in on a bouncier spring once it's mostly there.
    /// </summary>
    // Peek motion, tuned by eye. The storyboards in MainWindow.xaml are timed to match.
    private const float PeekGrowFrom = 0.15f;     // card's scale on its first frame, about the link
    private const double PeekGrowSeconds = 0.32;
    private const double PeekGrowBounce = 0.25;   // overshoots ~3% of the way, then settles
    private const double PeekGrowKick = 0.6;     // leaves the link at speed instead of easing out of it
    private const float PeekShrinkTo = 0.3f;      // where it heads back to on close
    private const double PeekShrinkSeconds = 0.22;
    private const double PeekShrinkBounce = 0.25; // dips just past its end before it fades
    private const int PeekControlsDelayMs = 120;  // buttons pop in once the card is mostly there

    private void SpringPeekIn()
    {
        // Starting points go in as `from`, never as the elements' own Scale: XAML draws them at that,
        // and a card drawn at half size then stretched up has soft borders the whole way.
        Spring.To(PeekCardFrame, "Scale", Vector3.One, duration: PeekGrowSeconds, bounce: PeekGrowBounce,
            from: new Vector3(_cardFromScale, _cardFromScale, 1), kick: PeekGrowKick);
        Spring.To(PeekCardFrame, "Translation", Vector3.Zero, duration: PeekGrowSeconds, bounce: PeekGrowBounce,
            from: new Vector3(0, _cardFromY, 0), kick: PeekGrowKick);

        // About the middle of the two stacked buttons.
        PeekControls.CenterPoint = new Vector3(
            (float)PeekCloseButton.Width / 2, (float)(PeekCloseButton.Height + PeekControls.Spacing / 2), 0);
        Spring.To(PeekControls, "Scale", Vector3.One, duration: 0.3, bounce: 0.35,
            delay: TimeSpan.FromMilliseconds(PeekControlsDelayMs), from: new Vector3(0.7f, 0.7f, 1));
    }

    /// <summary>Back toward the link on a spring, with a little bounce as it gets there.</summary>
    private void SpringPeekOut()
    {
        Spring.To(PeekCardFrame, "Scale", new Vector3(_cardToScale, _cardToScale, 1), duration: PeekShrinkSeconds, bounce: PeekShrinkBounce);
        Spring.To(PeekCardFrame, "Translation", new Vector3(0, _cardToY, 0), duration: PeekShrinkSeconds, bounce: PeekShrinkBounce);
        Spring.To(PeekControls, "Scale", new Vector3(0.7f, 0.7f, 1), duration: 0.2, bounce: 0);
    }

    /// <summary>The card and buttons at rest, with no spring running.</summary>
    private void SettlePeekMotion()
    {
        Spring.Jump(PeekCardFrame, "Scale", Vector3.One);
        Spring.Jump(PeekCardFrame, "Translation", Vector3.Zero);
        Spring.Jump(PeekControls, "Scale", Vector3.One);
    }

    /// <param name="animate">
    /// True whenever the user dismisses Peek, by pointer or keyboard (Esc, Ctrl+W): the exit is a
    /// quick spring back toward the link, and an instant close next to animated ones read as abrupt.
    /// False when Peek goes for another reason: opening it as a tab, or its tab closing.
    /// </param>
    private void ClosePeek(bool animate)
    {
        if (_peekView is null) return;

        var view = _peekView;
        var wasShown = IsPeekShown;
        _peekView = null;
        _peekPending = null;
        if (_peekOwner is not null) _peekOwner.HasPeek = false;
        _peekOwner = null;

        if (animate && wasShown)
        {
            // The card keeps its page while it fades; FinishPeekExit() removes it afterwards.
            _closingPeekView = view;
            if (_uiSettings.AnimationsEnabled)
            {
                PeekExitAnimation.Begin();
                SpringPeekOut();
            }
            else PeekFadeOutAnimation.Begin();
        }
        else
        {
            RemovePeekView(view);
            HidePeekLayer();
        }
        _active?.View.Focus(FocusState.Programmatic);
    }

    private void FinishPeekExit()
    {
        if (_closingPeekView is null) return;
        RemovePeekView(_closingPeekView);
        _closingPeekView = null;
        HidePeekLayer();
    }

    private void RemovePeekView(WebView2 view)
    {
        PeekWebHost.Children.Remove(view);
        try { view.Close(); } catch { }
    }

    /// <summary>
    /// Picks the page's Peek scale and shift so all four edges land on whole device pixels.
    /// A flat 0.97 about the centre leaves edges between pixels and the 1px border goes soft;
    /// the page's laid-out size can also be fractional (at rest XAML snaps it, under a transform
    /// it doesn't). So: scale from the corner to a whole-pixel size, shifted by a whole-pixel offset.
    /// </summary>
    private void SnapPageShrink()
    {
        var pixels = PageArea.XamlRoot?.RasterizationScale ?? 1;
        static (double Scale, double Shift) Fit(double size, double pixels)
        {
            // The element is drawn at its layout-rounded size, which can be half a pixel
            // larger than ActualWidth/ActualHeight report.
            var sizePx = Math.Round(size * pixels, MidpointRounding.AwayFromZero);
            if (sizePx < 1) return (1, 0);
            var shrunkPx = Math.Round(sizePx * 0.97);
            var shiftPx = Math.Round((sizePx - shrunkPx) / 2);
            return (shrunkPx / sizePx, shiftPx / pixels);
        }
        (PageShrinkX.Value, PageShiftX.Value) = Fit(PageArea.ActualWidth, pixels);
        (PageShrinkY.Value, PageShiftY.Value) = Fit(PageArea.ActualHeight, pixels);

        // The frame that draws the shrunk page's border and scrim sits exactly on those bounds.
        var margin = PageArea.Margin;
        PeekPageFrame.Margin = new Thickness(margin.Left + PageShiftX.Value, margin.Top + PageShiftY.Value, 0, 0);
        PeekPageFrame.Width = PageArea.ActualWidth * PageShrinkX.Value;
        PeekPageFrame.Height = PageArea.ActualHeight * PageShrinkY.Value;
    }

    private void ShowPeekLayer(bool animate)
    {
        PeekLayer.Visibility = Visibility.Visible;
        PeekPageFrame.Visibility = Visibility.Visible;
        // The frame draws the border while the page is shrunk; the page's own would be soft.
        // This local value hides the style's border until HidePeekLayer clears it.
        MainCard.BorderBrush = SplitCard.BorderBrush = null;
        SnapPageShrink();
        if (!animate)
        {
            // Switching back to the tab that owns Peek: no motion, just the open state.
            PeekEnterAnimation.Begin();
            PeekEnterAnimation.SkipToFill();
            SettlePeekMotion();
        }
        else if (_uiSettings.AnimationsEnabled)
        {
            // Measured now, so the springs know the card's real size and cut their tails on time.
            PeekLayer.UpdateLayout();
            PeekEnterAnimation.Begin();
            SpringPeekIn();
        }
        else
        {
            PeekFadeInAnimation.Begin();
            SettlePeekMotion();
        }
    }

    /// <summary>Instantly back to the closed state: page full size, layer hidden.</summary>
    private void HidePeekLayer()
    {
        PeekLayer.Visibility = Visibility.Collapsed;
        PeekPageFrame.Visibility = Visibility.Collapsed;
        // Back to the style's border, which follows light/dark mode as it changes.
        MainCard.ClearValue(Border.BorderBrushProperty);
        SplitCard.ClearValue(Border.BorderBrushProperty);
        // Stopping returns every animated value (page scale, opacity) to its base.
        SettlePeekMotion();
        PeekEnterAnimation.Stop();
        PeekExitAnimation.Stop();
        PeekFadeInAnimation.Stop();
        PeekFadeOutAnimation.Stop();
    }

    /// <summary>
    /// Open as tab (PeekExpand). A picture of the page stands in for it, held still, while the page
    /// moves into its new tab and lays itself out at full width out of sight; the card's edges spring
    /// out to the page's, on the page's own background colour; then the picture fades into the page.
    /// Nothing reloads; scroll, video, a half-filled form all carry on.
    /// </summary>
    private async void PromotePeekToTab()
    {
        if (_peekView is not { CoreWebView2: { } core } view || _peekExpanding) return;
        _peekExpanding = true;
        var owner = _peekOwner;
        var favicon = owner?.PeekFavicon;
        // No longer Peek's: its handlers stand down, and ClosePeek and the rest leave it be.
        _peekView = null;
        _peekPending = null;
        if (owner is not null) owner.HasPeek = false;
        _peekOwner = null;

        var floating = PeekCardFrame.Margin;
        var page = PageArea.Margin;
        var inset = PeekExpand.Inset(
            new Edges(floating.Left, floating.Top, floating.Right, floating.Bottom),
            new Edges(page.Left, page.Top, page.Right, page.Bottom));
        var cardVisual = ElementCompositionPreview.GetElementVisual(PeekCard);
        var revealed = view.Opacity > 0;
        var picture = revealed
            ? PeekFreeze.IsFresh(_freezeTakenAt, Environment.TickCount64) && _freezePicture is { } early ? early : SnapshotAsync(core)
            : Task.FromResult<byte[]?>(null);
        _freezePicture = null;
        _freezeTakenAt = null;
        BrowserTab? tab = null;
        try
        {
            SettlePeekMotion();
            // 1. The picture over the page, exactly where it is: nothing on screen changes.
            var held = revealed && await Deadline.Within(FreezeAsync(picture), TimeSpan.FromMilliseconds(400), fallback: false);

            // 2. The page into its tab, out of sight: see-through, so it draws but doesn't show, and the
            //    tab in front stays in front until the end. It lays itself out at full width meanwhile.
            view.OpacityTransition = null;
            view.Opacity = 0;
            tab = AdoptAsTab(view, core, favicon, activate: false);
            WebHost.UpdateLayout();
            var laidOut = Deadline.Within(LaidOutAsync(core, PeekExpand.CssWidth(view.ActualWidth, _peekZoom)),
                TimeSpan.FromMilliseconds(700), fallback: null);
            // 3. The card's edges spring out to the page's; the picture stays put inside it.
            var grown = _uiSettings.AnimationsEnabled ? GrowCard(cardVisual, inset, held) : Task.CompletedTask;
            await Task.WhenAll(grown, laidOut);

            // 4. The tab in front, drawn, under the card; then the card (and the picture) fades into it.
            Activate(tab);
            view.Opacity = 1;
            view.Focus(FocusState.Programmatic);
            PeekEnterAnimation.Stop();
            PeekFadeInAnimation.Stop();
            PeekPageFrame.Visibility = Visibility.Collapsed;
            await NextFrameAsync();
            await NextFrameAsync();
            if (_uiSettings.AnimationsEnabled)
            {
                PeekLayer.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromSeconds(PeekExpand.FadeSeconds) };
                PeekLayer.Opacity = 0;
                await Task.Delay(TimeSpan.FromSeconds(PeekExpand.FadeSeconds + 0.02));
            }
        }
        catch
        {
            // It couldn't become a tab: it goes, rather than staying stuck over the page.
            if (tab is null)
            {
                (view.Parent as Panel)?.Children.Remove(view);
                view.Close();
            }
            else
            {
                view.Opacity = 1;
                Activate(tab);
            }
        }
        finally
        {
            _peekExpanding = false;
            HidePeekLayer();
            PeekLayer.OpacityTransition = null;
            PeekLayer.Opacity = 1;
            cardVisual.Clip = null;
            cardVisual.StopAnimation("Scale");
            cardVisual.Scale = Vector3.One;
            PeekCardFrame.Margin = floating;
            PeekStill.Visibility = Visibility.Collapsed;
            PeekStillBrush.ImageSource = null;
            PeekCardTint.Background = null;
            PeekControls.Visibility = Visibility.Visible;
            PeekCardShadow.Visibility = Visibility.Visible;
            if (!revealed) view.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(150) };
        }
    }

    private bool _peekExpanding;

    // The picture taken while the pointer's on "Open as tab" (PeekFreeze), and when.
    private Task<byte[]?>? _freezePicture;
    private long? _freezeTakenAt;

    private void PeekToTab_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (_peekExpanding || _peekView is not { CoreWebView2: { } core } view || view.Opacity <= 0) return;
        if (PeekFreeze.IsFresh(_freezeTakenAt, Environment.TickCount64)) return;
        _freezePicture = SnapshotAsync(core);
        _freezeTakenAt = Environment.TickCount64;
    }

    /// <summary>
    /// The card takes the page's size at once and is cut back to where it was; the cut springs out to the
    /// page's edges and its corners to the page's. The picture (or, without one, the page's colour) keeps
    /// its place inside.
    /// </summary>
    private Task GrowCard(Visual cardVisual, Edges inset, bool held)
    {
        var done = new TaskCompletionSource();
        PeekControls.Visibility = Visibility.Collapsed;
        // It'd be the page's size at once, around a card that's still growing.
        PeekCardShadow.Visibility = Visibility.Collapsed;
        PeekCardFrame.Margin = PageArea.Margin;
        if (held) PeekStill.Margin = new Thickness(inset.Left, inset.Top, inset.Right, inset.Bottom);
        PeekCardFrame.UpdateLayout();
        float width = (float)PageArea.ActualWidth, height = (float)PageArea.ActualHeight;

        var compositor = cardVisual.Compositor;
        var shape = compositor.CreateRoundedRectangleGeometry();
        shape.CornerRadius = new Vector2(8);
        shape.Offset = new Vector2((float)inset.Left, (float)inset.Top);
        shape.Size = new Vector2(width - (float)(inset.Left + inset.Right), height - (float)(inset.Top + inset.Bottom));
        cardVisual.Clip = compositor.CreateGeometricClip(shape);

        var progress = compositor.CreatePropertySet();
        progress.InsertScalar("T", 0);
        ExpressionAnimation Bind(string expression)
        {
            var animation = compositor.CreateExpressionAnimation(expression);
            animation.SetReferenceParameter("p", progress);
            animation.SetScalarParameter("l", (float)inset.Left);
            animation.SetScalarParameter("t", (float)inset.Top);
            animation.SetScalarParameter("r", (float)inset.Right);
            animation.SetScalarParameter("b", (float)inset.Bottom);
            animation.SetScalarParameter("w", width);
            animation.SetScalarParameter("h", height);
            return animation;
        }
        shape.StartAnimation("Offset", Bind("Vector2(l * (1 - p.T), t * (1 - p.T))"));
        shape.StartAnimation("Size", Bind("Vector2(w - (l + r) * (1 - p.T), h - (t + b) * (1 - p.T))"));
        // From the card's rounding to the page's.
        shape.StartAnimation("CornerRadius", Bind("Vector2(8 - 2 * Min(p.T, 1), 8 - 2 * Min(p.T, 1))"));

        // Past the page's edges (T over 1) the clip has nothing more to show, so the card swells a touch
        // instead, each edge out by its own inset × the overshoot (PeekExpand.Bulge), and back.
        var bulge = PeekExpand.Bulge(inset, width, height);
        cardVisual.CenterPoint = new Vector3((float)bulge.CenterX, (float)bulge.CenterY, 0);
        var swell = Bind("Vector3(1 + Max(p.T - 1, 0) * x, 1 + Max(p.T - 1, 0) * y, 1)");
        swell.SetScalarParameter("x", (float)bulge.PerX);
        swell.SetScalarParameter("y", (float)bulge.PerY);
        cardVisual.StartAnimation("Scale", swell);

        // The same spring as Peek opening: it leaves at once, overshoots a little and settles.
        var grow = compositor.CreateSpringScalarAnimation();
        grow.InitialValue = 0;
        grow.FinalValue = 1;
        grow.InitialVelocity = (float)(PeekExpand.Kick * 2 * Math.PI / PeekExpand.Seconds);
        grow.DampingRatio = (float)(1 - PeekExpand.Bounce);
        // The compositor's Period is 1/ω (see Spring).
        grow.Period = TimeSpan.FromSeconds(PeekExpand.Seconds / (2 * Math.PI));
        progress.StartAnimation("T", grow);

        // Done once less than half a pixel of motion is left (a spring's tail crawls on for a while):
        // then exactly at the page's edges.
        var pixels = PageArea.XamlRoot?.RasterizationScale ?? 1;
        var travel = Math.Max(Math.Max(inset.Left, inset.Right), Math.Max(inset.Top, inset.Bottom)) * pixels;
        _ = Task.Delay(TimeSpan.FromSeconds(PeekExpand.SettleSeconds(travel))).ContinueWith(_ =>
        {
            progress.StopAnimation("T");
            progress.InsertScalar("T", 1);
            done.TrySetResult();
        }, TaskScheduler.FromCurrentSynchronizationContext());
        return done.Task;
    }

    /// <summary>Once the page is laid out at <paramref name="width"/> CSS pixels and has drawn a frame like that.</summary>
    private static Task<JsonElement?> LaidOutAsync(CoreWebView2 core, int width) => EvaluateAsync(core, $$"""
        (async () => {
          const frame = () => new Promise((done) => requestAnimationFrame(done));
          const start = performance.now();
          while (Math.abs(innerWidth - {{width}}) > 2 && performance.now() - start < 650) await frame();
          await frame(); await frame();
          return innerWidth;
        })()
        """, gesture: false);

    /// <summary>
    /// A picture of the page as it's drawn now (PNG), or null. Through DevTools: CapturePreviewAsync
    /// blanks the page for a few frames while it works.
    /// </summary>
    private static async Task<byte[]?> SnapshotAsync(CoreWebView2 core)
    {
        try
        {
            var reply = await core.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", """{"format":"png"}""");
            return System.Convert.FromBase64String(JsonDocument.Parse(reply).RootElement.GetProperty("data").GetString()!);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The picture over the Peek's page, decoded and on screen before this returns true, with the card
    /// behind it in the page's background colour. False if there's no picture.
    /// </summary>
    private async Task<bool> FreezeAsync(Task<byte[]?> picture)
    {
        if (await picture is not { } png) return false;
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        using (var writer = new Windows.Storage.Streams.DataWriter(stream))
        {
            writer.WriteBytes(png);
            await writer.StoreAsync();
            writer.DetachStream();
        }
        stream.Seek(0);
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        if (await BackgroundOfAsync(decoder) is { } color) PeekCardTint.Background = new SolidColorBrush(color);
        // Decoded here, all of it, before it's shown: an ImageBrush given a BitmapImage decodes it only
        // once it's drawn, and the card showed empty for a moment while it did.
        var bitmap = await decoder.GetSoftwareBitmapAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
            Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
        var source = new SoftwareBitmapSource();
        await source.SetBitmapAsync(bitmap);
        PeekStill.Margin = new Thickness(0);
        PeekStillBrush.ImageSource = source;
        PeekStill.Visibility = Visibility.Visible;
        // Set isn't drawn: two frames on, it's on screen.
        await NextFrameAsync();
        await NextFrameAsync();
        return true;
    }

    /// <summary>The colour along a picture's edges (PageColor): the page's background.</summary>
    private static async Task<Windows.UI.Color?> BackgroundOfAsync(Windows.Graphics.Imaging.BitmapDecoder decoder)
    {
        try
        {
            var samples = new List<uint>();
            foreach (var (x, y) in PageColor.SamplePoints((int)decoder.PixelWidth, (int)decoder.PixelHeight))
            {
                if (x < 0 || y < 0) continue;
                // Just that pixel decoded, not the whole picture.
                var transform = new Windows.Graphics.Imaging.BitmapTransform
                {
                    Bounds = new Windows.Graphics.Imaging.BitmapBounds { X = (uint)x, Y = (uint)y, Width = 1, Height = 1 },
                };
                var pixel = (await decoder.GetPixelDataAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                    Windows.Graphics.Imaging.BitmapAlphaMode.Ignore, transform,
                    Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation,
                    Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage)).DetachPixelData();
                samples.Add(0xFF000000u | ((uint)pixel[2] << 16) | ((uint)pixel[1] << 8) | pixel[0]);
            }
            if (PageColor.Pick(samples) is not { } argb) return null;
            return Windows.UI.Color.FromArgb(0xFF, (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The next time XAML draws a frame.</summary>
    private static Task NextFrameAsync()
    {
        var drawn = new TaskCompletionSource();
        void OnRendering(object? sender, object e)
        {
            CompositionTarget.Rendering -= OnRendering;
            drawn.TrySetResult();
        }
        CompositionTarget.Rendering += OnRendering;
        return drawn.Task;
    }

    /// <summary>A Peek's page, loaded as it is, becomes a new tab (in front, unless told otherwise).</summary>
    private BrowserTab AdoptAsTab(WebView2 view, CoreWebView2 core, BitmapImage? favicon, bool activate = true)
    {
        // Not hidden on the way: a hidden view drops what it's drawn and comes back blank for a moment.
        PeekWebHost.Children.Remove(view);
        WebHost.Children.Add(view);
        SetCornerRadius(view, 5);
        var tab = new BrowserTab(view, TabPlace.Regular)
        {
            Title = string.IsNullOrWhiteSpace(core.DocumentTitle) ? DisplayUrl(core.Source) : core.DocumentTitle,
            LastUrl = core.Source,
            Favicon = favicon,
            Zoom = _peekZoom,
        };
        CollectionFor(TabPlace.Regular).Add(tab);
        view.IsHitTestVisible = true;
        WireTab(tab);
        if (activate) Activate(tab);
        SaveSessionSoon();
        return tab;
    }

    // ───────────────────────── Address bar ─────────────────────────

    private void UpdateChrome()
    {
        var core = _active?.View.CoreWebView2;

        BackButton.IsEnabled = core?.CanGoBack ?? false;
        ForwardButton.IsEnabled = core?.CanGoForward ?? false;
        ReloadButton.IsEnabled = core is not null;
        ShowLoadButton();

        var address = DisplayUrl(core?.Source);
        AddressText.Text = address.Length > 0 ? address : "Search or Enter URL";
        if (address.Length > 0) AddressText.ClearValue(TextBlock.ForegroundProperty);
        else AddressText.Foreground = (Brush)Application.Current.Resources["DriftSecondaryTextBrush"];

        EmptyText.Visibility = _active is null ? Visibility.Visible : Visibility.Collapsed;
        var app = _private ? "Drift Private" : "Drift";
        Title = _active is null ? app : $"{_active.Title} – {app}";
        ShowSiteSecurity();
        ShowZoomChip();
        ShowCrash();
    }

    // ───────────────────────── Input ─────────────────────────

    /// <param name="source">The tab that sent the message; null for Peek, which records no link presses.</param>
    private void OnWebMessage(CoreWebView2WebMessageReceivedEventArgs e, BrowserTab? source)
    {
        string message;
        try { message = e.TryGetWebMessageAsString(); }
        catch { return; }

        // Anything without the token was posted by the page itself, not by real input.
        var prefix = ShortcutToken + ":";
        if (!message.StartsWith(prefix, StringComparison.Ordinal)) return;

        var command = message[prefix.Length..];
        if (command.StartsWith("link:", StringComparison.Ordinal))
        {
            if (source is not null) RecordLinkPress(source, command["link:".Length..]);
            return;
        }

        if (command.StartsWith("zoom:", StringComparison.Ordinal))
        {
            OnZoomMessage(source, command["zoom:".Length..]);
            return;
        }

        if (command.StartsWith("pip:", StringComparison.Ordinal))
        {
            if (source is not null) OnPipMessage(source, command["pip:".Length..]);
            return;
        }

        // Alt+click on a link: into Peek, for the tab it was clicked in (in Peek itself, Peek goes there).
        if (command.StartsWith("peek:", StringComparison.Ordinal))
        {
            if (PeekLink.Parse(command["peek:".Length..]) is { } link && (source ?? _peekOwner) is { } owner)
                OpenPeek(link, owner);
            return;
        }

        if (command == "escape")
        {
            if (CancelCycle()) { }
            else if (_commandBarOpen) CloseCommandBar();
            else if (IsPeekShown) ClosePeek(animate: true);
            return;
        }

        // "key:code,ctrl,shift,alt"
        if (!command.StartsWith("key:", StringComparison.Ordinal)) return;
        var parts = command["key:".Length..].Split(',');
        if (parts.Length != 4 || !int.TryParse(parts[0], out var code)) return;
        var pressed = new Shortcut((VirtualKey)code, parts[1] == "1", parts[2] == "1", parts[3] == "1");
        if (Settings.ActionFor(pressed) is { } action) RunAction(action.Id);
        else if (TabNumber(pressed) is { } number) SelectTabNumber(number);
    }

    /// <summary>What each shortcut does (Settings.Actions), from the app or a page.</summary>
    private void RunAction(string id)
    {
        switch (id)
        {
            case "newtab":
                OpenCommandBar(newTab: true, animate: false);
                break;
            case "closetab":
                CloseCurrent();
                break;
            case "reopen":
                ReopenClosedTab();
                break;
            case "folder":
                NewFolder(null);
                break;
            case "nexttab":
                CycleTabs(+1);
                break;
            case "prevtab":
                CycleTabs(-1);
                break;
            case "address":
                OpenCommandBar(newTab: false, animate: false);
                break;
            case "copyurl":
                if (CopyCurrentLink()) ShowCopiedToast();
                break;
            case "private":
                OpenPrivateWindow();
                break;
            case "downloads":
                ToggleLibrary("downloads");
                break;
            case "history":
                ToggleLibrary("history");
                break;
            case "sidebar":
                SetSidebarHidden(!_sidebarHidden);
                break;
            case "settings":
                SettingsWindow.Open(this);
                break;
        }
    }

    /// <summary>New Tab's shortcut, as the hint at the row's end shows it ("Ctrl T"): whatever it's set to.</summary>
    private void ShowNewTabHint() =>
        NewTabHint.Text = KeyHint.From(Settings.ShortcutFor(Settings.Actions.First(a => a.Id == "newtab")).ToString());

    private void NewTabButton_PointerEntered(object sender, PointerRoutedEventArgs e) => NewTabHint.Opacity = 1;

    private void NewTabButton_PointerExited(object sender, PointerRoutedEventArgs e) => NewTabHint.Opacity = 0;

    private void Settings_Changed()
    {
        ApplyShortcuts();
        ShowNewTabHint();
        // Pages loaded from now on keep the new shortcuts from the page too; pages already open
        // pass them on anyway (InputScript), only without holding them back from the page.
        foreach (var view in AllTabs.Select(t => t.View).Append(_peekView))
            if (view?.CoreWebView2 is { } core) _ = SwapInputScriptAsync(core);
        SaveSessionSoon();
    }

    private static async Task SwapInputScriptAsync(CoreWebView2 core)
    {
        try
        {
            if (InputScriptIds.TryGetValue(core, out var old)) core.RemoveScriptToExecuteOnDocumentCreated(old);
            await AddInputScriptAsync(core);
        }
        catch
        {
            // The view closed meanwhile.
        }
    }

    /// <summary>
    /// The window's own shortcuts, for when focus is in the app rather than a page, rebuilt from
    /// settings. They show in the menu too.
    /// </summary>
    private void ApplyShortcuts()
    {
        Root.KeyboardAccelerators.Clear();
        foreach (var action in Settings.Actions)
        {
            var shortcut = Settings.ShortcutFor(action);
            var modifiers = VirtualKeyModifiers.None;
            if (shortcut.Ctrl) modifiers |= VirtualKeyModifiers.Control;
            if (shortcut.Shift) modifiers |= VirtualKeyModifiers.Shift;
            if (shortcut.Alt) modifiers |= VirtualKeyModifiers.Menu;
            var accelerator = new KeyboardAccelerator { Key = shortcut.Key, Modifiers = modifiers };
            var id = action.Id;
            accelerator.Invoked += (_, args) =>
            {
                args.Handled = true;
                RunAction(id);
            };
            Root.KeyboardAccelerators.Add(accelerator);
        }
        // Ctrl+1–9, unless a shortcut has been set to one of them.
        foreach (var keys in TabNumberKeys.Where(k => Settings.ActionFor(k) is null))
        {
            var accelerator = new KeyboardAccelerator { Key = keys.Key, Modifiers = VirtualKeyModifiers.Control };
            var number = TabNumber(keys)!.Value;
            accelerator.Invoked += (_, args) =>
            {
                args.Handled = true;
                SelectTabNumber(number);
            };
            Root.KeyboardAccelerators.Add(accelerator);
        }
    }

    // ───────────────────────── Switching and reopening tabs ─────────────────────────

    // Ctrl+1 to Ctrl+8 go to that tab, top of the sidebar down; Ctrl+9 to the last one, like Chrome.
    private static readonly Shortcut[] TabNumberKeys = Enumerable.Range(1, 9)
        .Select(n => new Shortcut(VirtualKey.Number0 + n, true, false, false)).ToArray();

    private static int? TabNumber(Shortcut keys) =>
        keys is { Ctrl: true, Shift: false, Alt: false } && keys.Key is >= VirtualKey.Number1 and <= VirtualKey.Number9
            ? keys.Key - VirtualKey.Number0
            : null;

    private void SelectTabNumber(int number)
    {
        var all = AllTabs.ToList();
        if (all.Count == 0) return;
        if (number == 9) ShowTab(all[^1]);
        else if (number <= all.Count) ShowTab(all[number - 1]);
    }

    /// <summary>
    /// Ctrl+Tab and Ctrl+Shift+Tab with Settings › Tabs set to go down the sidebar: the next or
    /// previous tab there, round the end. Otherwise they go by use (TabSwitcher.cs).
    /// </summary>
    private void SwitchTab(int step)
    {
        var all = AllTabs.ToList();
        if (all.Count == 0) return;
        var at = _active is null ? -1 : all.IndexOf(_active);
        var next = at < 0 ? (step > 0 ? 0 : all.Count - 1) : (at + step + all.Count) % all.Count;
        ShowTab(all[next]);
    }

    /// <summary>A tab switched to from the keyboard: in front, with the page ready to type into.</summary>
    private void ShowTab(BrowserTab tab)
    {
        if (_commandBarOpen) CloseCommandBar(focusPage: false);
        Activate(tab);
        tab.View.Focus(FocusState.Programmatic);
    }

    private const int MaxClosedTabs = 25;
    // Closed tabs, oldest first; saved with the session so they survive a restart.
    private readonly List<SessionTab> _closedTabs = new();

    private void RememberClosed(BrowserTab tab, int index)
    {
        var url = tab.Url;
        if (string.IsNullOrEmpty(url) || url == "about:blank") url = tab.HomeUri?.ToString();
        if (string.IsNullOrEmpty(url)) return;
        _closedTabs.Add(new SessionTab
        {
            Url = url,
            Title = tab.Title,
            Pinned = tab.IsPinned,
            Place = tab.Place.ToString(),
            Home = tab.HomeUri?.ToString(),
            Index = index,
            Folder = tab.Folder?.Id,
        });
        if (_closedTabs.Count > MaxClosedTabs) _closedTabs.RemoveAt(0);
    }

    /// <summary>Ctrl+Shift+T: the last tab closed comes back, in its group and place, on the page it was on.</summary>
    private void ReopenClosedTab()
    {
        if (_closedTabs.Count == 0) return;
        if (_commandBarOpen) CloseCommandBar(focusPage: false);
        var saved = _closedTabs[^1];
        _closedTabs.RemoveAt(_closedTabs.Count - 1);
        SaveSessionSoon();
        if (!Uri.TryCreate(saved.Url, UriKind.Absolute, out var uri)) return;
        Uri.TryCreate(saved.Home, UriKind.Absolute, out var home);
        var place = Enum.TryParse<TabPlace>(saved.Place, out var p) ? p : TabPlace.Regular;
        // Back into its folder, if that's still there; otherwise with the pinned tabs.
        var folder = saved.Folder is { } id ? _folderTree.AllFolders.FirstOrDefault(f => f.Id == id) : null;
        var index = saved.Index;
        if (place == TabPlace.Folder && folder is null) (place, index) = (TabPlace.Pinned, null);
        _ = CreateTabAsync(uri, place, activate: true, title: saved.Title, home: home, index: index, folder: folder);
    }

    // ───────────────────────── Crashed pages ─────────────────────────

    /// <summary>
    /// A page's process died or hung, or the whole engine stopped. The tab in front shows what
    /// happened in place of its page, with a way back; tabs behind it are put back to sleep, so they
    /// load again (in a working engine) when they're next opened. Other processes (GPU, utilities,
    /// a frame's) recover on their own.
    /// </summary>
    private void OnProcessFailed(BrowserTab tab, CoreWebView2ProcessFailedEventArgs e)
    {
        // A dead page reports no end to its loading.
        tab.Load.Reset();
        switch (e.ProcessFailedKind)
        {
            case CoreWebView2ProcessFailedKind.RenderProcessExited:
                tab.Crash = TabCrash.Crashed;
                break;
            case CoreWebView2ProcessFailedKind.RenderProcessUnresponsive:
                tab.Crash = TabCrash.Unresponsive;
                break;
            case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                EngineStopped();
                if (tab != _active)
                {
                    RestartTab(tab, start: false);
                    return;
                }
                tab.Crash = TabCrash.EngineStopped;
                break;
            default:
                return;
        }
        if (tab == _active) UpdateChrome();
    }

    /// <summary>The engine is gone: the next page to start brings up a new one.</summary>
    private static void EngineStopped()
    {
        _environment = null;
        Extensions.Detach();
    }

    private void ShowCrash()
    {
        var crash = _active?.Crash ?? TabCrash.None;
        CrashPanel.Visibility = crash == TabCrash.None ? Visibility.Collapsed : Visibility.Visible;
        if (crash == TabCrash.None) return;
        _active!.View.Visibility = Visibility.Collapsed;
        var host = DisplayUrl(_active.Url);
        (CrashTitle.Text, CrashText.Text) = crash switch
        {
            TabCrash.Unresponsive => ("This page isn't responding",
                "You can wait for it, or reload it."),
            TabCrash.EngineStopped => ("Drift's web engine stopped",
                "Your tabs are all still here. Reload to open this one again."),
            _ => ("This page crashed",
                host.Length > 0 ? $"Something on {host} made it stop. Reloading usually brings it back." : "Reloading usually brings it back."),
        };
        CrashWaitButton.Visibility = crash == TabCrash.Unresponsive ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CrashReload_Click(object sender, RoutedEventArgs e)
    {
        if (_active is not { } tab) return;
        if (tab.Crash == TabCrash.Crashed)
        {
            // Its process is gone but the view is fine: reloading starts a new one.
            tab.Crash = TabCrash.None;
            tab.View.Visibility = Visibility.Visible;
            try { tab.View.CoreWebView2.Reload(); }
            catch { RestartTab(tab, start: true); }
        }
        else RestartTab(tab, start: true);
        UpdateChrome();
        tab.View.Focus(FocusState.Programmatic);
    }

    /// <summary>A hung page is given more time: its page comes back as it is.</summary>
    private void CrashWait_Click(object sender, RoutedEventArgs e)
    {
        if (_active is not { } tab) return;
        tab.Crash = TabCrash.None;
        tab.View.Visibility = Visibility.Visible;
        UpdateChrome();
    }

    /// <summary>
    /// A new view for the tab, on the page it was on: started now if it's in front, otherwise
    /// left to load when it's next opened. (A hung or dead view can't be reused.)
    /// </summary>
    private void RestartTab(BrowserTab tab, bool start)
    {
        var url = Uri.TryCreate(tab.Url, UriKind.Absolute, out var here) ? here : tab.HomeUri;
        if (tab == _peekOwner) ClosePeek(animate: false);
        var old = tab.View;
        var view = NewTabView();
        var host = HostFor(tab);
        host.Children.Add(view);
        (old.Parent as Panel)?.Children.Remove(old);
        try { old.Close(); } catch { }
        tab.View = view;
        tab.Crash = TabCrash.None;
        tab.PendingUri = url;
        if (start && tab == _active)
        {
            view.Visibility = Visibility.Visible;
            _ = StartTabViewAsync(tab, load: true);
        }
        else tab.IsUnloaded = true;
    }

    // ───────────────────────── Site info ─────────────────────────

    private static readonly string LockGlyph = ((char)0xE72E).ToString();
    private static readonly string WarningGlyph = ((char)0xE7BA).ToString();
    private static readonly string InfoGlyph = ((char)0xE946).ToString();

    private enum SiteSecurity { None, Secure, Local, NotSecure, BadCertificate }

    private SiteSecurity SecurityOf(Uri? uri)
    {
        if (uri is null) return SiteSecurity.None;
        if (uri.Scheme == Uri.UriSchemeHttps)
            return string.Equals(_active?.CertificateErrorHost, uri.Host, StringComparison.OrdinalIgnoreCase)
                ? SiteSecurity.BadCertificate : SiteSecurity.Secure;
        if (uri.Scheme == Uri.UriSchemeHttp) return uri.IsLoopback ? SiteSecurity.Local : SiteSecurity.NotSecure;
        return SiteSecurity.None;
    }

    private Uri? ActiveSite => _active?.Crash == TabCrash.None && Uri.TryCreate(_active.Url, UriKind.Absolute, out var uri) ? uri : null;

    // The pill's text stops short of the badges at its end (zoom, Private).
    private double PillEnd => PillBadges.ActualWidth > 0 ? PillBadges.ActualWidth + PillBadges.Margin.Right + 6 : 10;

    private void PillBadges_SizeChanged(object sender, SizeChangedEventArgs e) =>
        AddressPill.Padding = new Thickness(AddressPill.Padding.Left, 0, PillEnd, 0);

    /// <summary>
    /// The pill's padlock (or warning) at its start, and its copy link and site settings buttons at its
    /// end: all for sites, not Drift's own pages; the end ones only while you're in the sidebar.
    /// </summary>
    private void ShowSiteSecurity()
    {
        var security = SecurityOf(ActiveSite);
        _hasSite = security != SiteSecurity.None;
        ShowSiteSettingsButton();
        if (!_hasSite)
        {
            SiteSecurityButton.Visibility = Visibility.Collapsed;
            AddressPill.Padding = new Thickness(10, 0, PillEnd, 0);
            return;
        }
        SiteSecurityGlyph.Glyph = security switch
        {
            SiteSecurity.Secure => LockGlyph,
            SiteSecurity.Local => InfoGlyph,
            _ => WarningGlyph,
        };
        // The padlock is quiet; a warning isn't.
        SiteSecurityGlyph.Opacity = security == SiteSecurity.Secure ? 0.6 : 1;
        var tip = SecurityText(security);
        ToolTipService.SetToolTip(SiteSecurityButton, tip ?? "Site settings");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SiteSecurityButton, tip is null ? "Site settings" : tip + ", site settings");
        SiteSecurityButton.Visibility = Visibility.Visible;
        AddressPill.Padding = new Thickness(32, 0, PillEnd, 0);
    }

    /// <summary>What the padlock says; nothing for a page on this computer.</summary>
    private static string? SecurityText(SiteSecurity security) => security switch
    {
        SiteSecurity.Secure => "Connection is secure",
        SiteSecurity.Local => null,
        SiteSecurity.BadCertificate => "Certificate isn't valid",
        _ => "Connection isn't secure",
    };

    /// <summary>The padlock opens site settings, below it.</summary>
    private void SiteSecurity_Click(object sender, RoutedEventArgs e) =>
        SiteInfoFlyout.ShowAt(SiteSecurityButton, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
        {
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedLeft,
        });

    private static readonly string LinkGlyph = ((char)0xE71B).ToString();
    private static readonly string CopiedGlyph = ((char)0xE73E).ToString();

    /// <summary>
    /// The address of the page you're looking at to the clipboard: Peek's page while it's open, else
    /// the tab's. False if there's none, or the clipboard was busy.
    /// </summary>
    private bool CopyCurrentLink()
    {
        var core = IsPeekShown ? _peekView?.CoreWebView2 : _active?.View.CoreWebView2;
        if (core?.Source is not { Length: > 0 } url || url == "about:blank") return false;
        var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
        data.SetText(url);
        try
        {
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private DispatcherQueueTimer? _copiedTimer;

    /// <summary>Ctrl+Shift+C: "Copied link" at the foot of the page, rising in, gone after a moment.</summary>
    private void ShowCopiedToast()
    {
        CopiedToast.Visibility = Visibility.Visible;
        CopiedToast.Opacity = 1;
        Spring.To(CopiedToast, "Translation", new Vector3(0, 0, 32), 0.25, 0, from: new Vector3(0, 8, 32));
        if (_copiedTimer is null)
        {
            _copiedTimer = DispatcherQueue.CreateTimer();
            _copiedTimer.IsRepeating = false;
            _copiedTimer.Interval = TimeSpan.FromSeconds(1.5);
            _copiedTimer.Tick += (_, _) => CopiedToast.Opacity = 0;
        }
        _copiedTimer.Stop();
        _copiedTimer.Start();
    }

    /// <summary>The page's address to the clipboard; the button shows a tick for a moment.</summary>
    private void CopyLink_Click(object sender, RoutedEventArgs e)
    {
        if (!CopyCurrentLink()) return;
        CopyLinkGlyph.Glyph = CopiedGlyph;
        ToolTipService.SetToolTip(CopyLinkButton, "Copied");
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1.5);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            CopyLinkGlyph.Glyph = LinkGlyph;
            ToolTipService.SetToolTip(CopyLinkButton, "Copy link");
        };
        timer.Start();
    }

    private bool _hasSite;
    private bool _sidebarHovered;
    private bool _siteInfoOpen;

    private void ShowSiteSettingsButton()
    {
        var shows = SiteSettingsButton.Shows(
            hasSite: _hasSite,
            sidebarHovered: _sidebarHovered,
            panelOpen: _siteInfoOpen,
            focused: SiteInfoButton.FocusState != FocusState.Unfocused);
        // Out of the pill altogether while hidden, so what's left (the zoom level) sits at its end.
        // They fade in as they come; they go at once, as the pill takes the room back.
        foreach (var button in new[] { CopyLinkButton, SiteInfoButton })
        {
            button.Visibility = shows ? Visibility.Visible : Visibility.Collapsed;
            button.Opacity = shows ? 1 : 0;
        }
    }

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _pillButtonsTimer;

    /// <summary>The tab list fills what's left of the sidebar's scrolling part below the tabs.</summary>
    private void SidebarScroll_SizeChanged(object sender, SizeChangedEventArgs e) =>
        SidebarScrollContent.MinHeight = e.NewSize.Height;

    private void Sidebar_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pillButtonsTimer?.Stop();
        _sidebarHovered = true;
        ShowSiteSettingsButton();
    }

    /// <summary>
    /// The pill's buttons stay a moment after the pointer leaves the sidebar, as long as the floating
    /// sidebar does (SidebarDismissDelayMs), so brushing past the edge doesn't snatch them away.
    /// </summary>
    private void Sidebar_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_pillButtonsTimer is null)
        {
            _pillButtonsTimer = DispatcherQueue.CreateTimer();
            _pillButtonsTimer.Interval = TimeSpan.FromMilliseconds(SidebarDismissDelayMs);
            _pillButtonsTimer.IsRepeating = false;
            _pillButtonsTimer.Tick += (_, _) =>
            {
                _sidebarHovered = false;
                ShowSiteSettingsButton();
            };
        }
        _pillButtonsTimer.Stop();
        _pillButtonsTimer.Start();
    }

    private void SiteInfoButton_FocusChanged(object sender, RoutedEventArgs e) => ShowSiteSettingsButton();

    private void SiteInfo_Opened(object? sender, object e)
    {
        _siteInfoOpen = true;
        SidebarMenu_Opened(sender, e);
        ShowSiteSettingsButton();
    }

    private void SiteInfo_Closed(object? sender, object e)
    {
        _siteInfoOpen = false;
        SidebarMenu_Closed(sender, e);
        ShowSiteSettingsButton();
    }

    // The permissions always listed in site info, so they can be set before a site asks. Others show
    // once a site has one set.
    private static readonly (CoreWebView2PermissionKind Kind, string Name)[] MainPermissions =
    {
        (CoreWebView2PermissionKind.Camera, "Camera"),
        (CoreWebView2PermissionKind.Microphone, "Microphone"),
        (CoreWebView2PermissionKind.Geolocation, "Location"),
        (CoreWebView2PermissionKind.Notifications, "Notifications"),
    };

    internal static string PermissionName(CoreWebView2PermissionKind kind) => kind switch
    {
        CoreWebView2PermissionKind.Camera => "Camera",
        CoreWebView2PermissionKind.Microphone => "Microphone",
        CoreWebView2PermissionKind.Geolocation => "Location",
        CoreWebView2PermissionKind.Notifications => "Notifications",
        CoreWebView2PermissionKind.ClipboardRead => "Clipboard",
        CoreWebView2PermissionKind.OtherSensors => "Motion sensors",
        CoreWebView2PermissionKind.MultipleAutomaticDownloads => "Automatic downloads",
        CoreWebView2PermissionKind.FileReadWrite => "Editing files",
        CoreWebView2PermissionKind.Autoplay => "Sound and autoplay",
        CoreWebView2PermissionKind.LocalFonts => "Fonts",
        CoreWebView2PermissionKind.MidiSystemExclusiveMessages => "MIDI devices",
        CoreWebView2PermissionKind.WindowManagement => "Window management",
        _ => kind.ToString(),
    };

    private async void SiteInfo_Opening(object sender, object e)
    {
        var uri = ActiveSite;
        var core = _active?.View.CoreWebView2;
        if (uri is null || core is null) return;
        var origin = uri.GetLeftPart(UriPartial.Authority);

        SiteInfoHost.Text = uri.Host.StartsWith("www.") ? uri.Host[4..] : uri.Host;
        var security = SecurityOf(uri);
        SiteInfoSecureGlyph.Glyph = security switch
        {
            SiteSecurity.Secure => LockGlyph,
            SiteSecurity.Local => InfoGlyph,
            _ => WarningGlyph,
        };
        var secureText = SecurityText(security);
        SiteInfoSecureText.Text = secureText ?? "";
        SiteInfoSecureText.Visibility = secureText is null ? Visibility.Collapsed : Visibility.Visible;
        SiteDataText.Text = "Signs you out of this site and clears what it has stored.";
        ShowSiteAdBlock(uri);
        ClearSiteDataButton.IsEnabled = true;

        SitePermissionList.Children.Clear();
        IReadOnlyList<CoreWebView2PermissionSetting> settings;
        try { settings = await core.Profile.GetNonDefaultPermissionSettingsAsync(); }
        catch { settings = []; }
        var here = settings
            .Where(s => string.Equals(s.PermissionOrigin.TrimEnd('/'), origin, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(s => s.PermissionKind, s => s.PermissionState);
        var kinds = MainPermissions.Select(p => p.Kind).Concat(here.Keys.Where(k => MainPermissions.All(p => p.Kind != k)));
        foreach (var kind in kinds)
            SitePermissionList.Children.Add(PermissionRow(core.Profile, origin, kind, here.TryGetValue(kind, out var state) ? state : CoreWebView2PermissionState.Default));
    }

    /// <summary>A permission and its setting for the site: ask (the default), allow or block.</summary>
    private static Grid PermissionRow(CoreWebView2Profile profile, string origin, CoreWebView2PermissionKind kind, CoreWebView2PermissionState state)
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = PermissionName(kind), FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        var choice = new ComboBox { Width = 110, FontSize = 13, ItemsSource = new[] { "Ask", "Allow", "Block" } };
        choice.SelectedIndex = state switch
        {
            CoreWebView2PermissionState.Allow => 1,
            CoreWebView2PermissionState.Deny => 2,
            _ => 0,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(choice, PermissionName(kind));
        choice.SelectionChanged += async (_, _) =>
        {
            var chosen = choice.SelectedIndex switch
            {
                1 => CoreWebView2PermissionState.Allow,
                2 => CoreWebView2PermissionState.Deny,
                _ => CoreWebView2PermissionState.Default,
            };
            try { await profile.SetPermissionStateAsync(kind, origin, chosen); }
            catch { }
        };
        Grid.SetColumn(choice, 1);
        row.Children.Add(choice);
        return row;
    }

    /// <summary>Cookies, storage and cache for the site in front, then a reload so the page sees it.</summary>
    private async void ClearSiteData_Click(object sender, RoutedEventArgs e)
    {
        var uri = ActiveSite;
        var core = _active?.View.CoreWebView2;
        if (uri is null || core is null) return;
        ClearSiteDataButton.IsEnabled = false;
        try
        {
            var origin = uri.GetLeftPart(UriPartial.Authority);
            await core.CallDevToolsProtocolMethodAsync("Storage.clearDataForOrigin",
                System.Text.Json.JsonSerializer.Serialize(new { origin, storageTypes = "all" }));
            // Cookies set for the whole site (.example.com) too, not just this host.
            foreach (var cookie in await core.CookieManager.GetCookiesAsync(origin)) core.CookieManager.DeleteCookie(cookie);
            SiteDataText.Text = "Cleared.";
            core.Reload();
        }
        catch (Exception ex)
        {
            SiteDataText.Text = "Couldn't clear it: " + ex.Message;
            ClearSiteDataButton.IsEnabled = true;
        }
    }

    // ───────────────────────── Downloads ─────────────────────────

    private static readonly string DownloadGlyph = ((char)0xE896).ToString();
    private static readonly string DoneGlyph = ((char)0xE73E).ToString();

    /// <summary>
    /// The sidebar's downloads row: shown while something downloads (name and progress) and, once it's
    /// done, until you've looked at the list.
    /// </summary>
    private void ShowDownloads()
    {
        var running = Downloads.Items.Where(i => i.IsRunning).ToList();
        var latest = running.FirstOrDefault() ?? (Downloads.HasUnseen ? Downloads.Items.FirstOrDefault() : null);
        if (_libraryOpen && _libraryPage == "downloads") UpdateDownloadsPage();
        if (latest is null)
        {
            DownloadsButton.Visibility = Visibility.Collapsed;
            return;
        }
        DownloadsButton.Visibility = Visibility.Visible;
        DownloadsText.Text = running.Count > 1 ? $"{running.Count} downloads" : latest.FileName;
        ToolTipService.SetToolTip(DownloadsButton, running.Count > 1 ? $"{running.Count} downloads" : $"{latest.FileName} · {latest.Status}");
        if (running.Count > 0)
        {
            long received = running.Sum(i => i.Received), total = running.Sum(i => i.Total ?? 0);
            DownloadsProgress.Visibility = Visibility.Visible;
            DownloadsProgress.IsIndeterminate = running.Any(i => i.Total is not > 0);
            DownloadsProgress.Value = total > 0 ? received * 100.0 / total : 0;
            DownloadsGlyph.Glyph = DownloadGlyph;
        }
        else
        {
            DownloadsProgress.Visibility = Visibility.Collapsed;
            DownloadsGlyph.Glyph = latest.State == DownloadState.Completed ? DoneGlyph : WarningGlyph;
        }
    }

    private void DownloadList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not DownloadItem item || item.State != DownloadState.Completed) return;
        Downloads.Open(item);
    }

    private void DownloadFolder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is DownloadItem item) Downloads.ShowInFolder(item);
    }

    private void DownloadCancel_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is DownloadItem item) Downloads.Cancel(item);
    }

    private void DownloadRemove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DownloadItem item) return;
        Downloads.Remove(item);
        RefreshDownloads();
    }

    private void DownloadsFolder_Click(object sender, RoutedEventArgs e) => Downloads.ShowInFolder(null);

    private void DownloadsClear_Click(object sender, RoutedEventArgs e)
    {
        Downloads.ClearFinished();
        RefreshDownloads();
    }

    // ───────────────────────── Private windows ─────────────────────────

    /// <summary>Ctrl+Shift+N: a private window, a little down and right of this one.</summary>
    private void OpenPrivateWindow()
    {
        if (_commandBarOpen) CloseCommandBar(focusPage: false);
        var window = new MainWindow(isPrivate: true);
        var at = AppWindow.Position;
        window.AppWindow.Move(new PointInt32(at.X + 40, at.Y + 40));
        window.Activate();
    }


    // ───────────────────────── Opened from outside ─────────────────────────

    private readonly List<Uri> _launchUris = new();
    private bool _restored;

    /// <summary>
    /// Another Drift was started to open these (a link clicked in another app): Drift comes to the
    /// front and opens each in a new tab. Started with nothing to open, it just comes to the front.
    /// </summary>
    /// <summary>A page picked somewhere else in Drift (History): a new tab here, with the window in front.</summary>
    internal void OpenPage(Uri uri)
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter) presenter.Restore();
        Activate();
        if (_commandBarOpen) CloseCommandBar(focusPage: false);
        OpenInNewTab(uri);
    }

    internal void OpenFromLaunch(IReadOnlyList<Uri> uris)
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter) presenter.Restore();
        Activate();
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        if (!_restored)
        {
            _launchUris.AddRange(uris);
            return;
        }
        foreach (var uri in uris) OpenInNewTab(uri);
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    private static string ShortcutText(string id) =>
        Settings.ShortcutFor(Settings.Actions.First(a => a.Id == id)).ToString();

    /// <param name="args">"x,y,pageWidth" in CSS pixels. The view's width over the page's is the zoom.</param>
    private static void RecordLinkPress(BrowserTab tab, string args)
    {
        var parts = args.Split(',');
        if (parts.Length != 3) return;
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) return;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) return;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var pageWidth) || pageWidth <= 0) return;

        var zoom = tab.View.ActualWidth / pageWidth;
        tab.LinkPress = new Point(x * zoom, y * zoom);
        tab.LinkPressTime = Environment.TickCount64;
    }

    private void CloseCurrent()
    {
        if (_commandBarOpen) CloseCommandBar();
        else if (IsPeekShown) ClosePeek(animate: true);
        else if (_active is not null) CloseTab(_active);
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && CancelCycle())
        {
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape && _commandBarOpen)
        {
            CloseCommandBar();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape && IsPeekShown)
        {
            ClosePeek(animate: true);
            e.Handled = true;
        }
    }

    private void TabList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection) return;
        if (((ListViewBase)sender).SelectedItem is BrowserTab tab && tab != _active) Activate(tab);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => _active?.View.GoBack();
    private void Forward_Click(object sender, RoutedEventArgs e) => _active?.View.GoForward();
    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (_active is not { } tab) return;
        if (tab.Load.IsLoading) tab.View.CoreWebView2?.Stop();
        else tab.View.Reload();
    }

    /// <summary>
    /// A tab's loading, for the reload button. Added after WireTab's Peek interception, whose
    /// handler runs first, so a link it cancels (and opens in Peek) never counts as loading.
    /// </summary>
    private void WireLoading(BrowserTab tab, CoreWebView2 core)
    {
        tab.Load.Reset();
        core.NavigationStarting += (_, e) =>
        {
            if (e.Cancel) return;
            tab.Load.Started(e.NavigationId);
            if (tab == _active) ShowLoadButton();
        };
        core.NavigationCompleted += (_, e) =>
        {
            tab.Load.Finished(e.NavigationId);
            if (tab == _active) ShowLoadButton();
        };
    }

    // Segoe Fluent Icons: refresh, and a cross to stop.
    private const string ReloadGlyphText = "\uE72C";
    private const string StopGlyphText = "\uE711";

    /// <summary>The reload button, or while the tab in front is loading, a stop button in its place.</summary>
    private void ShowLoadButton()
    {
        var loading = _active?.Load.IsLoading == true;
        ReloadGlyph.Glyph = loading ? StopGlyphText : ReloadGlyphText;
        var name = loading ? "Stop loading" : "Reload";
        ToolTipService.SetToolTip(ReloadButton, name);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ReloadButton, name);
    }
    private void NewTab_Click(object sender, RoutedEventArgs e) => OpenCommandBar(newTab: true, animate: true);
    private void Reopen_Click(object sender, RoutedEventArgs e) => ReopenClosedTab();
    private void PrivateWindow_Click(object sender, RoutedEventArgs e) => OpenPrivateWindow();
    private void Downloads_Click(object sender, RoutedEventArgs e) => OpenLibrary("downloads");
    private void History_Click(object sender, RoutedEventArgs e) => OpenLibrary("history");

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not BrowserTab tab) return;
        // A split's row is both pages (Split.cs).
        if (tab is { IsSplitRow: true, Split: { } split }) CloseSplit(split);
        else CloseTab(tab);
    }

    /// <summary>The menu's Close tab: the tab, its Peek with it.</summary>
    private void CloseTabNow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not BrowserTab tab) return;
        if (tab is { IsSplitRow: true, Split: { } split }) CloseSplit(split);
        else RemoveTab(tab);
    }

    private void MoveToEssentials_Click(object sender, RoutedEventArgs e) => MoveFrom(sender, TabPlace.Essential);
    private void MoveToPinned_Click(object sender, RoutedEventArgs e) => MoveFrom(sender, TabPlace.Pinned);
    private void MoveToRegular_Click(object sender, RoutedEventArgs e) => MoveFrom(sender, TabPlace.Regular);

    // ───────────────────────── Dragging tabs ─────────────────────────
    // Drag a tab to reorder it, or into another group (Essentials, pinned, regular) to move it there,
    // at the spot it's dropped. Like Zen: the lists don't open a gap; a line in the accent colour
    // shows where it will land, and the ghost under the pointer takes the shape of the group it's
    // over (a tile on Essentials, a row on the lists).

    private BrowserTab? _dragTab;
    private Windows.Graphics.Imaging.SoftwareBitmap? _rowGhost, _tileGhost, _pageGhost;
    private Point _rowGhostAnchor, _tileGhostAnchor, _pageGhostAnchor;

    private void Tabs_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        // A drag whose end went unreported (its row was replaced as it moved) mustn't linger.
        _dragTab = null;
        _dragFolder = null;
        if (e.Items.FirstOrDefault() is TabFolder folder)
        {
            // A folder moves among the folders only.
            if (folder.IsRenaming) { e.Cancel = true; return; }
            _dragFolder = folder;
            e.Data.RequestedOperation = DataPackageOperation.Move;
            _ = RenderFolderGhostAsync(folder);
            return;
        }
        if (e.Items.FirstOrDefault() is not BrowserTab tab) { e.Cancel = true; return; }
        _dragTab = tab;
        e.Data.RequestedOperation = DataPackageOperation.Move;
        // Empty groups get a place to drop into.
        EssentialDropZone.Visibility = EssentialTabs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PinnedDropZone.Visibility = PinnedTabs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        // And the page, to split with (Split.cs).
        ShowSplitDrop(tab);
        _ = RenderGhostsAsync(tab);
    }

    private void Tabs_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        _dragTab = null;
        _dragFolder = null;
        _rowGhost = _tileGhost = _pageGhost = null;
        if (_slideRows is null) _slideExcept = null;
        // Let go somewhere else, or cancelled: the rows go back.
        if (_rowPreviewShown) ClearRowPreviews(animate: true);
        HideDropIndicator();
        HideFolderHighlight();
        ResetEssentialsPreview(animate: true);
        EssentialDropZone.Visibility = PinnedDropZone.Visibility = Visibility.Collapsed;
        HideSplitDrop();
        // Moving a tab takes it out and puts it back, which loses its selection, and its focus
        // (which would otherwise land on the first button in the window, with a focus rectangle).
        if (_active is not null)
        {
            Activate(_active);
            _active.View.Focus(FocusState.Programmatic);
        }
        SaveSessionSoon();
    }

    /// <summary>Photographs the two ghosts (row and tile) for this tab, the size its rows and tiles are.</summary>
    private async Task RenderGhostsAsync(BrowserTab tab)
    {
        _rowGhost = _tileGhost = _pageGhost = null;
        RowGhostIcon.Source = TileGhostIcon.Source = PageGhostIcon.Source = tab.Favicon;
        RowGhostGlyph.Visibility = TileGhostGlyph.Visibility = PageGhostGlyph.Visibility = tab.PlaceholderVisibility;
        RowGhostTitle.Text = PageGhostTitle.Text = tab.Title;
        RowGhost.Width = Math.Max(120, TabList.ActualWidth);
        TileGhost.Width = EssentialList.ItemsPanelRoot is ItemsWrapGrid { ItemWidth: > 8 and var cell } ? cell - 8 : 88;
        try
        {
            var row = await SnapshotAsync(RowGhost);
            var tile = await SnapshotAsync(TileGhost);
            var page = await SnapshotAsync(PageGhost);
            if (_dragTab != tab) return;
            // The pointer holds a row by its icon, so the drop line shows to its left. A tile hangs
            // just below the pointer, so the bar's ring and top show above it.
            if (row is { } r) { _rowGhost = r.Bitmap; _rowGhostAnchor = new Point(19 * r.Scale, r.Bitmap.PixelHeight / 2.0); }
            if (tile is { } t) { _tileGhost = t.Bitmap; _tileGhostAnchor = new Point(t.Bitmap.PixelWidth / 2.0, 4 * t.Scale); }
            // Over the page (to split, Split.cs), a card of the page, held by its middle.
            if (page is { } g) { _pageGhost = g.Bitmap; _pageGhostAnchor = new Point(g.Bitmap.PixelWidth / 2.0, g.Bitmap.PixelHeight / 2.0); }
        }
        catch
        {
            // No custom ghost: the drag shows the list's own picture of the item.
        }
    }

    private static async Task<(Windows.Graphics.Imaging.SoftwareBitmap Bitmap, double Scale)?> SnapshotAsync(FrameworkElement element)
    {
        var target = new RenderTargetBitmap();
        await target.RenderAsync(element);
        if (target.PixelWidth == 0 || element.ActualWidth == 0) return null;
        var pixels = await target.GetPixelsAsync();
        var bitmap = Windows.Graphics.Imaging.SoftwareBitmap.CreateCopyFromBuffer(pixels,
            Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, target.PixelWidth, target.PixelHeight,
            Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
        return (bitmap, target.PixelWidth / element.ActualWidth);
    }

    private TabPlace? DropPlace(object target) =>
        target == EssentialList || target == EssentialDropZone ? TabPlace.Essential
        : target == FolderList ? TabPlace.Folder
        : target == PinnedList || target == PinnedDropZone ? TabPlace.Pinned
        : target == TabList ? TabPlace.Regular
        : null;

    private void Tabs_DragOver(object sender, DragEventArgs e)
    {
        if ((_dragTab is null && _dragFolder is null) || DropPlace(sender) is not { } place) return;
        // A split's row moves among the tabs only: it can't be pinned or filed as one (Split.cs).
        if (_dragTab is { IsSplitRow: true } && place != TabPlace.Regular) return;
        // Folders only move among the folders.
        if (_dragFolder is not null && sender != FolderList) return;
        e.Handled = true;
        e.AcceptedOperation = DataPackageOperation.Move;
        var ui = e.DragUIOverride;
        ui.IsGlyphVisible = false;
        ui.IsCaptionVisible = false;
        ui.IsContentVisible = true;
        var shape = place == TabPlace.Essential ? _tileGhost : _rowGhost;
        if (shape is not null) ui.SetContentFromSoftwareBitmap(shape, place == TabPlace.Essential ? _tileGhostAnchor : _rowGhostAnchor);

        // The other lists: the dragged row lifted out of its own, any room made for it closed.
        ShowRowPreviewsExcept(sender);
        if (sender == EssentialList)
        {
            HideFolderHighlight();
            PreviewEssentials(e);
            return;
        }
        ResetEssentialsPreview(animate: true);
        if (sender == FolderList)
        {
            FolderList_DragOver(e);
            return;
        }
        HideFolderHighlight();
        ShowDropZone(sender as Grid);
        if (sender is ListViewBase list) ShowRowPreview(list, LandingIn(list, e).Slot);
    }

    private void Tabs_DragLeave(object sender, DragEventArgs e)
    {
        // Moving between a list's own rows raises this too; only leaving the list itself counts.
        if (sender is FrameworkElement target)
        {
            var at = e.GetPosition(target);
            if (at.X >= 0 && at.Y >= 0 && at.X < target.ActualWidth && at.Y < target.ActualHeight) return;
        }
        HideDropIndicator();
        if (sender is ListViewBase left && left != EssentialList) ShowRowPreview(left, null);
        if (sender == FolderList) HideFolderHighlight();
        if (sender == EssentialList) ResetEssentialsPreview(animate: true);
    }

    private void Tabs_Drop(object sender, DragEventArgs e)
    {
        HideDropIndicator();
        // The dropped row lands where it was let go; the rest slide on from where they're shown
        // (making room for it) to their new places, which should be the same.
        _slideExcept = (object?)_dragTab ?? _dragFolder;
        NoteSlideStart();
        var landing = sender is ListViewBase over && over != EssentialList && over != FolderList ? LandingIn(over, e).Gap : 0;
        ClearRowPreviews(animate: false);
        var essentialLanding = _essentialLanding;
        // The tiles go back to their places as the real order takes over, without animating.
        ResetEssentialsPreview(animate: false);
        if (sender == FolderList)
        {
            if (_dragTab is null && _dragFolder is null) return;
            e.Handled = true;
            FolderList_Drop(e);
            return;
        }
        if (_dragTab is not { } tab || DropPlace(sender) is not { } place) return;
        if (tab.IsSplitRow && place != TabPlace.Regular) return;
        e.Handled = true;
        if (sender == EssentialList && essentialLanding is { } land)
        {
            if (tab.Place != TabPlace.Essential) MoveTab(tab, TabPlace.Essential, land);
            else if (EssentialTabs.IndexOf(tab) is var at and >= 0 && at != land)
            {
                _syncingSelection = true;
                EssentialTabs.Move(at, land);
                _syncingSelection = false;
                SaveSessionSoon();
            }
            return;
        }
        var index = landing;
        if (place != tab.Place)
        {
            MoveTab(tab, place, index);
            return;
        }
        // Reorder: the index counts the tab itself, which leaves its old spot first.
        var items = CollectionFor(place);
        var from = items.IndexOf(tab);
        var to = index > from ? index - 1 : index;
        if (from < 0 || to == from) return;
        _syncingSelection = true;
        items.Move(from, Math.Clamp(to, 0, items.Count - 1));
        _syncingSelection = false;
        SaveSessionSoon();
    }

    // ── Essentials preview: the tiles move to the order a drop would give ──

    // Tiles are 46 high with 4 around them.
    private const double EssentialCellHeight = 54;
    private int _essentialsArriving;            // 1 while a tab from another group is over Essentials
    private int? _essentialLanding;             // where the dragged tab would land, while over them
    private (int Land, int Columns, double Width)? _essentialPreview;

    /// <summary>
    /// While a tab is dragged over Essentials: the tiles spring to the order dropping it here would
    /// give, with the tab itself (faded) in the slot under the pointer. A tab from another group gets a
    /// faded tile of its own, and the grid takes the column count it would have with it.
    /// </summary>
    private void PreviewEssentials(DragEventArgs e)
    {
        if (_dragTab is not { } tab) return;
        var arriving = tab.Place != TabPlace.Essential;
        if (arriving && _essentialsArriving == 0)
        {
            _essentialsArriving = 1;
            LayoutEssentials();
        }
        if (EssentialList.ItemsPanelRoot is not ItemsWrapGrid { ItemWidth: > 0 and var width } grid) return;
        var columns = grid.MaximumRowsOrColumns;
        var count = EssentialTabs.Count + (arriving ? 1 : 0);

        // The slot under the pointer, from the grid's cells (the tiles themselves may be mid-move).
        var at = e.GetPosition(grid);
        var column = Math.Clamp((int)Math.Floor(at.X / width), 0, columns - 1);
        var row = Math.Max(0, (int)Math.Floor(at.Y / EssentialCellHeight));
        var land = Math.Clamp(row * columns + column, 0, count - 1);
        _essentialLanding = land;
        if (_essentialPreview == (land, columns, width)) return;
        _essentialPreview = (land, columns, width);

        // Room for a row the arriving tab would start.
        var rows = (count + columns - 1) / columns;
        EssentialList.MinHeight = rows * EssentialCellHeight;

        Vector3 Slot(int i) => new((float)(i % columns * width), (float)(i / columns * EssentialCellHeight), 0);
        var order = EssentialTabs.Where(t => t != tab).ToList();
        order.Insert(land, tab);
        for (var i = 0; i < EssentialTabs.Count; i++)
        {
            if (EssentialList.ContainerFromIndex(i) is not UIElement tile) continue;
            Spring.To(tile, "Translation", Slot(order.IndexOf(EssentialTabs[i])) - Slot(i), duration: 0.3, bounce: 0.15);
            tile.Opacity = EssentialTabs[i] == tab ? 0.4 : 1;
        }

        if (arriving)
        {
            var origin = grid.TransformToVisual(DropIndicatorLayer).TransformPoint(new Point(4, 4));
            var first = EssentialPlaceholder.Visibility == Visibility.Collapsed;
            Canvas.SetLeft(EssentialPlaceholder, origin.X);
            Canvas.SetTop(EssentialPlaceholder, origin.Y);
            EssentialPlaceholder.Width = width - 8;
            EssentialPlaceholderIcon.Source = tab.Favicon;
            EssentialPlaceholderGlyph.Visibility = tab.PlaceholderVisibility;
            EssentialPlaceholder.Visibility = Visibility.Visible;
            if (first) Spring.Jump(EssentialPlaceholder, "Translation", Slot(land));
            else Spring.To(EssentialPlaceholder, "Translation", Slot(land), duration: 0.3, bounce: 0.15);
        }
    }

    /// <summary>The tiles back in their own places, and the grid back to its own size.</summary>
    private void ResetEssentialsPreview(bool animate)
    {
        if (_essentialPreview is null && _essentialsArriving == 0) return;
        _essentialPreview = null;
        _essentialLanding = null;
        if (EssentialList.ItemsPanelRoot is { } panel)
        {
            foreach (var tile in panel.Children)
            {
                if (animate) Spring.To(tile, "Translation", Vector3.Zero, duration: 0.3, bounce: 0.15);
                else Spring.Jump(tile, "Translation", Vector3.Zero);
                tile.Opacity = 1;
            }
        }
        EssentialPlaceholder.Visibility = Visibility.Collapsed;
        EssentialList.MinHeight = 0;
        if (_essentialsArriving != 0)
        {
            _essentialsArriving = 0;
            LayoutEssentials();
        }
    }

    /// <summary>Outlines an empty group's drop zone in the accent colour while the tab is over it.</summary>
    private void ShowDropZone(Grid? zone)
    {
        if (zone is not null) DropLine.Visibility = Visibility.Collapsed;
        var accent = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        var normal = (Brush)Application.Current.Resources["DriftDividerBrush"];
        EssentialDropZoneOutline.Stroke = zone == EssentialDropZone ? accent : normal;
        PinnedDropZoneOutline.Stroke = zone == PinnedDropZone ? accent : normal;
    }

    private void HideDropIndicator()
    {
        DropLine.Visibility = Visibility.Collapsed;
        ShowDropZone(null);
    }

    private void MoveFrom(object sender, TabPlace place)
    {
        if ((sender as FrameworkElement)?.Tag is BrowserTab tab) MoveTab(tab, place);
    }

    private void ResetPinned_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is BrowserTab { HomeUri: { } home } tab) Navigate(tab, home);
    }

    private void PeekPageFrame_Tapped(object sender, TappedRoutedEventArgs e) => ClosePeek(animate: true);
    private void PeekClose_Click(object sender, RoutedEventArgs e) => ClosePeek(animate: true);
    private void PeekToTab_Click(object sender, RoutedEventArgs e) => PromotePeekToTab();

    // The close button on a tab row only shows while the row is hovered.
    private void TabRow_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        SetCloseButtonOpacity(sender, 1);
        IntegrationAnchor_PointerEntered(sender, e);
    }

    private void TabRow_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        SetCloseButtonOpacity(sender, 0);
        IntegrationAnchor_PointerExited(sender, e);
    }

    private static void SetCloseButtonOpacity(object row, double opacity)
    {
        if (row is Grid grid && grid.Children.OfType<Button>().FirstOrDefault() is { } close) close.Opacity = opacity;
    }

    // ───────────────────────── Window and sidebar ─────────────────────────

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e) => SetSidebarHidden(!_sidebarHidden);

    /// <summary>
    /// Hidden: the page takes the whole window and the sidebar only appears, floating over
    /// the page, while the pointer is at the window's left edge.
    /// </summary>
    private void SetSidebarHidden(bool hidden)
    {
        // Docking or undocking puts the library away at once; it opens again in the new place.
        if (_libraryOpen || _libraryStepping)
        {
            StopLibraryMotion();
            _libraryOpen = false;
            SetSidebarWidth(SidebarWidth);
        }
        ApplySidebarLayout(hidden);
    }

    /// <summary>The sidebar docked in its column, or floating over the page out of sight.</summary>
    private void ApplySidebarLayout(bool hidden)
    {
        _sidebarHidden = hidden;
        // Shut, from where the page now starts.
        SetSidebarWidth(ClosedWidth);
        var width = new GridLength(hidden ? 0 : SidebarWidth);
        SidebarColumn.Width = width;
        PageArea.Margin = hidden ? new Thickness(8) : new Thickness(0, 8, 8, 8);

        // Floating, the sidebar is a card inset FloatingInset from the window's edges, level with the
        // page's top and bottom. The sidebar itself runs out to the window's left, top and bottom edges
        // (and FloatingBleed past the left one), and a transparent background makes that whole area
        // count as the panel. Otherwise the gap between the edge and the card was outside: the slide-in
        // moved the card's edge past the pointer that revealed it, the panel left, the hot zone revealed
        // it again, and it flickered. The bleed keeps the edge covered while the spring overshoots.
        // The card's padding is even on both sides, and it's 4px wider than the docked column,
        // so the content inside keeps its docked width.
        Grid.SetColumnSpan(Sidebar, hidden ? 2 : 1);
        Sidebar.HorizontalAlignment = hidden ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        Sidebar.Width = hidden ? FloatingBleed + FloatingInset + SidebarWidth + 4 : double.NaN;
        Sidebar.Margin = hidden ? new Thickness(-FloatingBleed, 0, 0, 0) : new Thickness(0);
        Sidebar.Background = hidden ? FloatingHitArea : null;
        Sidebar.Padding = hidden
            ? new Thickness(FloatingBleed + FloatingInset + 8, FloatingInset + 6, 8, FloatingInset + 8)
            : new Thickness(8, 6, 4, 8);
        // From the content box out to the card's edge, which sits FloatingInset inside the window's.
        FloatingSidebarBackdrop.Margin = new Thickness(-8, -6, -8, -8);
        FloatingSidebarBackdrop.Visibility = hidden ? Visibility.Visible : Visibility.Collapsed;
        _sidebarDismissing = false;
        _sidebarDismissTimer.Stop();
        _sidebarWatch.Stop();
        _pointerAtEdgeSince = _pointerOffSidebarSince = null;
        Spring.Jump(Sidebar, "Translation", Vector3.Zero);
        Sidebar.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
        SidebarHotZone.Visibility = hidden ? Visibility.Visible : Visibility.Collapsed;

        UpdateDragRegions();
    }

    // How far the floating sidebar slides: from just past the window's edge, shadow included.
    private const float SidebarSlide = (float)(FloatingInset + SidebarWidth + 4 + 16);

    /// <summary>
    /// The floating sidebar slides in like a drawer on a spring with a slight bounce, in about 250ms,
    /// pushed off rather than from rest so it's moving the moment it's asked for (like an ease-out).
    /// </summary>
    private void RevealFloatingSidebar()
    {
        Sidebar.Visibility = Visibility.Visible;
        Spring.To(Sidebar, "Translation", Vector3.Zero, duration: 0.25, bounce: 0.15,
            from: new Vector3(-SidebarSlide, 0, 0), kick: 0.5);
        _sidebarWatch.Start();
    }

    /// <summary>
    /// Back off the edge on a spring with no bounce, faster than it came; it collapses once it's out
    /// of sight. Coming back over it on the way (ReturnFloatingSidebar) retargets the same spring.
    /// </summary>
    private void DismissFloatingSidebar()
    {
        if (!_uiSettings.AnimationsEnabled)
        {
            Sidebar.Visibility = Visibility.Collapsed;
            return;
        }
        _sidebarDismissing = true;
        Spring.To(Sidebar, "Translation", new Vector3(-SidebarSlide, 0, 0), duration: 0.2, bounce: 0);
        // A spring has no completed event to wait for; by now it's off the edge, shadow and all.
        _sidebarDismissTimer.Start();
    }

    /// <summary>Back over the panel while it's leaving: the spring turns around from wherever it got to.</summary>
    private void ReturnFloatingSidebar()
    {
        _sidebarDismissing = false;
        _sidebarDismissTimer.Stop();
        Spring.To(Sidebar, "Translation", Vector3.Zero, duration: 0.25, bounce: 0.15);
    }

    private void SidebarDismissTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        if (!_sidebarDismissing) return;
        _sidebarDismissing = false;
        Sidebar.Visibility = Visibility.Collapsed;
    }

    // The pointer has to rest at the window's edge this long before the floating sidebar opens:
    // long enough that a pointer flung past the edge (to another window or screen) doesn't open it,
    // short enough that a deliberate hover still feels immediate.
    private const long SidebarRevealDelayMs = 60;

    // And it has to be off the panel this long before it closes, so slipping a few pixels past
    // its edge and straight back doesn't close it.
    private const long SidebarDismissDelayMs = 300;

    private void SidebarHotZone_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (!_sidebarHidden || Sidebar.Visibility == Visibility.Visible || _libraryOpen) return;
        // The wait starts now, not at the watch's first look.
        _pointerAtEdgeSince = Environment.TickCount64;
        _sidebarWatch.Start();
    }

    /// <summary>
    /// While the floating sidebar is opening or open, decides from where the pointer really is,
    /// asked of Windows every 20ms, rather than from XAML's pointer enter and exit events. Those
    /// don't arrive when the pointer leaves the window, or comes back in over the page (the web
    /// view takes the pointer), which left the sidebar open until it was hovered and left again.
    /// </summary>
    private void SidebarWatch_Tick(DispatcherQueueTimer sender, object args)
    {
        // (Nor while the library's open: it's in the sidebar's place.)
        if (!_sidebarHidden || _libraryOpen)
        {
            sender.Stop();
            return;
        }
        var pointer = PointerOverWindow();
        var now = Environment.TickCount64;

        if (Sidebar.Visibility != Visibility.Visible)
        {
            // Closed: open once the pointer has rested at the left edge for a moment.
            if (pointer is { } point && point.X < SidebarHotZone.ActualWidth)
            {
                _pointerAtEdgeSince ??= now;
                if (now - _pointerAtEdgeSince >= SidebarRevealDelayMs)
                {
                    _pointerAtEdgeSince = null;
                    _pointerOffSidebarSince = null;
                    RevealFloatingSidebar();
                }
            }
            else
            {
                // Left the edge before it opened, or the panel finished leaving: nothing to watch.
                _pointerAtEdgeSince = null;
                sender.Stop();
            }
            return;
        }

        // Open (or on its way out). Where the panel sits at rest, from the window's left edge
        // to the card's right edge, gap included.
        var panel = new Rect(0, 0, Math.Max(0, Sidebar.ActualWidth - FloatingBleed), Root.ActualHeight);
        var keepOpen = _commandBarOpen || _openSidebarMenus > 0 || _dragTab is not null || (pointer is { } over && panel.Contains(over))
            || _trackerSwiping;
        if (keepOpen)
        {
            _pointerOffSidebarSince = null;
            if (_sidebarDismissing) ReturnFloatingSidebar();
            return;
        }
        if (_sidebarDismissing) return;
        _pointerOffSidebarSince ??= now;
        if (now - _pointerOffSidebarSince >= SidebarDismissDelayMs)
        {
            _pointerOffSidebarSince = null;
            DismissFloatingSidebar();
        }
    }

    private void SidebarMenu_Opened(object? sender, object e) => _openSidebarMenus++;

    // Once the menu closes, SidebarWatch_Tick decides from where the pointer is: picking an item or
    // clicking back on the sidebar keeps it, clicking away on the page sends it off.
    private void SidebarMenu_Closed(object? sender, object e) => _openSidebarMenus = Math.Max(0, _openSidebarMenus - 1);

    /// <summary>
    /// Where the pointer is in the window, in its own coordinates, asked of Windows; null when the
    /// pointer isn't over Drift (off the window, or over another app's window on top of it).
    /// </summary>
    private Point? PointerOverWindow()
    {
        if (Root.XamlRoot is null || !GetCursorPos(out var screen)) return null;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var under = WindowFromPoint(screen);
        // Drift's own window, or one inside it (the web views' input windows belong to their engine).
        if (under == IntPtr.Zero || GetAncestor(under, GetAncestorRoot) != hwnd) return null;
        var client = screen;
        if (!ScreenToClient(hwnd, ref client)) return null;
        var scale = Root.XamlRoot.RasterizationScale;
        var point = new Point(client.X / scale, client.Y / scale);
        return point.X >= 0 && point.Y >= 0 && point.X < Root.ActualWidth && point.Y < Root.ActualHeight ? point : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    private const uint GetAncestorRoot = 2;

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hwnd, ref NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    // ───────────────────────── Polish ─────────────────────────

    /// <summary>
    /// WebView2 composes the page through its own child visual, which a parent's CornerRadius
    /// or clip never reaches, so pages painted square corners over the rounded cards.
    /// Clipping that visual directly rounds the page itself.
    /// </summary>
    /// <param name="radius">The card's corner radius minus its 1px border.</param>
    /// <summary>A view already rounded (RoundCorners) gets another radius: Peek's page becoming a tab's.</summary>
    private static void SetCornerRadius(WebView2 view, float radius)
    {
        if (ElementCompositionPreview.GetElementChildVisual(view) is { Clip: CompositionGeometricClip { Geometry: CompositionRoundedRectangleGeometry geometry } })
            geometry.CornerRadius = new Vector2(radius);
    }

    private static void RoundCorners(WebView2 view, float radius)
    {
        void Apply()
        {
            if (ElementCompositionPreview.GetElementChildVisual(view) is not { } visual) return;
            if (visual.Clip is not CompositionGeometricClip { Geometry: CompositionRoundedRectangleGeometry geometry })
            {
                geometry = visual.Compositor.CreateRoundedRectangleGeometry();
                geometry.CornerRadius = new Vector2(radius);
                visual.Clip = visual.Compositor.CreateGeometricClip(geometry);
            }
            geometry.Size = view.ActualSize;
        }

        Apply();
        view.SizeChanged += (_, _) => Apply();
    }

    /// <summary>
    /// Pressable things shrink slightly while pressed, so the UI visibly hears the click, and
    /// spring back with a small rebound on release. Springs on the element's own Scale: no layout
    /// work, and a quick press-release retargets smoothly mid-motion.
    /// </summary>
    private void AddPressFeedback(UIElement element, float pressedScale)
    {
        _pressFeedback.Add(element);

        void Press(object sender, PointerRoutedEventArgs e)
        {
            if (!_uiSettings.AnimationsEnabled) return;
            element.CenterPoint = new Vector3(element.ActualSize / 2, 0);
            // Down fast and without bounce; the liveliness is in the release.
            Spring.To(element, "Scale", new Vector3(pressedScale, pressedScale, 1), duration: 0.15, bounce: 0);
        }
        void Release(object sender, PointerRoutedEventArgs e)
        {
            if (element.Scale == Vector3.One) return;
            Spring.To(element, "Scale", Vector3.One, duration: 0.3, bounce: 0.4);
        }

        // Buttons and list items mark pointer events handled, so listen to handled ones too.
        element.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Press), true);
        element.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(Release), true);
        element.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(Release), true);
        element.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(Release), true);
        element.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler(Release), true);
    }

    private void ListItem_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (sender == FolderList && !args.InRecycleQueue) IndentFolderRow(args.ItemContainer, args.Item);
        // Containers are recycled; wire each one once.
        if (args.InRecycleQueue || _pressFeedback.Contains(args.ItemContainer)) return;
        AddPressFeedback(args.ItemContainer, sender == EssentialList ? 0.96f : 0.98f);
    }

    private void Menu_Opening(object? sender, object e)
    {
        SidebarMenuItem.Text = _sidebarHidden ? "Show sidebar" : "Hide sidebar";
        NewTabMenuItem.KeyboardAcceleratorTextOverride = ShortcutText("newtab");
        ReopenMenuItem.KeyboardAcceleratorTextOverride = ShortcutText("reopen");
        ReopenMenuItem.IsEnabled = _closedTabs.Count > 0;
        NewFolderMenuItem.KeyboardAcceleratorTextOverride = ShortcutText("folder");
        PrivateMenuItem.KeyboardAcceleratorTextOverride = ShortcutText("private");
        DownloadsMenuItem.KeyboardAcceleratorTextOverride = ShortcutText("downloads");
        HistoryMenuItem.KeyboardAcceleratorTextOverride = ShortcutText("history");
        SidebarMenuItem.KeyboardAcceleratorTextOverride = ShortcutText("sidebar");
        SettingsMenuItem.KeyboardAcceleratorTextOverride = ShortcutText("settings");
        var maximized = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
        MaximizeMenuItem.Text = maximized ? "Restore" : "Maximize";
        MaximizeMenuIcon.Glyph = maximized ? "" : "";
    }

    // As the system minimizes, so the menu this was clicked in is closed first (GhostPopups).
    private void Minimize_Click(object sender, RoutedEventArgs e) => _ghostPopups.Minimize();

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter) return;
        if (presenter.State == OverlappedPresenterState.Maximized) presenter.Restore();
        else presenter.Maximize();
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsWindow.Open(this);

    private void DragLayout_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateDragRegions();
        if (_commandBarOpen) PlaceCommandCard();
        // The page's whole-pixel shrink depends on its size: re-snap if the window changes during Peek.
        if (sender == PageArea && IsPeekShown && _closingPeekView is null) ShowPeekLayer(animate: false);
    }

    /// <summary>
    /// The window drags from the empty middle of the sidebar's top row and from the margins
    /// around the page. Everything else, the page included, gets clicks as usual.
    /// </summary>
    private void UpdateDragRegions()
    {
        if (Root.XamlRoot is null) return;

        var width = Root.ActualWidth;
        var height = Root.ActualHeight;
        // From layout, not rendered bounds: the page is scaled down while Peek is open.
        // And it's moved over (not narrowed) while the library is open.
        var margin = PageArea.Margin;
        var page = new Rect(
            SidebarColumn.ActualWidth + margin.Left + PageArea.Translation.X, margin.Top,
            Math.Max(0, width - SidebarColumn.ActualWidth - margin.Left - margin.Right),
            Math.Max(0, height - margin.Top - margin.Bottom));
        var rects = new List<Rect>
        {
            new(page.Left, 0, width - page.Left, page.Top),
            new(Math.Min(page.Right, width), 0, Math.Max(0, width - page.Right), height),
            new(page.Left, page.Bottom, width - page.Left, height - page.Bottom),
        };
        // Floating sidebar: its top row isn't always there, and the left edge is the hot zone.
        // (Not with the library over it.)
        if (!_sidebarHidden && !Library.IsHitTestVisible) rects.Add(BoundsOf(TitleDragSpacer));
        // The command bar's layer covers the window, and a click anywhere outside it closes it.
        if (_commandBarOpen) rects.Clear();

        var scale = Root.XamlRoot.RasterizationScale;
        var pixels = rects
            .Where(r => r.Width > 0 && r.Height > 0)
            .Select(r => new RectInt32(
                (int)Math.Round(r.X * scale), (int)Math.Round(r.Y * scale),
                (int)Math.Round(r.Width * scale), (int)Math.Round(r.Height * scale)))
            .ToArray();
        InputNonClientPointerSource.GetForWindowId(AppWindow.Id).SetRegionRects(NonClientRegionKind.Caption, pixels);
    }

    private Rect BoundsOf(FrameworkElement element) =>
        element.TransformToVisual(Root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    // ───────────────────────── Helpers ─────────────────────────

    /// <summary>Turns address-bar input into a URL: full URLs, bare domains, localhost, or a search.</summary>
    private static Uri ToUri(string input) => TryParseAddress(input, out var uri) ? uri : SearchUri(input);

    /// <summary>Full URLs, bare domains and localhost; false for anything that should be a search.</summary>
    internal static bool TryParseAddress(string input, out Uri uri)
    {
        var lower = input.ToLowerInvariant();

        // Local dev servers (ng serve on :4200 and friends) are plain http.
        if ((lower.StartsWith("localhost") || lower.StartsWith("127.0.0.1")) && !input.Contains(' ')
            && Uri.TryCreate("http://" + input, UriKind.Absolute, out var local))
        {
            uri = local;
            return true;
        }

        if (Uri.TryCreate(input, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps || absolute.Scheme == Uri.UriSchemeFile))
        {
            uri = absolute;
            return true;
        }

        var looksLikeDomain = !input.Contains(' ') && input.Contains('.') && !input.EndsWith('.');
        if (looksLikeDomain && Uri.TryCreate("https://" + input, UriKind.Absolute, out var withScheme))
        {
            uri = withScheme;
            return true;
        }

        uri = null!;
        return false;
    }

    private static Uri SearchUri(string query) => new("https://www.google.com/search?q=" + Uri.EscapeDataString(query));

    /// <summary>
    /// Rough same-site check on the last two host labels (mail.google.com ~ accounts.google.com).
    /// Good enough to start; it treats every *.co.uk as one site. Swap in a public-suffix list later.
    /// </summary>
    private static bool SameSite(Uri a, Uri b) =>
        string.Equals(SiteKey(a.Host), SiteKey(b.Host), StringComparison.OrdinalIgnoreCase);

    private static string SiteKey(string host)
    {
        var parts = host.Split('.');
        return parts.Length <= 2 ? host : string.Join('.', parts[^2..]);
    }

    private static string DisplayUrl(string? url)
    {
        if (string.IsNullOrEmpty(url) || url == "about:blank") return "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host)) return url;
        return uri.Host.StartsWith("www.") ? uri.Host[4..] : uri.Host;
    }

    /// <summary>
    /// Asks the engine for the page's icon as a PNG, so SVG favicons (GitHub, Hacker News)
    /// show too; BitmapImage can't decode SVG from a URL.
    /// </summary>
    /// <param name="remember">Keep it for the command bar's history suggestions (not in private windows).</param>
    private static async Task<BitmapImage?> LoadFaviconAsync(CoreWebView2 core, bool remember)
    {
        try
        {
            var source = core.Source;
            using var stream = await core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
            if (stream is null || stream.Size == 0) return null;
            var bytes = new byte[stream.Size];
            using (var reader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0)))
            {
                await reader.LoadAsync((uint)stream.Size);
                reader.ReadBytes(bytes);
            }
            var image = await History.DecodeAsync(bytes);
            // Kept for the command bar, which shows sites from history with the icon they had.
            if (remember && Uri.TryCreate(source, UriKind.Absolute, out var uri)) History.StoreIcon(uri.Authority, bytes, image);
            return image;
        }
        catch
        {
            // The tab closed mid-request, or the page has no usable icon.
            return null;
        }
    }

    private void ShowStartupError(Exception ex)
    {
        EmptyText.Text =
            "The web engine didn't start. Check that the Microsoft Edge WebView2 Runtime is installed " +
            $"(it ships with Windows 11), then restart Drift.\n\n{ex.Message}";
        EmptyText.Visibility = Visibility.Visible;
    }
}
