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
using Microsoft.UI.Xaml.Media.Animation;
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

    // The docked sidebar's width: dragged by its edge (SidebarResizing.cs), kept in the session.
    private double _sidebarWidth = SidebarResize.Default;
    private double SidebarWidth => _sidebarWidth;

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
              // The link's box goes with the press (the line of it pressed, for a link that wraps), for Peek to grow from.
              const boxOf = (link, x, y) => [...link.getClientRects()].find((r) => x >= r.left && x <= r.right && y >= r.top && y <= r.bottom) || link.getBoundingClientRect();
              const postLink = (x, y, link) => {
                const r = boxOf(link, x, y);
                post(token + ':link:' + [x, y, window.innerWidth, r.left, r.top, r.width, r.height].join(','));
              };
              // Ctrl+click or middle-click: the new tab it opens follows "switch to new tabs" (MainWindow.OnAsidePress).
              const postAside = (e) => post(token + ':aside:' + (+e.shiftKey));
              window.addEventListener('pointerdown', (e) => {
                if (!e.isTrusted || e.button > 1) return;
                const link = linkOf(e);
                if (!link) return;
                postLink(e.clientX, e.clientY, link);
                if (e.button === 1 || e.ctrlKey || e.metaKey) postAside(e);
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
                postLink(r.left + r.width / 2, r.top + r.height / 2, link);
                if (e.ctrlKey || e.metaKey) postAside(e);
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
              // Its first content drawn, often well before DOMContentLoaded: Peek shows the page then (RevealWhenLoaded).
              try {
                new PerformanceObserver((list, observer) => {
                  if (!list.getEntries().some((p) => p.name === 'first-contentful-paint')) return;
                  observer.disconnect();
                  post(token + ':painted');
                }).observe({ type: 'paint', buffered: true });
              } catch { }
            __PIP__
            })();
            """.Replace("__PIP__", PipScript() + MediaScript() + OutlookPushScript() + GooglePushScript() + SwipeScript()).Replace("__TOKEN__", ShortcutToken).Replace("__KEYS__", keys);
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

    internal static Task<CoreWebView2Environment> EnvironmentAsync() => _environment ??= StartEngineAsync();

    /// <summary>Starts the engine as Drift starts (App.OnLaunched), while the window is being made.</summary>
    internal static void StartEngine() => _ = EnvironmentAsync();

    /// <summary>
    /// The engine, on Drift's profile. The first time Drift runs, it waits for your answer about
    /// Microsoft sign-in (MicrosoftSignIn.cs), which can't be changed once the engine is going.
    /// </summary>
    private static async Task<CoreWebView2Environment> StartEngineAsync()
    {
        await SignInAnswered;
        var signIn = Settings.Current.MicrosoftSignIn;
        // Read off the UI thread, which is making the window meanwhile.
        var (runtime, gpus) = await Task.Run(() =>
        {
            string? version;
            try { version = CoreWebView2Environment.GetAvailableBrowserVersionString(); }
            catch { version = null; }
            return (version, DisplayAdapters());
        });
        EngineRuntime = runtime;
        var videoOnCpu = VideoDecode.Choose(gpus, Settings.Current.VideoOnCpuFor, EngineRuntime) != VideoDecoding.Gpu;
        var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, ProfileFolder(), EngineOptions(signIn, videoOnCpu));
        StartupTrace.Mark("engine ready");
        EngineSignsIn = signIn;
        EngineVideoOnCpu = videoOnCpu;
        return environment;
    }

    /// <summary>The PC's display adapters, as Windows lists them (readable without admin rights).</summary>
    private static List<Gpu> DisplayAdapters()
    {
        var gpus = new List<Gpu>();
        try
        {
            using var display = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (display is null) return gpus;
            foreach (var name in display.GetSubKeyNames())
            {
                try
                {
                    using var adapter = display.OpenSubKey(name);
                    if (VideoDecode.Parse(adapter?.GetValue("MatchingDeviceId") as string, adapter?.GetValue("DriverVersion") as string) is { } gpu)
                        gpus.Add(gpu);
                }
                catch
                {
                    // One we can't read ("Properties"): skip it.
                }
            }
        }
        catch
        {
            // None readable: the GPU, as normal.
        }
        return gpus;
    }

    /// <summary>The WebView2 runtime the engine runs on.</summary>
    private static string? EngineRuntime;
    /// <summary>Whether the running engine decodes video on the CPU (GpuProcessFailed).</summary>
    private static bool EngineVideoOnCpu;

    /// <summary>
    /// What the engine starts with. Every engine on one profile folder must start with the same
    /// options, or its pages fail to open (ERROR_INVALID_STATE): a setting here applies to the whole
    /// folder, and a space with its own would need a folder of its own.
    /// </summary>
    private static CoreWebView2EnvironmentOptions EngineOptions(bool signInWithWindows, bool videoOnCpu) => new()
    {
        // Video on the CPU only on graphics known to hang decoding it (VideoDecode), or where the GPU
        // kept failing (GpuFailures). The HEVC-only feature switch,
        // PlatformHEVCDecoderSupport, no longer does anything in runtime 157.
        AdditionalBrowserArguments = "--enable-features=OverlayScrollbar" + (videoOnCpu ? " --disable-accelerated-video-decode" : ""),
        AreBrowserExtensionsEnabled = true,
        // Outlook, Office, SharePoint… sign in with the Windows account, as in Edge.
        AllowSingleSignOnUsingOSPrimaryAccount = signInWithWindows,
    };

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
    // Where Peek's card grows from and goes back to (AimPeekCard): its scale and shift.
    private Vector3 _cardFromScale = new(PeekGrowFrom, PeekGrowFrom, 1), _cardFromShift, _cardToScale = new(PeekShrinkTo, PeekShrinkTo, 1), _cardToShift;
    private double _cardToBounce;
    private readonly DispatcherQueueTimer _sidebarDismissTimer;
    private readonly DispatcherQueueTimer _sidebarWatch;
    private readonly DispatcherQueueTimer _sessionTimer;
    private long? _pointerAtEdgeSince;
    private long? _pointerOffSidebarSince;
    private bool _syncingSelection;

    /// <summary>The Essentials of the Profile being worked in (as PinnedTabs): the one on screen, but for a moment (InProfile).</summary>
    public ObservableCollection<BrowserTab> EssentialTabs => _profile.Essentials;

    /// <summary>
    /// The pinned tabs and tabs of the Profile being worked in (Profiles.cs): the one on screen, but for a
    /// moment while a tab in another one changes (InProfile).
    /// </summary>
    public ObservableCollection<BrowserTab> PinnedTabs => _profile.Pinned;
    public ObservableCollection<BrowserTab> Tabs => _profile.Tabs;

    /// <summary>Every tab, in every Profile, each top of its sidebar to bottom.</summary>
    private IEnumerable<BrowserTab> AllTabs => Profiles.SelectMany(TabsIn);

    /// <summary>The tabs in the sidebar now: the Profile you're on.</summary>
    private IEnumerable<BrowserTab> ShownTabs => TabsIn(_profile);

    /// <summary>A Profile's tabs, top of its sidebar to bottom: its Essentials, its folders', its pinned tabs, its tabs.</summary>
    private static IEnumerable<BrowserTab> TabsIn(Profile profile) =>
        profile.Essentials.Concat(profile.Tree.AllTabs.Cast<BrowserTab>()).Concat(profile.Pinned.SelectMany(RegularTabsOf))
            .Concat(profile.GroupSplitRows.SelectMany(RegularTabsOf)).Concat(profile.Tabs.SelectMany(RegularTabsOf));

    /// <summary>The pinned tabs, those in a split too (its row is in their place in the pinned list).</summary>
    private IEnumerable<BrowserTab> PinnedPages => PinnedTabs.SelectMany(RegularTabsOf).Where(t => t.Place == TabPlace.Pinned);

    /// <summary>
    /// Every Profile's Essentials, pinned tabs and tab groups' tabs, the Profiles in order: where the integrations
    /// (Gmail, the calendars, Outlook) find their tabs, so they follow them whichever Profile is on screen.
    /// </summary>
    private IEnumerable<BrowserTab> PinnedInEveryProfile => Profiles.SelectMany(p => p.Essentials
        .Concat(p.Pinned.SelectMany(RegularTabsOf).Where(t => t.Place == TabPlace.Pinned))
        .Concat(p.Tree.AllTabs.Cast<BrowserTab>()));

    /// <summary>
    /// A row of the tab list or the pinned list: its tab, or for a split's row (Split.cs), the split's
    /// tabs that are in that row, left first: its regular and pinned tabs (Essentials and folder tabs
    /// are in their own places).
    /// </summary>
    private static IEnumerable<BrowserTab> RegularTabsOf(BrowserTab row) =>
        row is { IsSplitRow: true, Split: { } split } ? split.Pages.Where(t => t.Place is TabPlace.Regular or TabPlace.Pinned) : [row];

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

    // Every open window (a private one is a second), so a control in a list's row can find the one it's in.
    private static readonly List<MainWindow> OpenWindows = [];

    /// <summary>The window an element is in: for a row's control (Controls/), whose list template can't hand it one.</summary>
    internal static MainWindow Of(UIElement element) => OpenWindows.First(w => w.Content.XamlRoot == element.XamlRoot);

    public MainWindow(IEnumerable<Uri>? launchUris = null, bool isPrivate = false)
    {
        _private = isPrivate;
        if (launchUris is not null) _launchUris.AddRange(launchUris);
        SetUpProfiles();
        SetUpEngine();
        InitializeComponent();
        // The views the window is made of (Views/): what's done in them is handled here.
        MediaCard.Owner = this;
        CaptionBar.Owner = this;
        MeetingNotice.Owner = this;
        MeetingCard.Owner = this;
        CommandLayer.Owner = this;
        SwitcherLayer.Owner = this;
        Library.Owner = this;
        DownloadPeekCard.Owner = this;
        Toolbar.Owner = this;
        AddressBar.Owner = this;
        CrashPanel.Owner = this;
        // And the controls in its lists' rows (Controls/), which find it when they need it (Of).
        OpenWindows.Add(this);
        _ghostPopups = new GhostPopups(this);
        // The site settings button shows while the pointer is anywhere in the sidebar, over its buttons too.
        Sidebar.AddHandler(UIElement.PointerEnteredEvent, new PointerEventHandler(Sidebar_PointerEntered), true);
        Sidebar.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler(Sidebar_PointerExited), true);
        if (_private)
        {
            PrivateWindows.Add(this);
            PrivateTint.Visibility = Visibility.Visible;
            AddressBar.PrivateBadge.Visibility = Visibility.Visible;
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
        WatchClosing();
        Closed += (_, _) =>
        {
            IsOpen = false;
            OpenWindows.Remove(this);
            // First: a toast's time can run out while the rest closes (Press again to close's did, and crashed).
            foreach (var toast in new[] { BookmarkToast, CopiedToast, CloseAgainHint, UndoToast }) toast.Stop();
            _adBlock?.Close();
            _pdfTools?.Close();
            StopCalendar();
            StopMail();
            StopOutlook();
            StopLiveFolders();
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
            foreach (var window in PrivateWindows.ToList())
            {
                // Asked about already, with this one.
                window._closeConfirmed = true;
                window.Close();
            }
            PopupWindow.CloseAll();
            PlayerWindow.CloseAll();
            SettingsWindow.CloseIfOpen();
            History.SaveNow(wait: true);
        };
        Downloads.Changed += ShowDownloads;
        WireDownloadDrops();
        _sessionTimer = DispatcherQueue.CreateTimer();
        _sessionTimer.Interval = TimeSpan.FromSeconds(2);
        _sessionTimer.IsRepeating = false;
        _sessionTimer.Tick += (_, _) => SaveSession(wait: false);
        Settings.Changed += Settings_Changed;
        Root.Loaded += (_, _) => AskAboutSignInOnce();
        ApplyShortcuts();
        ShowNewTabHint();
        PeekExitAnimation.Completed += (_, _) => FinishPeekExit();
        PageArea.SizeChanged += (_, _) => PlacePeekCard();
        PeekFadeOutAnimation.Completed += (_, _) => FinishPeekExit();
        _sidebarDismissTimer = DispatcherQueue.CreateTimer();
        _sidebarDismissTimer.Interval = TimeSpan.FromMilliseconds(300);
        _sidebarDismissTimer.IsRepeating = false;
        _sidebarDismissTimer.Tick += SidebarDismissTimer_Tick;
        _sidebarWatch = DispatcherQueue.CreateTimer();
        _sidebarWatch.Interval = TimeSpan.FromMilliseconds(20);
        _sidebarWatch.Tick += SidebarWatch_Tick;
        StartCaptionWatch();

        foreach (var button in new[] { Toolbar.MenuButton, Toolbar.SidebarToggleButton, Toolbar.BackButton, Toolbar.ForwardButton, Toolbar.ReloadButton })
            AddPressFeedback(button, 0.95f);
        AddPressFeedback(AddressBar.Pill, 0.98f);
        AddPressFeedback(PeekCloseButton, 0.95f);
        AddPressFeedback(PeekToTabButton, 0.95f);
        // Each Profile's lists, its Essentials too, are wired as its sidebar is made (ProfilePages.cs).
        WatchForSlides();
        WireProfiles();
        SetUpLibrary();
        SetUpPageHandles();
        WarmIconPickerSoon();
    }

    /// <summary>
    /// Essentials fill the sidebar's width, as many across as EssentialsGrid says for how many there
    /// are and how wide it is: widening the sidebar fits more in a row. A short last row fills the width
    /// too, with wider tiles (EssentialsPanel). When the count across changes, the tiles glide from where
    /// they were to their new places and widths rather than jump.
    /// </summary>
    private void LayoutEssentials() => LayoutEssentials(CurrentPage);

    /// <summary>A Profile's Essentials, each sidebar's own (ProfilePages.cs); only the one on screen's glide.</summary>
    private void LayoutEssentials(ProfilePageView page)
    {
        var list = page.EssentialList;
        if (list.ItemsPanelRoot is not EssentialsPanel panel) return;
        var width = list.ActualWidth - list.Padding.Left - list.Padding.Right;
        if (width <= 0) return;
        var onScreen = page == CurrentPage;
        // Counting a tab that's being dragged in, so the grid shows the layout it would get.
        var columns = EssentialsGrid.Columns(page.Profile.Essentials.Count + (onScreen ? _essentialsArriving : 0), width);
        if (panel.Columns == columns) return;
        // (Not mid-drag: the tiles are showing where a drop would put them, springing there themselves.)
        if (onScreen && _dragTab is null && _restored)
        {
            // The groups below move as the rows change: they slide too.
            NoteGroupSlideStart();
            panel.Animate(duration: 0.3, bounce: 0);
        }
        panel.Columns = columns;
    }

    private async void Root_Loaded(object sender, RoutedEventArgs e)
    {
        StartupTrace.Mark("loaded");
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
            if (session?.SidebarWidth is { } sidebarWidth) RestoreSidebarWidth(sidebarWidth);
            _pullsFolderMade = session?.PullsFolderMade ?? false;
            var opening = new List<Task<BrowserTab>>();
            if (session is null)
            {
                foreach (var url in DefaultPinned)
                    opening.Add(CreateTabAsync(new Uri(url), TabPlace.Essential, activate: false));
            }
            else
            {
                // The Profiles, each with its folders and data; the one you were on is the one on screen.
                var savedProfiles = session.ProfileList();
                var profiles = RestoreProfiles(savedProfiles);
                var folders = profiles.Select((s, i) => s.Tree.Load(savedProfiles[i].Folders)).ToList();
                var shownProfile = profiles[Math.Clamp(session.Profile, 0, profiles.Count - 1)];
                var engines = profiles.Select(s => s.Engine.IsEngineDefault ? null : s.Engine.Id).ToList();
                // Each saved tab's tab, by position, for putting splits back together.
                var restoring = new Task<BrowserTab>?[session.Tabs.Count];
                _restoring = true;
                try
                {
                    for (var i = 0; i < session.Tabs.Count; i++)
                    {
                        var saved = session.Tabs[i];
                        if (!Uri.TryCreate(saved.Url, UriKind.Absolute, out var uri)) continue;
                        Uri.TryCreate(saved.Home, UriKind.Absolute, out var home);
                        var place = Enum.TryParse<TabPlace>(saved.Place, out var p) ? p
                            : saved.Pinned ? TabPlace.Essential : TabPlace.Regular;
                        // An Essential in a file from when they were a profile's goes to the first Profile on it.
                        var profileAt = place == TabPlace.Essential ? saved.EssentialProfileIn(engines) : saved.ProfileIn(profiles.Count);
                        TabFolder? folder = null;
                        if (place == TabPlace.Folder && (saved.Folder is null || !folders[profileAt].TryGetValue(saved.Folder, out folder)))
                            place = TabPlace.Pinned;
                        var index = i;
                        // Into its own Profile.
                        InProfile(profiles[profileAt], () =>
                        {
                            opening.Add(restoring[index] = CreateTabAsync(uri, place, activate: index == session.Active,
                                deferLoad: true, title: saved.Title, home: home, folder: folder, name: saved.Name, icon: saved.Icon));
                            // It's in its folder already (added before the first await): note where it goes.
                            if (folder is not null) _folderTree.AddSaved((IFolderTab)folder.Children[^1], folder, saved.Position);
                        });
                        // The tab you were last on in each Profile, to come back to there.
                        for (var s = 0; s < profiles.Count; s++)
                            if (savedProfiles[s].Active == i) profiles[s].LastActive = _tabJustMade;
                    }
                }
                finally
                {
                    _restoring = false;
                }
                foreach (var profile in profiles) profile.Tree.Arrange();
                ShowProfile(shownProfile, show: _active);
                // The tab you were on didn't come back (its address no longer reads): the first one is in front. With
                // none in front (a Profile with no tabs yet), none is.
                if (_active is null && session.Active >= 0 && ShownTabs.FirstOrDefault() is { } first) Activate(first);
                RefreshFolderRows();
                StartupTrace.Mark($"{opening.Count} tabs in the sidebar");
                await Task.WhenAll(opening);
                RestoreSplits(restoring.Select(t => t?.Result).ToList(), session, profiles);
            }
            await Task.WhenAll(opening);
            _restored = true;
            // Of the tabs put back, only the one in front has been visited.
            _recentTabs.Clear();
            if (_active is not null) NoteRecentTab(_active);
            // The first Peek starts loading at once too, once the tabs have had their start.
            PrepareSparePeekViewSoon(5000);
            LoadEssentialsSoon();
            StartCalendar();
            StartMail();
            StartOutlook();
            StartLiveFolders();
            // Settings › Accounts shows the Windows account straight away when it's opened.
            _ = WindowsAccount.PreloadAsync();
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
    /// otherwise every tab where it is now. <paramref name="everything"/>: every tab regardless, for a restart.
    /// </summary>
    private void SaveSession(bool wait, bool everything = false)
    {
        _sessionTimer.Stop();
        if (_private) return;
        var keepAll = everything || Settings.Current.OnClose == CloseBehavior.KeepTabs;
        var data = new SessionData
        {
            SidebarWidth = _sidebarWidth,
            PullsFolderMade = _pullsFolderMade,
            Profile = Profiles.IndexOf(_profileShown),
            Profiles = Profiles.Select(s => new SessionProfile
            {
                Name = s.Name,
                Icon = s.Icon,
                Engine = s.Engine.IsEngineDefault ? null : s.Engine.Id,
                Folders = s.Tree.Save(),
            }).ToList(),
        };
        var savedAt = new Dictionary<BrowserTab, int>();
        void SaveTab(BrowserTab tab, int profile, FolderTree? tree)
        {
            if (!keepAll && !tab.IsPinned) return;
            var home = tab.HomeUri?.ToString();
            var url = keepAll ? tab.Url : home;
            if (string.IsNullOrEmpty(url) || url == "about:blank") url = home;
            if (string.IsNullOrEmpty(url)) return;
            if (tab == _active) data.Active = data.Tabs.Count;
            // The Profile it was last on in.
            for (var s = 0; s < Profiles.Count; s++)
                if (Profiles[s].LastActive == tab) data.Profiles[s].Active = data.Tabs.Count;
            savedAt[tab] = data.Tabs.Count;
            data.Tabs.Add(new SessionTab
            {
                Url = url,
                Title = url == tab.Url ? tab.PageTitle : new Uri(url).Host,
                Name = tab.Name,
                Icon = tab.Icon,
                Pinned = tab.IsPinned,
                Place = tab.Place.ToString(),
                Home = home,
                Folder = tab.Folder?.Id,
                Position = tree?.PositionOf(tab),
                // An Essential says its Profile, the first too: without one, it's from a file before they were a Profile's.
                Profile = profile > 0 || tab.Place == TabPlace.Essential ? profile : null,
            });
        }
        for (var s = 0; s < Profiles.Count; s++)
            foreach (var tab in TabsIn(Profiles[s])) SaveTab(tab, s, Profiles[s].Tree);
        // Splits with at least two of their tabs kept, with where their rows are in their Profile's tab list (one
        // in the pinned list goes back to its pinned tab's place, whatever its row's number); their widths if
        // they all were.
        for (var s = 0; s < Profiles.Count; s++)
            foreach (var (rows, pinned) in new (IReadOnlyList<BrowserTab>, bool)[] { (Profiles[s].Pinned, true), (Profiles[s].Tabs, false), (Profiles[s].GroupSplitRows, false) })
                for (var row = 0; row < rows.Count; row++)
                {
                    if (rows[row] is not { IsSplitRow: true, Split: { } split }) continue;
                    var kept = split.Pages.Where(savedAt.ContainsKey).Select(p => savedAt[p]).ToList();
                    if (kept.Count < 2) continue;
                    data.Splits.Add(new SessionSplit
                    {
                        Pages = kept,
                        Shares = kept.Count == split.Count ? split.Shares.ToList() : null,
                        Row = pinned ? -1 : row,
                        Profile = s > 0 ? s : null,
                    });
                }
        data.Closed = _closedTabs.TakeLast(MaxClosedTabs).ToList();
        Session.Save(data, wait);
    }

    // ───────────────────────── Tabs ─────────────────────────

    /// <param name="deferLoad">Restored tabs: don't load the page until the tab is first shown.</param>
    /// <param name="title">The title to show until the page gives its own.</param>
    /// <param name="home">For pinned tabs, the page they're pinned to, when it isn't <paramref name="uri"/>.</param>
    /// <param name="index">Where in its group (reopening a closed tab); by default, last.</param>
    /// <param name="folder">For a tab in a folder (Place Folder), the folder.</param>
    /// <param name="name">The name you gave it (Rename…).</param>
    /// <param name="icon">The icon you chose for it (Change icon…).</param>
    /// <param name="forPage">A page's own window (OpenTabForPage): handed to the page, which says where it goes.</param>
    /// <param name="openedFrom">The tab whose link opened it (BrowserTab.OpenedFrom).</param>
    private async Task<BrowserTab> CreateTabAsync(Uri uri, TabPlace place, bool activate,
        bool deferLoad = false, string? title = null, Uri? home = null, int? index = null, TabFolder? folder = null,
        string? name = null, string? icon = null, CoreWebView2NewWindowRequestedEventArgs? forPage = null,
        BrowserTab? openedFrom = null, EngineProfile? engine = null)
    {
        if (place == TabPlace.Folder && folder is null) place = TabPlace.Pinned;
        var view = NewTabView();
        WebHost.Children.Add(view);

        // In the sidebar right away, in order, while the engine starts; it navigates once it has.
        var tab = new BrowserTab(view, place)
        {
            Title = string.IsNullOrWhiteSpace(title) ? uri.Host : title,
            HomeUri = place == TabPlace.Regular || folder is { IsPinned: false } ? null : (home ?? uri),
            PendingUri = forPage is null ? uri : null,
            IsUnloaded = deferLoad,
            Name = name,
            OpenedFrom = openedFrom is null ? null : new WeakReference<BrowserTab>(openedFrom),
            // Its Profile's profile (ProfileData.cs).
            Engine = engine ?? _profile.Engine,
        };
        _tabJustMade = tab;
        if (icon is not null) _ = SetTabIconAsync(tab, icon);
        if (folder is not null && place == TabPlace.Folder)
        {
            // Among its folder's Profile's folders (a live folder fills in whichever Profile it's in).
            TreeOf(folder).Add(tab, folder, index);
            RefreshFolderRows();
        }
        else
        {
            var list = CollectionFor(place);
            if (index is { } at) list.Insert(Math.Clamp(at, 0, list.Count), tab);
            else list.Add(tab);
        }
        // While the session is being put back, only the tab you were on comes to the front, not the first one back.
        var show = activate || (_active is null && !_restoring);
        // Put back asleep, a tab starts its engine only when it's first shown (LoadShown), like an unloaded
        // tab: when every restored tab started one, the page in front waited behind all of them.
        var starting = deferLoad && !show && forPage is null ? Task.CompletedTask : StartTabViewAsync(tab, load: !deferLoad, forPage);
        if (show) Activate(tab);
        if (deferLoad) ShowSavedIcon(tab, uri);
        await starting;
        return tab;
    }

    // Set while Root_Loaded puts the session back (CreateTabAsync).
    private bool _restoring;

    // The tab CreateTabAsync last made, there as soon as it's called (it's in the sidebar before its first await).
    private BrowserTab? _tabJustMade;

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
    /// <param name="forPage">A page's own window: handed to the page instead, which navigates it.</param>
    private async Task StartTabViewAsync(BrowserTab tab, bool load, CoreWebView2NewWindowRequestedEventArgs? forPage = null)
    {
        var view = tab.View;
        _startingViews.Add(view);
        try
        {
            await InitWebViewAsync(view, EngineOf(tab));
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
        if (forPage is not null)
        {
            // Before it has been anywhere, as the engine requires; the page sends it where it's going.
            forPage.NewWindow = view.CoreWebView2;
            tab.OpenedByPage = true;
        }
        else if (load || tab == _active) LoadPending(tab);
        SaveSessionSoon();
    }

    /// <summary>Navigates a tab to where it's waiting to go, once its engine is ready.</summary>
    private static void LoadPending(BrowserTab tab)
    {
        if (tab.PendingUri is not { } uri || tab.View.CoreWebView2 is null) return;
        tab.PendingUri = null;
        tab.IsUnloaded = false;
        StartupTrace.Mark($"navigate {uri.Host}");
        Navigate(tab, uri);
    }

    /// <summary>
    /// Essentials are there to be ready: put back asleep like every tab, they load just after the
    /// tab in front has had its start, a little apart. One you unload later stays unloaded till opened.
    /// </summary>
    private async void LoadEssentialsSoon()
    {
        if (!Settings.Current.LoadEssentialsAtStart) return;
        await Task.Delay(1500);
        foreach (var tab in EssentialTabs.ToList())
        {
            if (!tab.IsUnloaded || tab.PendingUri is null || !EssentialTabs.Contains(tab)) continue;
            LoadShown(tab);
            await Task.Delay(250);
        }
    }

    /// <summary>A restored tab that hasn't loaded shows its site's icon from last time.</summary>
    private static async void ShowSavedIcon(BrowserTab tab, Uri uri)
    {
        if (await History.IconAsync(uri.Authority) is { } icon && tab.Favicon is null) tab.Favicon = icon;
    }

    private PdfTools? _pdfTools;

    private async Task InitWebViewAsync(WebView2 view, EngineProfile profile)
    {
        // Files dragged in from Explorer or another app reach the page (WinUI's WebView2 turns them away by default).
        view.AllowDrop = true;
        var environment = await EnvironmentAsync();
        await EnsureEngineAsync(view, environment, _private, profile.Id);
        var core = view.CoreWebView2;
        if (_private) Extensions.AttachPrivate(core.Profile);
        else if (profile.IsEngineDefault) Extensions.Attach(core.Profile);
        else Extensions.AttachOther(core.Profile);
        Downloads.UseProfile(core.Profile);
        // Draw, Erase, Highlight and Add text in PDFs (PdfTools.cs): watching from the window's first page on.
        (_pdfTools ??= new PdfTools(WinRT.Interop.WindowNative.GetWindowHandle(this), _private)).Start();
        core.DownloadStarting += (_, e) =>
        {
            var path = e.ResultFilePath;
            Downloads.Track(e, _private);
            FlyDownloadIn(view, path);
        };
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
    /// <param name="cover">
    /// Over the page in its background colour: shown at once on load, the page, and the cover fades instead.
    /// A WebView2 fading in (in the Peek card) stopped everything else in the card drawing while it did.
    /// </param>
    /// <returns>Shows it now, if it hasn't been: when the page reports its first content drawn ("painted").</returns>
    private static Action RevealWhenLoaded(WebView2 view, UIElement? cover = null)
    {
        var core = view.CoreWebView2;
        view.Opacity = 0;
        if (cover is null) view.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(150) };
        else
        {
            view.OpacityTransition = null;
            cover.OpacityTransition = null;
            cover.Opacity = 1;
        }

        // The handlers stay on, doing nothing after the first: taking one off while WebView2 is
        // calling its handlers can leave it calling a freed one.
        var revealed = false;
        void Reveal()
        {
            if (revealed) return;
            revealed = true;
            StartupTrace.Done($"shown {view.Source?.Host}");
            view.Opacity = 1;
            if (cover is null) return;
            PeekTrace("revealed");
            cover.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(150) };
            cover.Opacity = 0;
        }
        // With a cover, once the page has drawn a frame: DOMContentLoaded comes before that, and the cover
        // faded over a blank page that then appeared all at once.
        async void RevealDrawn()
        {
            if (revealed) return;
            if (cover is not null)
                await Deadline.Within(EvaluateAsync(core, "new Promise((done) => requestAnimationFrame(() => requestAnimationFrame(() => done(1))))", gesture: false),
                    TimeSpan.FromMilliseconds(400), fallback: null);
            Reveal();
        }
        void OnContentLoaded(CoreWebView2 sender, CoreWebView2DOMContentLoadedEventArgs e) => RevealDrawn();
        // Covers loads that fail or are cancelled before any document arrives.
        void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs e) => RevealDrawn();
        core.DOMContentLoaded += OnContentLoaded;
        core.NavigationCompleted += OnNavigationCompleted;
        return RevealDrawn;
    }

    private void OpenInNewTab(Uri uri) => OpenInNewTab(uri, from: null);

    /// <param name="from">The tab whose link opened it, if a link did.</param>
    private void OpenInNewTab(Uri uri, BrowserTab? from) => _ = CreateTabAsync(uri, TabPlace.Regular, activate: true, openedFrom: from);

    /// <summary>A link you chose to open in a new tab: in front or behind, as the setting says (Shift turns it round).</summary>
    /// <param name="from">The tab the link was in; null from Peek and bookmarks.</param>
    private void OpenLinkInNewTab(Uri uri, bool shift = false, BrowserTab? from = null) =>
        _ = CreateTabAsync(uri, TabPlace.Regular, activate: NewTabChoice.ComesToFront(chosen: true, Settings.Current.SwitchToNewTabs, shift),
            openedFrom: from);

    /// <summary>
    /// A page opening a window with no address, to fill in itself (window.open(), then where it goes): a new tab,
    /// handed back to the page as that window, so it can. A tab of Drift's own left the page with nothing back, and
    /// the tab stayed blank, with no name.
    /// </summary>
    /// <param name="from">The tab whose page asked, for a Ctrl+click's choice; null from Peek.</param>
    private async void OpenTabForPage(CoreWebView2NewWindowRequestedEventArgs e, BrowserTab? from)
    {
        // The page waits for its window until the deferral completes.
        var deferral = e.GetDeferral();
        try
        {
            var chosen = from is not null && NewTabChoice.PressedJustNow(from.AsidePressTime, Environment.TickCount64);
            var shift = from?.AsidePressShift ?? false;
            if (from is not null) from.AsidePressTime = 0;
            // In the opener's Profile and profile, which the engine needs for the handover: a tab in another Profile opens
            // its window there.
            var opening = Task.CompletedTask;
            var profile = from is null ? null : ProfileOf(from);
            InProfile(profile ?? _profile, () => opening = CreateTabAsync(new Uri("about:blank"), TabPlace.Regular,
                activate: NewTabChoice.ComesToFront(chosen, Settings.Current.SwitchToNewTabs, shift), forPage: e, openedFrom: from,
                engine: from is null ? null : EngineOf(from)));
            await opening;
        }
        catch (Exception error)
        {
            App.LogCrash(error);
        }
        finally
        {
            // Without a window (its engine didn't start), the page gets none, rather than the engine's own.
            e.Handled = true;
            deferral.Complete();
        }
    }

    private void WireTab(BrowserTab tab)
    {
        var core = tab.View.CoreWebView2;

        core.DocumentTitleChanged += (_, _) =>
        {
            tab.Title = string.IsNullOrWhiteSpace(core.DocumentTitle) ? DisplayUrl(core.Source) : LiveTitle(tab, core.DocumentTitle);
            if (!_private) History.SetTitle(core.Source, core.DocumentTitle);
            if (tab == _active) UpdateChrome();
        };
        if (!_private) RecordHistory(core);

        core.FaviconChanged += async (_, _) => tab.Favicon = await LoadFaviconAsync(core, remember: !_private);

        core.SourceChanged += (_, _) =>
        {
            var before = tab.LastUrl;
            tab.LastUrl = core.Source;
            if (tab == _active) UpdateChrome();
            SaveSessionSoon();
            NoteLivePage(before, core.Source);
        };
        core.ProcessFailed += (_, e) => OnProcessFailed(tab, e);
        // window.close() in a tab a page opened as its window (OpenTabForPage), as sign-ins do when they're done.
        // After the event, not in it: closing a view while it's calling its handlers can crash the engine.
        core.WindowCloseRequested += (_, _) =>
        {
            if (tab.OpenedByPage) DispatcherQueue.TryEnqueue(() => { if (AllTabs.Contains(tab)) RemoveTab(tab); });
        };
        // The engine shows its own warning page; if you go on to the site anyway, the site info says so.
        core.ServerCertificateErrorDetected += (_, e) =>
        {
            if (Uri.TryCreate(e.RequestUri, UriKind.Absolute, out var bad)) tab.CertificateErrorHost = bad.Host;
        };
        core.HistoryChanged += (_, _) => { if (tab == _active) UpdateChrome(); };
        core.WebMessageReceived += (_, e) => OnWebMessage(e, tab);
        core.ContextMenuRequested += (_, e) => OpenLinkInNewTabItem(core, e, tab);
        WireAudio(tab, core);
        WirePicture(tab, core);
        WireSplitFocus(tab, tab.View);

        // Links that ask for a new window (target="_blank", window.open).
        // Popups (sign-in windows and the like) stay popups. Otherwise, from a pinned tab they
        // open in Peek; from a normal tab, in a new tab.
        core.NewWindowRequested += (_, e) =>
        {
            // A page floating itself (Pip.cs), opening its window: a small window of its own, not Peek or a tab.
            if (e.Uri == "about:blank" && IsPipWindow(tab))
            {
                PlayerWindow.Show(e, this, 0, 0, _private, EngineOf(tab).Id);
                return;
            }
            if (PopupWindow.IsPopup(e))
            {
                PopupWindow.Show(e, this, OpenInNewTab, _private, EngineOf(tab).Id);
                return;
            }
            if (NewTabChoice.PageFillsItIn(e.Uri))
            {
                OpenTabForPage(e, tab);
                return;
            }
            e.Handled = true;
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var target)) return;
            // A Ctrl+click or middle-click just now (the page said so as it was pressed): you chose a new tab.
            var aside = NewTabChoice.PressedJustNow(tab.AsidePressTime, Environment.TickCount64);
            tab.AsidePressTime = 0;
            if (tab.IsPinned) OpenPeek(target, tab);
            else if (aside) OpenLinkInNewTab(target, tab.AsidePressShift, from: tab);
            else OpenInNewTab(target, from: tab);
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
        WireHistoryPictures(tab, core);
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
        core.NavigationCompleted += (_, e) =>
        {
            if (stillPeek?.Invoke() == false) return;
            History.SetTitle(core.Source, core.DocumentTitle);
            // A site that says how to search it, for the command bar (SiteSearchBar.cs).
            if (e.IsSuccess) SiteSearchStore.Notice(core);
        };
    }

    /// <summary>
    /// The right-click menu's "Open link in new window" opens a new tab instead (Drift has one window,
    /// and tabs in its sidebar), and says so. Left to the engine it would go wherever the page's own
    /// new-window links go: in Peek, Peek itself; from a pinned tab, Peek. A tab's page also gets Open
    /// link in Peek and Open link in split view after it, as Zen and Arc have (<paramref name="tab"/>,
    /// null in Peek).
    /// </summary>
    private void OpenLinkInNewTabItem(CoreWebView2 core, CoreWebView2ContextMenuRequestedEventArgs e, BrowserTab? tab = null)
    {
        var items = e.MenuItems;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Name != "openLinkInNewWindow") continue;
            if (!Uri.TryCreate(e.ContextMenuTarget.LinkUri, UriKind.Absolute, out var link)) return;
            var item = core.Environment.CreateContextMenuItem("Open link in new tab", null, CoreWebView2ContextMenuItemKind.Command);
            item.CustomItemSelected += (_, _) => DispatcherQueue.TryEnqueue(() => OpenLinkInNewTab(link, from: tab));
            items.RemoveAt(i);
            items.Insert(i, item);
            if (tab is null || link.Scheme is not ("http" or "https")) return;
            var peek = core.Environment.CreateContextMenuItem("Open link in Peek", null, CoreWebView2ContextMenuItemKind.Command);
            peek.CustomItemSelected += (_, _) => DispatcherQueue.TryEnqueue(() => OpenPeek(link, tab));
            items.Insert(i + 1, peek);
            // A split already four pages wide has no room for another.
            if (tab.Split is { IsFull: true }) return;
            var split = core.Environment.CreateContextMenuItem("Open link in split view", null, CoreWebView2ContextMenuItemKind.Command);
            split.CustomItemSelected += (_, _) => DispatcherQueue.TryEnqueue(() => _ = OpenLinkInSplitAsync(link, tab));
            items.Insert(i + 2, split);
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
        // A tab in another Profile (from the command bar, the mini player…): that Profile comes, with it in front.
        if (ProfileOf(tab) is { } profile && profile != _profile)
        {
            ShowProfile(profile, show: tab);
            return;
        }
        var left = _active;
        _active = tab;
        // What each Profile comes back to, an Essential too.
        _profile.LastActive = tab;
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
        // Before they start loading: an unloaded page's last look, under it till it's drawn (SwipePage.cs).
        ShowLastLooks();
        foreach (var page in _shownPages) LoadShown(page);
        SaveSessionSoon();

        // A tab in a closed folder shows under its header while it's in front, and after.
        NoteFolderTabShown(tab);
        MarkInSidebar(tab);

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
    private async void CloseTab(BrowserTab tab)
    {
        var close = TabCloses.First(hasPeek: tab == _peekOwner && _peekView is not null, tab.IsPinned);
        if (close == TabClose.ClosePeek)
        {
            ClosePeek(animate: true);
            return;
        }
        // Its page goes either way: not if it has unsaved changes you'd keep (Confirm.cs).
        if (!await MayLeaveAsync([tab])) return;
        if (close == TabClose.Unload) UnloadTab(tab);
        else RemoveTab(tab);
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
        // In another Profile, worked on there (Profiles.cs).
        if (ProfileOf(tab) is { } profile && profile != _profile)
        {
            InProfile(profile, () => UnloadTab(tab));
            return;
        }
        // Its page goes, so it can't stay beside another (Split.cs).
        LeaveSplit(tab);
        if (tab == _peekOwner) ClosePeek(animate: false);
        tab.Load.Reset();

        if (tab == _active)
        {
            // In this Profile: switching Profile is for you to do.
            var next = _recentTabs.FirstOrDefault(t => t != tab && !t.IsUnloaded && IsShown(t))
                ?? ShownTabs.FirstOrDefault(t => t != tab);
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
        tab.IsPlayingAudio = tab.IsMuted = tab.IsMedia = false;
        tab.AudioSince = null;
        UpdateMediaBar();
        if (home is not null)
        {
            var saved = History.All.FirstOrDefault(h => string.Equals(h.Url.TrimEnd('/'), home.ToString().TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
            tab.Title = string.IsNullOrWhiteSpace(saved?.Title) ? home.Host : LiveTitle(tab, saved!.Title);
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
    /// <param name="remember">For Reopen closed tab; not for a live folder's finished pull request (LiveFolders.cs).</param>
    private void RemoveTab(BrowserTab tab, bool remember = true)
    {
        // In another Profile (a page there closing itself), taken out of that one's lists (Profiles.cs).
        if (ProfileOf(tab) is { } profile && profile != _profile)
        {
            InProfile(profile, () => RemoveTab(tab, remember));
            return;
        }
        if (tab.Split is { } split)
        {
            // Out of its split first; the page beside it is the one you're on.
            TakeOutOfSplit(tab, next: _active == tab ? split.Beside(tab) : null);
        }
        if (tab == _peekOwner) ClosePeek(animate: false);
        BrowserTab? neighbour;
        if (tab.Place == TabPlace.Folder)
        {
            var index = tab.Folder?.Children.IndexOf(tab) ?? -1;
            if (index < 0) return;
            if (remember) RememberClosed(tab, index);
            // The next tab showing in its group list, else the one before.
            var showing = GroupRowsOf(tab).OfType<BrowserTab>().ToList();
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
            if (remember) RememberClosed(tab, index);

            _syncingSelection = true;
            list.RemoveAt(index);
            _syncingSelection = false;
            neighbour = list.Count > 0 ? list[Math.Min(index, list.Count - 1)] : null;
        }

        (tab.View.Parent as Panel)?.Children.Remove(tab.View);
        try { tab.View.Close(); } catch { }
        Unpick(tab);
        _recentTabs.Remove(tab);
        _shownPages.Remove(tab);
        UpdateMediaBar();
        SaveSessionSoon();

        if (_active == tab)
        {
            _active = null;
            // The neighbour in the same list, else the tab you were on before.
            var next = neighbour ?? _recentTabs.FirstOrDefault(IsShown) ?? ShownTabs.FirstOrDefault();
            if (next is not null) Activate(next);
            else UpdateChrome();
        }
    }

    /// <summary>A tab row's end button (TabRowTemplate): a pinned tab's −/×, a regular tab's ×.</summary>
    internal void RowAction_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is BrowserTab { IsPinned: true }) PinnedAction_Click(sender, e);
        else CloseTab_Click(sender, e);
    }

    /// <summary>A pinned row's hover button: "−" unloads the tab; on an unloaded tab, "×" removes it.</summary>
    private async void PinnedAction_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not BrowserTab tab) return;
        if (tab.IsUnloaded) RemoveTab(tab);
        else if (!ClosePeekOf(tab) && await MayLeaveAsync([tab])) UnloadTab(tab);
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
        // Before it leaves: a tab in an unpinned group wasn't kept.
        var wasPinned = tab.IsPinned;
        _syncingSelection = true;
        DetachTab(tab);
        if (place == TabPlace.Regular) tab.HomeUri = null;
        else if (!wasPinned) tab.HomeUri = Uri.TryCreate(tab.Url, UriKind.Absolute, out var here) ? here : null;
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
        // From the split page you're not on (a link there can open Peek before clicking into the
        // page has made it the one you're on): it becomes the one you're on, as Peek is its.
        if (owner != _active && _shownPages.Contains(owner)) Activate(owner);
        _peekPending = uri;
        _peekOwner = owner;
        owner.HasPeek = true;
        PeekTrace("open " + uri.Host);

        // A previous Peek may still be fading out; finish it now rather than fight its animation.
        FinishPeekExit();
        var press = TakeLinkPress(owner);
        AimPeekCard(owner, press?.Point, press?.Box);
        // From a link: a moment later, the card grows out of it.
        if (press is not null && _uiSettings.AnimationsEnabled) ShowPeekLayerSoon(owner);
        else ShowPeekLayer(animate: true);

        // Hidden until its page has content (RevealWhenLoaded): the card grows in its themed colour.
        // The view made ready ahead, if there is one, so the page starts loading at once.
        var (view, ready) = TakeSparePeekView(EngineOf(owner));
        _peekView = view;
        _peekZoom = 1;

        try
        {
            await ready;
        }
        catch
        {
            if (_peekView == view) ClosePeek(animate: false);
            return;
        }

        PeekTrace("engine ready");
        // Dismissed while the engine was starting.
        if (_peekView != view)
        {
            try { view.Close(); } catch { }
            return;
        }

        RoundCorners(view, 5);
        var revealNow = RevealWhenLoaded(view, PeekCover);
        var core = view.CoreWebView2;
        // Shown as soon as the page has drawn something, rather than once its whole document has parsed.
        var painted = ShortcutToken + ":painted";
        core.WebMessageReceived += (_, e) =>
        {
            string message;
            try { message = e.TryGetWebMessageAsString(); }
            catch { return; }
            if (_peekView == view && message == painted)
            {
                PeekTrace("painted");
                revealNow();
            }
        };
        if (Tracing)
        {
            core.NavigationStarting += (_, _) => PeekTrace("navigation starting");
            core.ContentLoading += (_, _) => PeekTrace("content loading");
            core.DOMContentLoaded += (_, _) => PeekTrace("dom content loaded");
            core.NavigationCompleted += (_, _) => PeekTrace("navigation completed");
        }
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
                PopupWindow.Show(e, this, OpenInNewTab, _private, _peekProfile.Id);
                return;
            }
            // A window the page fills in itself can't be Peek's own page, the very page that's to fill it.
            if (NewTabChoice.PageFillsItIn(e.Uri))
            {
                OpenTabForPage(e, null);
                return;
            }
            e.Handled = true;
            core.Navigate(e.Uri);
        };

        view.Source = _peekPending ?? uri;
        if (IsPeekShown) view.Focus(FocusState.Programmatic);
        // And one ready for the next Peek, once this page has had a head start.
        PrepareSparePeekViewSoon();
    }

    // ── A view ready for the next Peek ──
    // A new view's engine takes ~200ms to start, and its page can't start loading until it has. So one is
    // kept ready, hidden under Peek's page and never taking a click, and the next Peek takes it and starts
    // loading at once.
    private WebView2? _sparePeekView;
    private Task? _sparePeekReady;
    private Task<CoreWebView2Environment>? _sparePeekEngine;
    // The profile the spare view is in, and Peek's page's: its tab's (ProfileData.cs).
    private EngineProfile? _sparePeekProfile;
    private EngineProfile _peekProfile = null!;

    private (WebView2 View, Task Ready) TakeSparePeekView(EngineProfile profile)
    {
        _peekProfile = profile;
        // Not one from an engine that has since stopped (EngineStopped), nor one in another profile.
        if (_sparePeekView is { } spare && _sparePeekReady is { IsFaulted: false } ready && _sparePeekEngine == _environment && _sparePeekProfile == profile)
        {
            _sparePeekView = null;
            _sparePeekReady = null;
            spare.IsHitTestVisible = true;
            PeekTrace("view ready ahead" + (ready.IsCompleted ? "" : " (still starting)"));
            return (spare, ready);
        }
        DropSparePeekView();
        PeekTrace("new view");
        var view = new WebView2 { Opacity = 0 };
        PeekWebHost.Children.Add(view);
        return (view, InitWebViewAsync(view, profile));
    }

    /// <summary>Makes a view ready for the next Peek in a moment, so it doesn't compete with what's loading now.</summary>
    private void PrepareSparePeekViewSoon(int delayMs = 1500)
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.IsRepeating = false;
        timer.Interval = TimeSpan.FromMilliseconds(delayMs);
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            PrepareSparePeekView();
        };
        timer.Start();
    }

    private async void PrepareSparePeekView()
    {
        if (_sparePeekView is not null || _closeConfirmed) return;
        var view = new WebView2 { Opacity = 0, IsHitTestVisible = false };
        PeekWebHost.Children.Insert(0, view);
        _sparePeekView = view;
        // In the profile of the Profile you're on, where the next Peek's most likely to be.
        _sparePeekProfile = _profileShown.Engine;
        var ready = InitWebViewAsync(view, _sparePeekProfile);
        _sparePeekReady = ready;
        _sparePeekEngine = _environment;
        try
        {
            await ready;
        }
        catch
        {
            if (_sparePeekView == view) DropSparePeekView();
        }
    }

    private void DropSparePeekView()
    {
        if (_sparePeekView is not { } view) return;
        _sparePeekView = null;
        _sparePeekReady = null;
        PeekWebHost.Children.Remove(view);
        try { view.Close(); } catch { }
    }

    // On opening from a link, how long before the card grows out of it.
    private const int PeekLinkLeadMs = 90;

    /// <summary>The card, a moment later, unless Peek has gone or been shown meanwhile.</summary>
    private void ShowPeekLayerSoon(BrowserTab owner)
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.IsRepeating = false;
        timer.Interval = TimeSpan.FromMilliseconds(PeekLinkLeadMs);
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_peekOwner == owner && _active == owner && _closingPeekView is null
                && PeekLayer.Visibility != Visibility.Visible)
                ShowPeekLayer(animate: true);
        };
        timer.Start();
    }

    /// <summary>The owner's last link press, and the link's box, if it was recent enough to be what opened this Peek.</summary>
    private static (Point Point, Area? Box)? TakeLinkPress(BrowserTab owner)
    {
        var press = owner.LinkPress;
        var box = owner.LinkBox;
        owner.LinkPress = null;
        owner.LinkBox = null;
        // Press to navigation is a click's length; anything older belongs to some other link.
        return press is { } point && Environment.TickCount64 - owner.LinkPressTime < 1500 ? (point, box) : null;
    }

    /// <summary>
    /// Sets where the card grows from and shrinks back to. From a link, it starts as the link itself:
    /// covering the link's box (Core/PeekOrigin.cs), each edge then springs out to its place, so it's
    /// plain which link opened; closing, it springs back into that box. With only the press point, it
    /// scales about it from a quarter of its size, as Arc's does, and back to a speck there. With no link
    /// (a window a script opened), it grows about its middle and sinks back a little as it fades.
    /// </summary>
    private void AimPeekCard(BrowserTab owner, Point? press, Area? box)
    {
        PlacePeekCard();
        // The card frame and the page share the page column; the card is inset by its margin.
        // The page isn't transformed yet, since Peek is only now opening.
        var page = PageArea.TransformToVisual(Root).TransformPoint(new Point(0, 0));
        var cardLeft = page.X - PageArea.Margin.Left + PeekCardFrame.Margin.Left;
        var cardTop = page.Y - PageArea.Margin.Top + PeekCardFrame.Margin.Top;
        // Collapsed until now, so the card's size comes from the page it sits over.
        var margin = PeekCardFrame.Margin;
        var width = PageArea.ActualWidth + PageArea.Margin.Left + PageArea.Margin.Right - margin.Left - margin.Right;
        var height = PageArea.ActualHeight + PageArea.Margin.Top + PageArea.Margin.Bottom - margin.Top - margin.Bottom;

        // From either page of a split: the link's point and box are in its own page.
        if (press is { } point && _shownPages.Contains(owner))
        {
            var toRoot = owner.View.TransformToVisual(Root);
            if (box is { } link && width > 0 && height > 0)
            {
                var at = toRoot.TransformPoint(new Point(link.X, link.Y));
                var start = PeekOrigin.From(new Area(at.X, at.Y, link.Width, link.Height), new Area(cardLeft, cardTop, width, height));
                PeekCardFrame.CenterPoint = Vector3.Zero;
                var scale = new Vector3((float)start.ScaleX, (float)start.ScaleY, 1);
                var shift = new Vector3((float)start.OffsetX, (float)start.OffsetY, 0);
                (_cardFromScale, _cardFromShift, _cardToScale, _cardToShift, _cardToBounce) = (scale, shift, scale, shift, PeekShrinkToLinkBounce);
                return;
            }
            var inRoot = toRoot.TransformPoint(point);
            PeekCardFrame.CenterPoint = new Vector3((float)(inRoot.X - cardLeft), (float)(inRoot.Y - cardTop), 0);
            (_cardFromScale, _cardFromShift, _cardToScale, _cardToShift, _cardToBounce) =
                (new(PeekGrowFrom, PeekGrowFrom, 1), Vector3.Zero, new(PeekShrinkTo, PeekShrinkTo, 1), Vector3.Zero, PeekShrinkToLinkBounce);
            return;
        }
        PeekCardFrame.CenterPoint = new Vector3((float)width / 2, (float)height / 2, 0);
        (_cardFromScale, _cardFromShift, _cardToScale, _cardToShift, _cardToBounce) =
            (new(PeekGrowFrom, PeekGrowFrom, 1), Vector3.Zero, new(0.98f, 0.98f, 1), new(0, 8, 0), PeekShrinkBounce);
    }

    /// <summary>The card's place over the page (PeekLayout): about three quarters of its width, centred.</summary>
    private void PlacePeekCard()
    {
        // Opening as a tab or split, the card's margin is the move's.
        if (_peekExpanding) return;
        var page = PageArea.Margin;
        var side = PeekLayout.CardSide(PageArea.ActualWidth);
        PeekCardFrame.Margin = new Thickness(page.Left + side, page.Top + PeekCardTop, page.Right + side, page.Bottom + PeekCardBottom);
    }

    // Peek's opening and place, measured from Arc's at 60fps; its close is Drift's own. The storyboards in
    // MainWindow.xaml are timed to match.
    private const double PeekCardTop = 3;         // the card a little in from the page's top and bottom
    private const double PeekCardBottom = 4;
    private const float PeekGrowFrom = 0.28f;     // with only the press: its scale on the first frame, about the press
    private const double PeekGrowSeconds = 0.32;
    private const double PeekGrowBounce = 0.36;   // overshoots about 5% and settles, by 0.45s
    private const float PeekShrinkTo = 0.02f;     // with only the press: closing, back to a speck there
    private const double PeekShrinkSeconds = 0.22;
    // No dip past the end on the way back into the link: past it, the card would turn inside out.
    private const double PeekShrinkToLinkBounce = 0;
    private const double PeekShrinkBounce = 0.25; // with no link: dips just past its end before it fades

    /// <summary>The card, its buttons on it, springs up out of the link (or the press) and settles.</summary>
    private void SpringPeekIn()
    {
        // Starting points go in as `from`, never as the elements' own Scale: XAML draws them at that,
        // and a card drawn at half size then stretched up has soft borders the whole way.
        // Scale and shift on the same spring, so each edge travels straight from the link's to its place.
        Spring.To(PeekCardFrame, "Scale", Vector3.One, duration: PeekGrowSeconds, bounce: PeekGrowBounce, from: _cardFromScale);
        Spring.To(PeekCardFrame, "Translation", Vector3.Zero, duration: PeekGrowSeconds, bounce: PeekGrowBounce, from: _cardFromShift);
    }

    /// <summary>Back into the link it came from on a spring (PeekExitAnimation fades it as it gets there).</summary>
    private void SpringPeekOut()
    {
        Spring.To(PeekCardFrame, "Scale", _cardToScale, duration: PeekShrinkSeconds, bounce: _cardToBounce);
        Spring.To(PeekCardFrame, "Translation", _cardToShift, duration: PeekShrinkSeconds, bounce: _cardToBounce);
    }

    /// <summary>The card at rest, with no spring running.</summary>
    private void SettlePeekMotion()
    {
        Spring.Jump(PeekCardFrame, "Scale", Vector3.One);
        Spring.Jump(PeekCardFrame, "Translation", Vector3.Zero);
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
        var flung = _peekFlungAt;
        _peekFlungAt = null;
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
                // Pulled down by its grabber: on down, rather than back to the link.
                if (flung is { } speed) SpringPeekAway(speed);
                else SpringPeekOut();
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
    /// The page's Peek scale and shift (PeekLayout.PageShrink): 8px in on every side, each edge on a whole
    /// device pixel so its 1px border stays sharp. The page's laid-out size can be fractional (at rest XAML
    /// snaps it, under a transform it doesn't), so it's scaled from its corner and shifted by whole pixels.
    /// </summary>
    private void SnapPageShrink()
    {
        var pixels = PageArea.XamlRoot?.RasterizationScale ?? 1;
        (PageShrinkX.Value, PageShiftX.Value) = PeekLayout.PageShrink(PageArea.ActualWidth, pixels);
        (PageShrinkY.Value, PageShiftY.Value) = PeekLayout.PageShrink(PageArea.ActualHeight, pixels);

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
        PeekToSplitButton.Visibility = CanSplitWithPeek(_peekOwner) ? Visibility.Visible : Visibility.Collapsed;
        // The frame draws the border while the page is shrunk; the page's own would be soft.
        // This local value hides the style's border until HidePeekLayer clears it.
        foreach (var pane in _panes) pane.Face.BorderBrush = null;
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
        UpdateDragRegions();
    }

    /// <summary>Instantly back to the closed state: page full size, layer hidden.</summary>
    private void HidePeekLayer()
    {
        PeekLayer.Visibility = Visibility.Collapsed;
        if (!_peekExpanding) UnstagePicture();
        PeekPageFrame.Visibility = Visibility.Collapsed;
        // Back to the style's border, which follows light/dark mode as it changes.
        foreach (var pane in _panes) pane.Face.ClearValue(Border.BorderBrushProperty);
        // Stopping returns every animated value (page scale, opacity) to its base.
        SettlePeekMotion();
        PeekEnterAnimation.Stop();
        PeekExitAnimation.Stop();
        PeekFadeInAnimation.Stop();
        PeekFadeOutAnimation.Stop();
        _peekPageBack?.Stop();
        _peekPageBack = null;
        _peekControlsOut?.Stop();
        _peekControlsOut = null;
        UpdateDragRegions();
    }

    /// <summary>
    /// Open as tab (PeekExpand). A picture of the page stands in for it, held still, while the page
    /// moves into its new tab and lays itself out at full width out of sight; the card's edges spring
    /// out to the page's, on the page's own background colour; then the picture fades into the page.
    /// Nothing reloads; scroll, video, a half-filled form all carry on.
    /// Open as split: the same, but the page goes beside the one it came from, in the right half. The
    /// card's edges spring to that half, the picture carried along by its left edge, while the page
    /// behind comes back from under Peek into the left half: one move, both at once.
    /// </summary>
    private async void PromotePeekToTab(bool split = false)
    {
        if (_peekView is not { CoreWebView2: { } core } view || _peekExpanding) return;
        var owner = _peekOwner;
        if (split && !CanSplitWithPeek(owner)) return;
        _peekExpanding = true;
        PeekTrace("promote " + (split ? "split" : "tab"));
        var favicon = owner?.PeekFavicon;
        // No longer Peek's: its handlers stand down, and ClosePeek and the rest leave it be.
        _peekView = null;
        _peekPending = null;
        if (owner is not null) owner.HasPeek = false;
        _peekOwner = null;

        var floating = PeekCardFrame.Margin;
        var cardVisual = ElementCompositionPreview.GetElementVisual(PeekCard.Face);
        var revealed = view.Opacity > 0;
        var picture = revealed
            ? PeekFreeze.IsFresh(_freezeTakenAt, Environment.TickCount64) && _freezePicture is { } early ? early : PrepareFreezeAsync(SnapshotAsync(core))
            : Task.FromResult<FrozenPage?>(null);
        _freezePicture = null;
        _freezeTakenAt = null;
        BrowserTab? tab = null;
        try
        {
            SettlePeekMotion();
            // The buttons go at once, as the card starts to move.
            PeekControls.IsHitTestVisible = false;
            FadePeekControls();
            // 1. The picture over the page, exactly where it is: nothing on screen changes.
            var held = revealed && await Deadline.Within(FreezeAsync(picture), TimeSpan.FromMilliseconds(400), fallback: false);
            PeekTrace("frozen " + held);

            // 2. The page into its tab, out of sight: see-through, so it draws but doesn't show, and the
            //    tab in front stays in front until the end. It lays itself out at its new width meanwhile.
            view.OpacityTransition = null;
            view.Opacity = 0;
            tab = AdoptAsTab(view, core, favicon, activate: false);
            PeekTrace("adopt only");
            Edges to;
            if (split)
            {
                // The page it came from stays the one you're on until the end, the new page to its right.
                PutBeside(owner!, tab, animate: false);
                PeekTrace("split tabs");
                PageColumns.UpdateLayout();
                PeekTrace("split laid out");
                to = PaneEdges(tab);
            }
            else
            {
                WebHost.UpdateLayout();
                var page = PageArea.Margin;
                to = new Edges(page.Left, page.Top, page.Right, page.Bottom);
            }
            PeekTrace("adopted");
            var laidOut = Deadline.Within(LaidOutAsync(core, PeekExpand.CssWidth(view.ActualWidth, _peekZoom)),
                TimeSpan.FromMilliseconds(700), fallback: null);
            _ = laidOut.ContinueWith(_ => PeekTrace("laid out"), TaskScheduler.FromCurrentSynchronizationContext());
            // 3. Opening as a tab, the card's edges spring out to the page's, the picture held still inside.
            //    Into a split, the card itself moves to the right half, the picture pinned to its top-left;
            //    the page fades in as soon as it's laid out and the card's all but there.
            var from = new Edges(floating.Left, floating.Top, floating.Right, floating.Bottom);
            if (split && !_uiSettings.AnimationsEnabled) ReturnPageFromPeek();
            var grown = !_uiSettings.AnimationsEnabled ? Task.CompletedTask
                : split ? MoveCardToSplit(from, to, PageReadyAsync(owner!), ReturnPageFromPeek)
                : GrowCard(cardVisual, from, to, held);
            PeekTrace("moving");
            await Task.WhenAll(grown, laidOut);
            PeekTrace("into the page");

            // 4. The tab in front, drawn, under the card; then the card (and the picture) fades into it.
            PeekControls.Visibility = Visibility.Collapsed;
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
            StopCardMove();
            HidePeekLayer();
            PeekLayer.OpacityTransition = null;
            PeekLayer.Opacity = 1;
            cardVisual.Clip = null;
            cardVisual.StopAnimation("Scale");
            cardVisual.Scale = Vector3.One;
            PlacePeekCard();
            UnstagePicture();
            PeekCardTint.Background = null;
            PeekControls.IsHitTestVisible = true;
            PeekControls.Visibility = Visibility.Visible;
            PeekCard.Caster.Visibility = Visibility.Visible;
            PeekCard.Caster.Opacity = 1;
            if (!revealed) view.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(150) };
        }
    }

    private bool _peekExpanding;

    // DRIFT_TRACE=1: when each step of opening a Peek as a tab or split happens, in peek-trace.log, to line
    // up with a screen recording.
    private static readonly bool Tracing = Environment.GetEnvironmentVariable("DRIFT_TRACE") == "1";

    private static void PeekTrace(string step)
    {
        if (!Tracing) return;
        try
        {
            var folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Drift");
            System.IO.File.AppendAllText(System.IO.Path.Combine(folder, "peek-trace.log"), $"{DateTime.Now:HH:mm:ss.fff} {step}{Environment.NewLine}");
        }
        catch
        {
            // Nowhere to write it.
        }
    }

    /// <summary>Its page can go beside the one it came from: that one isn't in a split, or its split has room.</summary>
    private static bool CanSplitWithPeek(BrowserTab? owner) => owner is { IsSplitRow: false } && owner.Split is not { IsFull: true };

    /// <summary>A split page's card, as margins in the page column (where Peek's card is placed).</summary>
    private Edges PaneEdges(BrowserTab page)
    {
        var card = _panes[Math.Max(0, page.Split?.IndexOf(page) ?? 0)].Card;
        var area = PageArea.Margin;
        var at = card.ActualOffset;
        return new Edges(
            area.Left + at.X,
            area.Top + at.Y,
            area.Right + Math.Max(0, PageColumns.ActualWidth - at.X - card.ActualWidth),
            area.Bottom + Math.Max(0, PageColumns.ActualHeight - at.Y - card.ActualHeight));
    }

    // The page behind coming back from under Peek (ReturnPageFromPeek), and the buttons leaving (FadePeekControls).
    private Storyboard? _peekPageBack;
    private Storyboard? _peekControlsOut;

    /// <summary>
    /// The page behind, from where Peek left it (shrunk a little, dimmed) back to itself, into a split:
    /// on the card's own spring (PeekExpand.Split*), from the same moment, so the two move as one. Its
    /// dimming lifts on the strong ease-out over the same time.
    /// </summary>
    private void ReturnPageFromPeek()
    {
        // Where the opening left it: scaled from its corner and shifted, which is a scale about this point.
        double scaleX = PageAreaTransform.ScaleX, scaleY = PageAreaTransform.ScaleY;
        double shiftX = PageAreaTransform.TranslateX, shiftY = PageAreaTransform.TranslateY, opacity = PageArea.Opacity;
        static float About(double shift, double scale) => scale < 1 ? (float)(shift / (1 - scale)) : 0;
        // The opening's storyboard lets go of it, and in the same frame the spring takes it from there.
        PeekEnterAnimation.Stop();
        PeekFadeInAnimation.Stop();
        PeekPageFrame.Visibility = Visibility.Collapsed;
        foreach (var pane in _panes) pane.Face.ClearValue(Border.BorderBrushProperty);
        if (!_uiSettings.AnimationsEnabled) return;

        // On what's inside it, which fills it exactly: PageArea has a RenderTransform (the opening's), and
        // XAML won't take Scale on an element that has one (it crashed, E_INVALIDARG).
        PageColumns.CenterPoint = new Vector3(About(shiftX, scaleX), About(shiftY, scaleY), 0);
        Spring.To(PageColumns, "Scale", Vector3.One, PeekExpand.SplitSeconds, PeekExpand.SplitBounce,
            from: new Vector3((float)scaleX, (float)scaleY, 1), kick: PeekExpand.SplitKick);
        var board = new Storyboard();
        Animate(board, PageArea, "Opacity", opacity, 1, PeekExpand.SplitSeconds, 0.23, 1, 0.32, 1);
        _peekPageBack = board;
        board.Begin();
    }

    // Undoes MoveCardToSplit's setup: while it's set, the card's on its way into a split.
    private Action? _cardMoveCleanup;

    /// <summary>
    /// Into a split: the card moves from where it floats to the right half, its position and size on one
    /// spring, as an object: its outline goes with it, and the picture keeps its size, pinned to its
    /// top-left, so the page reads as the same page in a narrower window. The shadow fades at once as it
    /// comes down into the page's plane. Run by the compositor: the UI thread is busy laying out the split
    /// and the pages meanwhile, and a move stepped there came out as one or two long jumps. Done once it's
    /// all but there; the rest of the spring carries on under the fade into the page.
    /// </summary>
    private async Task MoveCardToSplit(Edges from, Edges to, Task ready, Action together)
    {
        StopCardMove();
        var union = PeekExpand.Union(from, to);
        var start = PeekExpand.Inset(from, union);
        var end = PeekExpand.Inset(to, union);
        var pixels = PageArea.XamlRoot?.RasterizationScale ?? 1;
        var travel = new[] { end.Left - start.Left, end.Top - start.Top, end.Right - start.Right, end.Bottom - start.Bottom }.Max(Math.Abs);

        // The card takes the room of where it is and where it's going, cut back to where it is.
        var edge = PeekCard.Face.BorderBrush;
        PeekCard.Face.BorderBrush = null;
        PeekCard.Caster.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(100) };
        PeekCard.Caster.Opacity = 0;
        PeekStill.Margin = new Thickness(start.Left, start.Top, start.Right, start.Bottom);
        PeekCardFrame.Margin = new Thickness(union.Left, union.Top, union.Right, union.Bottom);
        PeekCardFrame.UpdateLayout();
        float width = (float)PeekCard.Face.ActualWidth, height = (float)PeekCard.Face.ActualHeight;

        var cardVisual = ElementCompositionPreview.GetElementVisual(PeekCard.Face);
        var stillVisual = ElementCompositionPreview.GetElementVisual(PeekStill);
        var compositor = cardVisual.Compositor;
        var progress = compositor.CreatePropertySet();
        progress.InsertScalar("T", 0);
        ExpressionAnimation Bind(string expression)
        {
            var animation = compositor.CreateExpressionAnimation(expression);
            animation.SetReferenceParameter("p", progress);
            animation.SetScalarParameter("l0", (float)start.Left);
            animation.SetScalarParameter("t0", (float)start.Top);
            animation.SetScalarParameter("r0", (float)start.Right);
            animation.SetScalarParameter("b0", (float)start.Bottom);
            animation.SetScalarParameter("l1", (float)end.Left);
            animation.SetScalarParameter("t1", (float)end.Top);
            animation.SetScalarParameter("r1", (float)end.Right);
            animation.SetScalarParameter("b1", (float)end.Bottom);
            animation.SetScalarParameter("w", width);
            animation.SetScalarParameter("h", height);
            return animation;
        }
        // Each edge from where it was to where it's going (a little past on the overshoot, as far as the card goes).
        const string left = "Clamp(l0 + (l1 - l0) * p.T, 0, w)";
        const string top = "Clamp(t0 + (t1 - t0) * p.T, 0, h)";
        const string right = "Clamp(w - r0 - (r1 - r0) * p.T, 0, w)";
        const string bottom = "Clamp(h - b0 - (b1 - b0) * p.T, 0, h)";
        const string round = "6";

        var shape = compositor.CreateRoundedRectangleGeometry();
        shape.StartAnimation("Offset", Bind($"Vector2({left}, {top})"));
        shape.StartAnimation("Size", Bind($"Vector2({right} - {left}, {bottom} - {top})"));
        shape.StartAnimation("CornerRadius", Bind($"Vector2({round}, {round})"));
        cardVisual.Clip = compositor.CreateGeometricClip(shape);

        // The picture rides with the card's top-left corner.
        ElementCompositionPreview.SetIsTranslationEnabled(PeekStill, true);
        stillVisual.StartAnimation("Translation", Bind($"Vector3({left} - l0, {top} - t0, 0)"));

        // The card's edge, drawn on the moving card (its own would only be where the room's edges are).
        var outline = compositor.CreateRoundedRectangleGeometry();
        outline.StartAnimation("Offset", Bind($"Vector2({left} + 0.5, {top} + 0.5)"));
        outline.StartAnimation("Size", Bind($"Vector2({right} - {left} - 1, {bottom} - {top} - 1)"));
        outline.StartAnimation("CornerRadius", Bind($"Vector2({round} - 0.5, {round} - 0.5)"));
        var stroke = compositor.CreateSpriteShape(outline);
        stroke.StrokeThickness = 1;
        stroke.StrokeBrush = compositor.CreateColorBrush((edge as SolidColorBrush)?.Color ?? Microsoft.UI.Colors.Transparent);
        var outlineVisual = compositor.CreateShapeVisual();
        outlineVisual.Size = new Vector2(width, height);
        outlineVisual.Shapes.Add(stroke);
        ElementCompositionPreview.SetElementChildVisual(PeekCard.Face, outlineVisual);

        _cardMoveCleanup = () =>
        {
            progress.StopAnimation("T");
            cardVisual.Clip = null;
            ElementCompositionPreview.SetElementChildVisual(PeekCard.Face, null);
            stillVisual.StopAnimation("Translation");
            stillVisual.Properties.InsertVector3("Translation", Vector3.Zero);
            PeekStill.Margin = new Thickness(0);
            // Back to its style's border, which follows light and dark.
            PeekCard.Face.ClearValue(Border.BorderBrushProperty);
            PeekCard.Caster.OpacityTransition = null;
            PeekCard.Caster.Opacity = 1;
        };

        // Nothing moves until the page behind is drawn at its new width and the card's set up on screen
        // as it was: started any sooner, the first part of the move went by unseen and the card jumped.
        // Then the card and the page behind leave on the same frame.
        await ready;
        await NextFrameAsync();
        if (_cardMoveCleanup is null)
        {
            together();
            return;
        }
        PeekTrace("move starts");
        together();
        var spring = compositor.CreateSpringScalarAnimation();
        spring.InitialValue = 0;
        spring.FinalValue = 1;
        spring.InitialVelocity = (float)(PeekExpand.SplitKick * 2 * Math.PI / PeekExpand.SplitSeconds);
        spring.DampingRatio = (float)(1 - PeekExpand.SplitBounce);
        // The compositor's Period is 1/ω (see Spring).
        spring.Period = TimeSpan.FromSeconds(PeekExpand.SplitSeconds / (2 * Math.PI));
        progress.StartAnimation("T", spring);
        await Task.Delay(TimeSpan.FromSeconds(PeekExpand.ArriveSeconds(travel * pixels, 2)));
    }

    /// <summary>Once a tab's page has drawn itself at its view's width (or a moment's passed).</summary>
    private static Task PageReadyAsync(BrowserTab tab) => tab.View.CoreWebView2 is { } core
        ? Deadline.Within(LaidOutAsync(core, PeekExpand.CssWidth(tab.View.ActualWidth, tab.Zoom)),
            TimeSpan.FromMilliseconds(150), fallback: null)
        : Task.CompletedTask;

    private void StopCardMove()
    {
        var cleanup = _cardMoveCleanup;
        _cardMoveCleanup = null;
        cleanup?.Invoke();
    }

    /// <summary>The buttons fade out, quickly (opening as a tab or split).</summary>
    private void FadePeekControls()
    {
        var opacity = PeekControls.Opacity;
        var board = new Storyboard();
        Animate(board, PeekControls, "Opacity", opacity, 0, 0.1, 0.23, 1, 0.32, 1);
        _peekControlsOut = board;
        board.Begin();
    }

    /// <summary>One property from a value to another, on a cubic-bezier curve.</summary>
    private static void Animate(Storyboard board, DependencyObject target, string property, double from, double to,
        double seconds, double x1, double y1, double x2, double y2)
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = from });
        animation.KeyFrames.Add(new SplineDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds)),
            Value = to,
            KeySpline = new KeySpline { ControlPoint1 = new Point(x1, y1), ControlPoint2 = new Point(x2, y2) },
        });
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        board.Children.Add(animation);
    }

    // The picture taken and decoded while the pointer's on "Open as tab" or "Open as split" (PeekFreeze),
    // and when: a click shows it at once.
    private Task<FrozenPage?>? _freezePicture;
    private long? _freezeTakenAt;

    private void PeekToTab_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (_peekExpanding || _peekView is not { CoreWebView2: { } core } view || view.Opacity <= 0) return;
        if (PeekFreeze.IsFresh(_freezeTakenAt, Environment.TickCount64)) return;
        PeekTrace("hover: taking picture");
        _freezePicture = PrepareFreezeAsync(SnapshotAsync(core));
        var picture = _freezePicture;
        _ = picture.ContinueWith(done =>
        {
            PeekTrace("hover: picture ready");
            // Staged while the pointer's still on its way to clicking, so the click shows it at once.
            if (_freezePicture == picture && !_peekExpanding && _peekView == view && done.Result is { } frozen) StagePicture(frozen);
        }, TaskScheduler.FromCurrentSynchronizationContext());
        _freezeTakenAt = Environment.TickCount64;
    }

    /// <summary>
    /// The card takes the size of where it is and where it's going together, and is cut back to where it
    /// was; the cut springs to where it's going, and its corners to the page's. The picture (or, without
    /// one, the page's colour) keeps its place inside.
    /// </summary>
    private Task GrowCard(Visual cardVisual, Edges from, Edges to, bool held)
    {
        var done = new TaskCompletionSource();
        // It'd be the page's size at once, around a card that's still growing.
        PeekCard.Caster.Visibility = Visibility.Collapsed;
        var union = PeekExpand.Union(from, to);
        var start = PeekExpand.Inset(from, union);
        var end = PeekExpand.Inset(to, union);
        PeekCardFrame.Margin = new Thickness(union.Left, union.Top, union.Right, union.Bottom);
        if (held) PeekStill.Margin = new Thickness(start.Left, start.Top, start.Right, start.Bottom);
        PeekCardFrame.UpdateLayout();
        float width = (float)PeekCard.Face.ActualWidth, height = (float)PeekCard.Face.ActualHeight;

        var compositor = cardVisual.Compositor;
        var shape = compositor.CreateRoundedRectangleGeometry();
        shape.CornerRadius = new Vector2(6);
        shape.Offset = new Vector2((float)start.Left, (float)start.Top);
        shape.Size = new Vector2(width - (float)(start.Left + start.Right), height - (float)(start.Top + start.Bottom));
        cardVisual.Clip = compositor.CreateGeometricClip(shape);

        var progress = compositor.CreatePropertySet();
        progress.InsertScalar("T", 0);
        ExpressionAnimation Bind(string expression)
        {
            var animation = compositor.CreateExpressionAnimation(expression);
            animation.SetReferenceParameter("p", progress);
            animation.SetScalarParameter("l0", (float)start.Left);
            animation.SetScalarParameter("t0", (float)start.Top);
            animation.SetScalarParameter("r0", (float)start.Right);
            animation.SetScalarParameter("b0", (float)start.Bottom);
            animation.SetScalarParameter("l1", (float)end.Left);
            animation.SetScalarParameter("t1", (float)end.Top);
            animation.SetScalarParameter("r1", (float)end.Right);
            animation.SetScalarParameter("b1", (float)end.Bottom);
            animation.SetScalarParameter("w", width);
            animation.SetScalarParameter("h", height);
            return animation;
        }
        // Each edge from where it was to where it's going (past it on the overshoot, as far as the card goes).
        const string left = "Clamp(l0 + (l1 - l0) * p.T, 0, w)";
        const string top = "Clamp(t0 + (t1 - t0) * p.T, 0, h)";
        const string right = "Clamp(w - r0 - (r1 - r0) * p.T, 0, w)";
        const string bottom = "Clamp(h - b0 - (b1 - b0) * p.T, 0, h)";
        shape.StartAnimation("Offset", Bind($"Vector2({left}, {top})"));
        shape.StartAnimation("Size", Bind($"Vector2({right} - {left}, {bottom} - {top})"));

        // Growing to fill the card (opening as a tab), past its edges (T over 1) the clip has nothing more
        // to show, so the card swells a touch instead, each edge out by its own inset × the overshoot
        // (PeekExpand.Bulge), and back.
        if (end == new Edges(0, 0, 0, 0))
        {
            var bulge = PeekExpand.Bulge(start, width, height);
            cardVisual.CenterPoint = new Vector3((float)bulge.CenterX, (float)bulge.CenterY, 0);
            var swell = Bind("Vector3(1 + Max(p.T - 1, 0) * x, 1 + Max(p.T - 1, 0) * y, 1)");
            swell.SetScalarParameter("x", (float)bulge.PerX);
            swell.SetScalarParameter("y", (float)bulge.PerY);
            cardVisual.StartAnimation("Scale", swell);
        }

        // The same spring as Peek opening: it leaves at once, overshoots a little and settles.
        var (seconds, bounce, kick) = (PeekExpand.Seconds, PeekExpand.Bounce, PeekExpand.Kick);
        var grow = compositor.CreateSpringScalarAnimation();
        grow.InitialValue = 0;
        grow.FinalValue = 1;
        grow.InitialVelocity = (float)(kick * 2 * Math.PI / seconds);
        grow.DampingRatio = (float)(1 - bounce);
        // The compositor's Period is 1/ω (see Spring).
        grow.Period = TimeSpan.FromSeconds(seconds / (2 * Math.PI));
        progress.StartAnimation("T", grow);

        // Done once less than half a pixel of motion is left (a spring's tail crawls on for a while):
        // then exactly where it's going.
        var pixels = PageArea.XamlRoot?.RasterizationScale ?? 1;
        var travel = new[] { end.Left - start.Left, end.Top - start.Top, end.Right - start.Right, end.Bottom - start.Bottom }
            .Max(Math.Abs) * pixels;
        _ = Task.Delay(TimeSpan.FromSeconds(PeekExpand.SettleSeconds(travel, seconds, bounce, kick))).ContinueWith(_ =>
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

    /// <summary>A picture of the page, decoded, ready to draw; and its background colour.</summary>
    private sealed record FrozenPage(SoftwareBitmapSource Source, Windows.UI.Color? Background);

    /// <summary>
    /// The picture over the Peek's page and on screen before this returns true, with the card behind it in
    /// the page's background colour. False if there's no picture.
    /// </summary>
    private async Task<bool> FreezeAsync(Task<FrozenPage?> picture)
    {
        PeekTrace("freeze: waiting for picture");
        if (await picture is not { } frozen) return false;
        PeekTrace("freeze: picture");
        if (frozen.Background is { } color) PeekCardTint.Background = new SolidColorBrush(color);
        PeekStill.Margin = new Thickness(0);
        if (!StagePicture(frozen))
        {
            // Not staged on the way to the button: it gets its frames behind the page now.
            for (var frame = 0; frame < StageFrames; frame++) await NextFrameAsync();
        }
        Canvas.SetZIndex(PeekStill, 0);
        // Set isn't drawn: two frames on, it's on screen.
        await NextFrameAsync();
        await NextFrameAsync();
        return true;
    }

    // A picture's first few frames on screen draw nothing (it's still going to the GPU): about four, so it
    // gets these behind the page before it's shown in front of it, or the card shows empty meanwhile.
    private const int StageFrames = 6;

    /// <summary>
    /// The picture behind the Peek's page, where it can get drawn unseen, ready to be brought in front.
    /// True if it was already there.
    /// </summary>
    private bool StagePicture(FrozenPage frozen)
    {
        var staged = PeekStillBrush.ImageSource == frozen.Source && PeekStill.Visibility == Visibility.Visible;
        if (staged) return true;
        Canvas.SetZIndex(PeekStill, -1);
        PeekStillBrush.ImageSource = frozen.Source;
        PeekStill.Visibility = Visibility.Visible;
        return false;
    }

    /// <summary>Drops a picture staged behind the Peek's page, or shown.</summary>
    private void UnstagePicture()
    {
        PeekStill.Visibility = Visibility.Collapsed;
        PeekStillBrush.ImageSource = null;
        Canvas.SetZIndex(PeekStill, 0);
    }

    /// <summary>The picture decoded, all of it, or null if there isn't one or it won't decode.</summary>
    private static async Task<FrozenPage?> PrepareFreezeAsync(Task<byte[]?> picture)
    {
        if (await picture is not { } png) return null;
        try
        {
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            using (var writer = new Windows.Storage.Streams.DataWriter(stream))
            {
                writer.WriteBytes(png);
                await writer.StoreAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
            var background = await BackgroundOfAsync(decoder);
            // Decoded here, all of it, before it's shown: an ImageBrush given a BitmapImage decodes it only
            // once it's drawn, and the card showed empty for a moment while it did.
            var bitmap = await decoder.GetSoftwareBitmapAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
            var source = new SoftwareBitmapSource();
            await source.SetBitmapAsync(bitmap);
            return new FrozenPage(source, background);
        }
        catch
        {
            return null;
        }
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

        Toolbar.BackButton.IsEnabled = core?.CanGoBack ?? false;
        Toolbar.ForwardButton.IsEnabled = core?.CanGoForward ?? false;
        Toolbar.ReloadButton.IsEnabled = core is not null;
        ShowLoadButton();

        var address = DisplayUrl(core?.Source);
        AddressBar.Text.Text = address.Length > 0 ? address : "Search or Enter URL";
        if (address.Length > 0) AddressBar.Text.ClearValue(TextBlock.ForegroundProperty);
        else AddressBar.Text.Foreground = (Brush)Application.Current.Resources["DriftSecondaryTextBrush"];

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
        // Outlook pushed a change to a tab of it (OutlookPushScript).
        if (command == "outlook-pushed")
        {
            OutlookPushed();
            return;
        }
        // Google pushed a change to a Google Calendar tab (GooglePushScript).
        if (command == "calendar-pushed")
        {
            CalendarPushed();
            return;
        }
        // Google pushed a change to a Gmail tab (GooglePushScript).
        if (command == "mail-pushed")
        {
            MailPushed();
            return;
        }
        if (command.StartsWith("link:", StringComparison.Ordinal))
        {
            if (source is not null)
            {
                RecordLinkPress(source, command["link:".Length..]);
                // A plain press; a Ctrl or middle one says so next.
                source.AsidePressTime = 0;
            }
            return;
        }

        if (command.StartsWith("aside:", StringComparison.Ordinal))
        {
            if (source is not null)
            {
                source.AsidePressTime = Environment.TickCount64;
                source.AsidePressShift = command["aside:".Length..] == "1";
            }
            return;
        }

        if (command.StartsWith("zoom:", StringComparison.Ordinal))
        {
            OnZoomMessage(source, command["zoom:".Length..]);
            return;
        }

        if (command.StartsWith("swipe:", StringComparison.Ordinal))
        {
            OnSwipeMessage(source, command["swipe:".Length..]);
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
        // In an answer, Ctrl+Shift+C copies the answer rather than the page's link.
        if (id == "copyurl" && CopyAskAnswer()) return;
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
            case "bookmark":
                BookmarkCurrent();
                break;
            case "bookmarks":
                ToggleLibrary("bookmarks");
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
    private void ShowNewTabHint()
    {
        foreach (var page in _profilePages.Values) page.NewTabHint.Text = NewTabHintText();
    }

    private void Settings_Changed()
    {
        // A preview switched on or off in Settings: at once, not at its next turn.
        if (_mailTimer is not null) Safely(MailTick);
        if (_calendarTimer is not null) Safely(CalendarTick);
        if (_outlookTimer is not null) Safely(OutlookTick);
        ApplyShortcuts();
        Downloads.FolderChanged();
        Downloads.ArchiveOld();
        if (_downloadPeekShown) RefreshDownloadPeek();
        ShowNewTabHint();
        // Split View pages one under another or side by side (Settings › Tabs): every split's row, at once.
        PlaceSplitCards();
        // Pages loaded from now on keep the new shortcuts from the page too; pages already open
        // pass them on anyway (InputScript), only without holding them back from the page.
        foreach (var view in AllTabs.Select(t => t.View).Append(_peekView).Append(_sparePeekView))
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

    // Ctrl+1 to Ctrl+9 go to that Profile, as ^1–9 do in Arc (Profiles.cs).
    private static readonly Shortcut[] TabNumberKeys = Enumerable.Range(1, 9)
        .Select(n => new Shortcut(VirtualKey.Number0 + n, true, false, false)).ToArray();

    private static int? TabNumber(Shortcut keys) =>
        keys is { Ctrl: true, Shift: false, Alt: false } && keys.Key is >= VirtualKey.Number1 and <= VirtualKey.Number9
            ? keys.Key - VirtualKey.Number0
            : null;

    private void SelectTabNumber(int number)
    {
        if (ProfileOrder.ForNumber(number, Profiles.Count) is { } at) GoToProfile(Profiles[at]);
    }

    /// <summary>
    /// Ctrl+Tab and Ctrl+Shift+Tab with Settings › Tabs set to go down the sidebar: the next or
    /// previous tab there, round the end. Otherwise they go by use (TabSwitcher.cs).
    /// </summary>
    private void SwitchTab(int step)
    {
        var all = ShownTabs.ToList();
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
        if (ClosedEntry(tab, index) is { } entry) RememberClosed(entry);
    }

    private void RememberClosed(SessionTab entry)
    {
        entry.Closed ??= DateTime.UtcNow;
        _closedTabs.Add(entry);
        if (_closedTabs.Count > MaxClosedTabs) _closedTabs.RemoveAt(0);
        ClosedTabsChanged();
    }

    /// <summary>A closed tab as Reopen closed tab brings it back; null if it had no page.</summary>
    private static SessionTab? ClosedEntry(BrowserTab tab, int index)
    {
        var url = tab.Url;
        if (string.IsNullOrEmpty(url) || url == "about:blank") url = tab.HomeUri?.ToString();
        if (string.IsNullOrEmpty(url)) return null;
        return new SessionTab
        {
            Url = url,
            Title = tab.PageTitle,
            Name = tab.Name,
            Icon = tab.Icon,
            Pinned = tab.IsPinned,
            Place = tab.Place.ToString(),
            Home = tab.HomeUri?.ToString(),
            Index = index,
            Folder = tab.Folder?.Id,
        };
    }

    /// <summary>Ctrl+Shift+T: the last tab closed comes back, in its group and place, on the page it was on.</summary>
    private void ReopenClosedTab()
    {
        if (_closedTabs.Count > 0) ReopenClosedTab(_closedTabs[^1]);
    }

    /// <summary>A closed tab back (the last, or one from the library's Closed tabs), out of the closed list.</summary>
    private void ReopenClosedTab(SessionTab saved)
    {
        if (!_closedTabs.Remove(saved)) return;
        if (_commandBarOpen) CloseCommandBar(focusPage: false);
        ClosedTabsChanged();
        SaveSessionSoon();
        if (!Uri.TryCreate(saved.Url, UriKind.Absolute, out var uri)) return;
        Uri.TryCreate(saved.Home, UriKind.Absolute, out var home);
        var place = Enum.TryParse<TabPlace>(saved.Place, out var p) ? p : TabPlace.Regular;
        // Back into its folder, if that's still there; otherwise with the pinned tabs.
        var folder = saved.Folder is { } id ? _folderTree.AllFolders.FirstOrDefault(f => f.Id == id) : null;
        var index = saved.Index;
        if (place == TabPlace.Folder && folder is null) (place, index) = (TabPlace.Pinned, null);
        _ = CreateTabAsync(uri, place, activate: true, title: saved.Title, home: home, index: index, folder: folder,
            name: saved.Name, icon: saved.Icon);
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
                WatchForRecovery(tab);
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
            case CoreWebView2ProcessFailedKind.GpuProcessExited:
                GpuProcessFailed(e);
                return;
            default:
                return;
        }
        if (tab == _active) UpdateChrome();
    }

    private static readonly GpuFailures GpuFailures = new();

    /// <summary>
    /// The engine's GPU process exited (every page froze until it came back). Failing again and
    /// again, it's usually its driver hanging on video, so video decodes on the CPU from the next start.
    /// </summary>
    private void GpuProcessFailed(CoreWebView2ProcessFailedEventArgs e)
    {
        var failure = GpuFailures.Report(DateTime.Now);
        if (failure == GpuFailure.Repeat) return;
        App.LogCrash(new InvalidOperationException($"GPU process exited: {e.Reason}, exit code {e.ExitCode}"));
        if (failure != GpuFailure.TooMany || EngineVideoOnCpu || EngineRuntime is null) return;
        var runtime = EngineRuntime;
        Settings.Update(s => s.VideoOnCpuFor = runtime);
        ShowNotice(new FontIcon { Glyph = "", FontSize = 20 }, "Video was freezing pages",
            "It plays without the graphics card from when Drift restarts", TimeSpan.FromSeconds(12),
            ("Restart now", () => _ = RestartAsync()));
    }

    private readonly HashSet<BrowserTab> _hungTabs = [];

    /// <summary>
    /// A hung page that starts answering again comes back by itself, as if Wait had been pressed
    /// (often a page catching up after the PC wakes). WebView2 reports the hang, but not its end.
    /// </summary>
    private async void WatchForRecovery(BrowserTab tab)
    {
        if (!_hungTabs.Add(tab)) return;
        try
        {
            var core = tab.View.CoreWebView2;
            Task<bool?>? asking = null;
            while (tab.Crash == TabCrash.Unresponsive)
            {
                // One question at a time: a hung page answers them all at once when it's back.
                asking ??= AnswersAsync(core);
                var answer = await Deadline.Within(asking, TimeSpan.FromSeconds(2), fallback: false);
                if (answer is null) return; // the view is gone
                if (answer == true)
                {
                    if (tab.Crash != TabCrash.Unresponsive || tab.View.CoreWebView2 != core) return;
                    tab.Crash = TabCrash.None;
                    tab.View.Visibility = Visibility.Visible;
                    if (tab == _active) UpdateChrome();
                    return;
                }
                if (asking.IsCompleted) asking = null;
            }
        }
        finally
        {
            _hungTabs.Remove(tab);
        }

        static async Task<bool?> AnswersAsync(CoreWebView2 core)
        {
            try
            {
                await core.ExecuteScriptAsync("1");
                return true;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>The engine is gone: the next page to start brings up a new one.</summary>
    private static void EngineStopped()
    {
        _environment = null;
        Extensions.Detach();
        // Its hidden view went with the engine; the next page to start brings a new one.
        foreach (var window in OpenWindows)
        {
            window._pdfTools?.Close();
            window._pdfTools = null;
        }
    }

    private bool _crashShown;

    /// <summary>
    /// The card over a page that's hung, crashed, or lost its engine. A hung page stays in view, dimmed
    /// behind it, so it's clear which page it is and that it may yet come back; a crashed one has nothing
    /// left to show. The card settles in from a touch smaller, and fades out the way it came.
    /// </summary>
    private void ShowCrash()
    {
        var crash = _active?.Crash ?? TabCrash.None;
        if (crash == TabCrash.None)
        {
            HideCrash();
            return;
        }
        var tab = _active!;
        var hung = crash == TabCrash.Unresponsive;
        tab.View.Visibility = hung ? Visibility.Visible : Visibility.Collapsed;
        // Which page it is shows in its icon (and, hung, the page itself), so the title stays one short line.
        (CrashPanel.Title.Text, CrashPanel.Text.Text) = crash switch
        {
            TabCrash.Unresponsive => ("This page isn't responding", ""),
            TabCrash.EngineStopped => ("Pages stopped working", "Your tabs are all still here"),
            _ => ("This page crashed", ""),
        };
        CrashPanel.Text.Visibility = CrashPanel.Text.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var favicon = crash == TabCrash.EngineStopped ? null : tab.Favicon;
        CrashPanel.Favicon.Source = favicon;
        CrashPanel.Favicon.Visibility = favicon is null ? Visibility.Collapsed : Visibility.Visible;
        CrashPanel.Glyph.Glyph = crash == TabCrash.EngineStopped ? "" : "";
        CrashPanel.Glyph.Visibility = favicon is null ? Visibility.Visible : Visibility.Collapsed;
        // Wait only means something for a hung page; otherwise Reload takes the whole row.
        CrashPanel.WaitButton.Visibility = hung ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumn(CrashPanel.ReloadButton, hung ? 1 : 0);
        Grid.SetColumnSpan(CrashPanel.ReloadButton, hung ? 1 : 2);
        CrashPanel.Scrim.Visibility = hung ? Visibility.Visible : Visibility.Collapsed;

        if (_crashShown) return;
        _crashShown = true;
        CrashPanel.Visibility = Visibility.Visible;
        CrashPanel.IsHitTestVisible = true;
        CrashPanel.Scrim.Opacity = 0.8;
        CrashPanel.Card.Opacity = 1;
        Spring.To(CrashPanel.Card, "Scale", Vector3.One, 0.35, 0, from: new Vector3(0.96f, 0.96f, 1));
    }

    private void HideCrash()
    {
        if (!_crashShown) return;
        _crashShown = false;
        CrashPanel.IsHitTestVisible = false;
        CrashPanel.Scrim.Opacity = 0;
        CrashPanel.Card.Opacity = 0;
        Spring.To(CrashPanel.Card, "Scale", new Vector3(0.96f, 0.96f, 1), 0.35, 0);
        var gone = DispatcherQueue.CreateTimer();
        gone.Interval = TimeSpan.FromMilliseconds(200);
        gone.IsRepeating = false;
        gone.Tick += (_, _) =>
        {
            gone.Stop();
            if (!_crashShown) CrashPanel.Visibility = Visibility.Collapsed;
        };
        gone.Start();
    }

    // Scales about its middle, whatever its text makes its height.
    internal void CrashCard_SizeChanged(object sender, SizeChangedEventArgs e) =>
        CrashPanel.Card.CenterPoint = new Vector3((float)e.NewSize.Width / 2, (float)e.NewSize.Height / 2, 0);

    internal void CrashReload_Click(object sender, RoutedEventArgs e)
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
    internal void CrashWait_Click(object sender, RoutedEventArgs e)
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
    private double PillEnd => AddressBar.PillBadges.ActualWidth > 0 ? AddressBar.PillBadges.ActualWidth + AddressBar.PillBadges.Margin.Right + 6 : 10;

    internal void PillBadges_SizeChanged(object sender, SizeChangedEventArgs e) =>
        AddressBar.Pill.Padding = new Thickness(AddressBar.Pill.Padding.Left, 0, PillEnd, 0);

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
            AddressBar.SiteSecurityButton.Visibility = Visibility.Collapsed;
            AddressBar.Pill.Padding = new Thickness(10, 0, PillEnd, 0);
            return;
        }
        AddressBar.SiteSecurityGlyph.Glyph = security switch
        {
            SiteSecurity.Secure => LockGlyph,
            SiteSecurity.Local => InfoGlyph,
            _ => WarningGlyph,
        };
        // The padlock is quiet; a warning isn't.
        AddressBar.SiteSecurityGlyph.Opacity = security == SiteSecurity.Secure ? 0.6 : 1;
        var tip = SecurityText(security);
        ToolTipService.SetToolTip(AddressBar.SiteSecurityButton, tip ?? "Site settings");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(AddressBar.SiteSecurityButton, tip is null ? "Site settings" : tip + ", site settings");
        AddressBar.SiteSecurityButton.Visibility = Visibility.Visible;
        AddressBar.Pill.Padding = new Thickness(32, 0, PillEnd, 0);
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
    internal void SiteSecurity_Click(object sender, RoutedEventArgs e) =>
        AddressBar.SiteInfoFlyout.ShowAt(AddressBar.SiteSecurityButton, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
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

    /// <summary>Ctrl+Shift+C: "Copied link" at the foot of the page, rising in, gone after a moment.</summary>
    private void ShowCopiedToast() => CopiedToast.Show("Copied link", TimeSpan.FromSeconds(1.5), glyph: "\uE73E");

    /// <summary>The page's address to the clipboard; the button shows a tick for a moment.</summary>
    internal void CopyLink_Click(object sender, RoutedEventArgs e)
    {
        if (!CopyCurrentLink()) return;
        AddressBar.CopyLinkGlyph.Glyph = CopiedGlyph;
        ToolTipService.SetToolTip(AddressBar.CopyLinkButton, "Copied");
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1.5);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            AddressBar.CopyLinkGlyph.Glyph = LinkGlyph;
            ToolTipService.SetToolTip(AddressBar.CopyLinkButton, "Copy link");
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
            focused: AddressBar.SiteInfoButton.FocusState != FocusState.Unfocused);
        // Out of the pill altogether while hidden, so what's left (the zoom level) sits at its end.
        // They fade in as they come; they go at once, as the pill takes the room back.
        foreach (var button in new[] { AddressBar.CopyLinkButton, AddressBar.SiteInfoButton })
        {
            button.Visibility = shows ? Visibility.Visible : Visibility.Collapsed;
            button.Opacity = shows ? 1 : 0;
        }
    }

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _pillButtonsTimer;

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

    internal void SiteInfoButton_FocusChanged(object sender, RoutedEventArgs e) => ShowSiteSettingsButton();

    internal void SiteInfo_Opened(object? sender, object e)
    {
        _siteInfoOpen = true;
        SidebarMenu_Opened(sender, e);
        ShowSiteSettingsButton();
    }

    internal void SiteInfo_Closed(object? sender, object e)
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

    internal async void SiteInfo_Opening(object sender, object e)
    {
        var uri = ActiveSite;
        var core = _active?.View.CoreWebView2;
        if (uri is null || core is null) return;
        var origin = uri.GetLeftPart(UriPartial.Authority);

        AddressBar.SiteInfoHost.Text = uri.Host.StartsWith("www.") ? uri.Host[4..] : uri.Host;
        var security = SecurityOf(uri);
        AddressBar.SiteInfoSecureGlyph.Glyph = security switch
        {
            SiteSecurity.Secure => LockGlyph,
            SiteSecurity.Local => InfoGlyph,
            _ => WarningGlyph,
        };
        var secureText = SecurityText(security);
        AddressBar.SiteInfoSecureText.Text = secureText ?? "";
        AddressBar.SiteInfoSecureText.Visibility = secureText is null ? Visibility.Collapsed : Visibility.Visible;
        AddressBar.SiteDataText.Text = "Signs you out of this site and clears what it has stored";
        ShowSiteAdBlock(uri);
        AddressBar.ClearSiteDataButton.IsEnabled = true;

        AddressBar.SitePermissionList.Children.Clear();
        IReadOnlyList<CoreWebView2PermissionSetting> settings;
        try { settings = await core.Profile.GetNonDefaultPermissionSettingsAsync(); }
        catch { settings = []; }
        var here = settings
            .Where(s => string.Equals(s.PermissionOrigin.TrimEnd('/'), origin, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(s => s.PermissionKind, s => s.PermissionState);
        var kinds = MainPermissions.Select(p => p.Kind).Concat(here.Keys.Where(k => MainPermissions.All(p => p.Kind != k)));
        foreach (var kind in kinds)
            AddressBar.SitePermissionList.Children.Add(PermissionRow(core.Profile, origin, kind, here.TryGetValue(kind, out var state) ? state : CoreWebView2PermissionState.Default));
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
    internal async void ClearSiteData_Click(object sender, RoutedEventArgs e)
    {
        var uri = ActiveSite;
        var core = _active?.View.CoreWebView2;
        if (uri is null || core is null) return;
        AddressBar.ClearSiteDataButton.IsEnabled = false;
        try
        {
            var origin = uri.GetLeftPart(UriPartial.Authority);
            await core.CallDevToolsProtocolMethodAsync("Storage.clearDataForOrigin",
                System.Text.Json.JsonSerializer.Serialize(new { origin, storageTypes = "all" }));
            // Cookies set for the whole site (.example.com) too, not just this host.
            foreach (var cookie in await core.CookieManager.GetCookiesAsync(origin)) core.CookieManager.DeleteCookie(cookie);
            AddressBar.SiteDataText.Text = "Cleared.";
            core.Reload();
        }
        catch (Exception ex)
        {
            AddressBar.SiteDataText.Text = "Couldn't clear it: " + ex.Message;
            AddressBar.ClearSiteDataButton.IsEnabled = true;
        }
    }

    // ───────────────────────── Downloads ─────────────────────────


    /// <summary>
    /// A download started, moved on or finished: the Library button (its ring, or a finished file's
    /// picture), the peek and the library's list, as Arc has them. No row of its own in the sidebar.
    /// </summary>
    private void ShowDownloads()
    {
        if (_libraryOpen && _libraryPage == "downloads") UpdateDownloadsPage();
        RefreshDownloadPeek();
        ShowLibraryButtonDownloads(Downloads.Items.Where(i => i.IsRunning).ToList());
    }

    internal void DownloadList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not DownloadItem item || item.State != DownloadState.Completed) return;
        Downloads.Open(item);
    }

    internal void DownloadFolder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is DownloadItem item) Downloads.ShowInFolder(item);
    }

    internal void DownloadPause_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is DownloadItem item) Downloads.PauseOrResume(item);
    }

    internal void DownloadCancel_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is DownloadItem item) Downloads.Cancel(item);
    }

    internal void DownloadRemove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DownloadItem item) return;
        Downloads.Remove(item);
        RefreshDownloads();
    }

    internal void DownloadsFolder_Click(object sender, RoutedEventArgs e) => Downloads.ShowInFolder(null);

    internal void DownloadsClear_Click(object sender, RoutedEventArgs e)
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

    /// <param name="args">"x,y,pageWidth,left,top,width,height" in CSS pixels (Core/PeekOrigin.cs). The view's width over the page's is the zoom.</param>
    private static void RecordLinkPress(BrowserTab tab, string args)
    {
        if (PeekOrigin.Parse(args) is not { } press) return;
        PeekTrace("link press");
        var zoom = tab.View.ActualWidth / press.PageWidth;
        tab.LinkPress = new Point(press.X * zoom, press.Y * zoom);
        // The part of the link that shows, in the view's coordinates.
        tab.LinkBox = press.Link is { } link
            ? PeekOrigin.Clip(new Area(link.X * zoom, link.Y * zoom, link.Width * zoom, link.Height * zoom),
                new Area(0, 0, tab.View.ActualWidth, tab.View.ActualHeight))
            : null;
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
        else if (e.Key == VirtualKey.Escape && _picked.Count > 0)
        {
            ClearPicks();
            e.Handled = true;
        }
    }

    private void TabList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection) return;
        // Ctrl or Shift held: picked out with others, not opened (Picks.cs).
        if (PickInstead((ListViewBase)sender, e)) return;
        if (((ListViewBase)sender).SelectedItem is not BrowserTab tab) return;
        if (tab != _active) Activate(tab);
        // The tab in front already, clicked where it's kept (an Essential in a split): the split's row
        // stays the one marked, not this one as well.
        else MarkInSidebar(tab);
    }

    /// <summary>
    /// The row of the tab in front marked, and no other. A split is its own row (as in Arc): that's
    /// the one marked, in whichever list it's in.
    /// </summary>
    private void MarkInSidebar(BrowserTab tab)
    {
        _syncingSelection = true;
        var row = tab.Split?.Row;
        EssentialList.SelectedItem = tab is { Place: TabPlace.Essential, Split: null } ? tab : null;
        PinnedList.SelectedItem = row is not null ? (PinnedTabs.Contains(row) ? row : null) : tab.Place == TabPlace.Pinned ? tab : null;
        TabList.SelectedItem = row is not null ? (Tabs.Contains(row) ? row : null) : tab.Place == TabPlace.Regular ? tab : null;
        foreach (var list in GroupLists)
            list.SelectedItem = GroupRowOf(tab) is { } groupRow && RowsOfList(list).Contains(groupRow) ? groupRow : null;
        _syncingSelection = false;
    }

    internal void Back_Click(object sender, RoutedEventArgs e) => _active?.View.GoBack();
    internal void Forward_Click(object sender, RoutedEventArgs e) => _active?.View.GoForward();
    internal void Reload_Click(object sender, RoutedEventArgs e)
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
            if (tab == _active) ShowLoadButton(animate: true);
            WatchSignIn(tab, e);
        };
        // The new page has come: the loading bar goes from waiting to drawing it.
        core.ContentLoading += (_, _) =>
        {
            tab.Load.Committed();
            if (tab == _active) ShowLoadButton(animate: true);
        };
        WatchSignInRequests(tab, core);
        core.NavigationCompleted += (_, e) =>
        {
            tab.Load.Finished(e.NavigationId);
            if (tab == _active) ShowLoadButton(animate: true);
        };
    }

    // Segoe Fluent Icons: refresh, and a cross to stop.
    private const string ReloadGlyphText = "\uE72C";
    private const string StopGlyphText = "\uE711";

    /// <summary>
    /// The reload button, or while the tab in front is loading, a stop button in its place; and the loading bar above
    /// the page (<paramref name="animate"/>: as the page in front loads, not as another tab comes to the front).
    /// </summary>
    private void ShowLoadButton(bool animate = false)
    {
        // Over the page in front: in a split, that page's column.
        if (_panes.Length > 0)
        {
            var front = _shownSplit is { } split ? Math.Max(0, split.IndexOf(FrontOf(split))) : 0;
            Grid.SetColumn(LoadingBar, Grid.GetColumn(_panes[Math.Min(front, _panes.Length - 1)].Marker));
        }
        LoadingBar.Show(_active?.Load.Phase ?? LoadPhase.None, animate);
        // Its page's dots make way for it, and come back once it's gone.
        ShowPageFocus();
        var loading = _active?.Load.IsLoading == true;
        Toolbar.ReloadGlyph.Glyph = loading ? StopGlyphText : ReloadGlyphText;
        var name = loading ? "Stop loading" : "Reload";
        ToolTipService.SetToolTip(Toolbar.ReloadButton, name);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(Toolbar.ReloadButton, name);
    }
    internal void NewTab_Click(object sender, RoutedEventArgs e) => OpenCommandBar(newTab: true, animate: true);
    internal void Reopen_Click(object sender, RoutedEventArgs e) => ReopenClosedTab();
    internal void PrivateWindow_Click(object sender, RoutedEventArgs e) => OpenPrivateWindow();
    internal void Downloads_Click(object sender, RoutedEventArgs e) => OpenLibrary("downloads");
    internal void History_Click(object sender, RoutedEventArgs e) => OpenLibrary("history");

    private async void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not BrowserTab tab) return;
        // A split's row is both pages (Split.cs).
        if (tab is { IsSplitRow: true, Split: { } split })
        {
            if (await MayLeaveAsync(RegularTabsOf(tab).Where(p => !p.IsPinned))) CloseSplit(split);
        }
        else CloseTab(tab);
    }

    /// <summary>The menu's Close tab: the tab, its Peek with it.</summary>
    private async void CloseTabNow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not BrowserTab tab) return;
        if (!await MayLeaveAsync(tab.IsSplitRow ? RegularTabsOf(tab).Where(p => !p.IsPinned) : [tab])) return;
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

    /// <summary>The tab or folder the ghosts are of (or being made of), and when they're done.</summary>
    private object? _ghostsOf;
    private Task? _ghostsReady;

    /// <summary>The ghosts of a tab or folder: made once a drag, by whichever of its starts asks first.</summary>
    private Task GhostsFor(object item)
    {
        if (_ghostsOf == item && _ghostsReady is not null) return _ghostsReady;
        _ghostsOf = item;
        return _ghostsReady = item switch
        {
            TabFolder folder => RenderFolderGhostAsync(folder),
            BrowserTab tab => RenderGhostsAsync(tab),
            _ => Task.CompletedTask,
        };
    }

    /// <summary>
    /// A row's drag starts with our ghost of it, not the list's own picture: that's the row as drawn, with
    /// its piece of a folder's card and its buttons, and Windows shows it again anywhere there's nothing to
    /// drop on. Held back the moment the ghost takes to make.
    /// </summary>
    private async void Row_DragStarting(UIElement sender, DragStartingEventArgs e)
    {
        if (sender is not ListViewItem { Content: { } item } || ItemsControl.ItemsControlFromItemContainer(sender) is not ListViewBase list) return;
        var deferral = e.GetDeferral();
        try
        {
            await GhostsFor(item);
            var (ghost, anchor) = list == EssentialList ? (_tileGhost, _tileGhostAnchor) : (_rowGhost, _rowGhostAnchor);
            if (ghost is not null) e.DragUI.SetContentFromSoftwareBitmap(ghost, anchor);
        }
        catch
        {
            // The list's own picture, then.
        }
        finally
        {
            deferral.Complete();
        }
    }

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
            // A group list with no groups: somewhere to drop it, to pin or unpin it.
            FolderDropZone.Visibility = Ui.Shown(FolderRows.Count == 0);
            GroupDropZone.Visibility = Ui.Shown(CurrentPage.GroupRows.Count == 0);
            _ = GhostsFor(folder);
            return;
        }
        // A split's row in the pinned list stays in its pinned tab's place (Split.cs); its halves move by their own menus.
        if (sender == PinnedList && e.Items.FirstOrDefault() is BrowserTab { IsSplitRow: true })
        {
            e.Cancel = true;
            return;
        }
        // A split's row in a group moves within the group only, by a drag of its own (GroupRowDrag.cs).
        if (e.Items.FirstOrDefault() is BrowserTab { IsSplitRow: true, Folder: not null } groupRow)
        {
            e.Cancel = true;
            if (sender is ListView groupList) BeginGroupRowDrag(groupList, groupRow);
            return;
        }
        if (e.Items.FirstOrDefault() is not BrowserTab tab) { e.Cancel = true; return; }
        // The press this drag began with switched to it (TabPressing.cs): the tab you were on comes back, so dropped
        // on the page, this one goes beside that one.
        if (_pressSwitchedFrom is { } was && AllTabs.Contains(was)) Activate(was);
        _pressSwitchedFrom = null;
        _dragTab = tab;
        e.Data.RequestedOperation = DataPackageOperation.Move;
        // Empty groups get a place to drop into.
        EssentialDropZone.Visibility = EssentialTabs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PinnedDropZone.Visibility = PinnedTabs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        // And the page, to split with (Split.cs).
        ShowSplitDrop(tab);
        _ = GhostsFor(tab);
    }

    private void Tabs_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        _dragTab = null;
        _dragFolder = null;
        _rowGhost = _tileGhost = _pageGhost = null;
        _ghostsOf = null;
        _ghostsReady = null;
        if (_slideRows is null) _slideExcept = null;
        // Let go somewhere else, or cancelled: the rows go back.
        if (_rowPreviewShown) ClearRowPreviews(animate: true);
        HideDropIndicator();
        HideFolderHighlight();
        ResetEssentialsPreview(animate: true);
        EssentialDropZone.Visibility = PinnedDropZone.Visibility = Visibility.Collapsed;
        FolderDropZone.Visibility = GroupDropZone.Visibility = Visibility.Collapsed;
        HideSplitDrop();
        // Moving a tab takes it out and puts it back, which loses its selection, and its focus
        // (which would otherwise land on the first button in the window, with a focus rectangle).
        if (_active is not null)
        {
            Activate(_active);
            _active.View.Focus(FocusState.Programmatic);
        }
        OpenSplitBarAfterDrag();
        SaveSessionSoon();
    }

    /// <summary>Photographs the two ghosts (row and tile) for this tab, the size its rows and tiles are.</summary>
    private async Task RenderGhostsAsync(BrowserTab tab)
    {
        _rowGhost = _tileGhost = _pageGhost = null;
        GhostStage.RowIcon.Source = GhostStage.TileIcon.Source = GhostStage.PageIcon.Source = tab.Favicon;
        GhostStage.RowGlyph.Visibility = GhostStage.TileGlyph.Visibility = GhostStage.PageGlyph.Visibility = tab.PlaceholderVisibility;
        GhostStage.RowTitle.Text = GhostStage.PageTitle.Text = tab.Title;
        GhostStage.Row.Width = Math.Max(120, TabList.ActualWidth);
        GhostStage.Tile.Width = EssentialList.ItemsPanelRoot is EssentialsPanel { CellWidth: > 8 and var cell } ? cell - 8 : 88;
        try
        {
            var row = await SnapshotAsync(GhostStage.Row);
            var tile = await SnapshotAsync(GhostStage.Tile);
            var page = await SnapshotAsync(GhostStage.Page);
            if (_ghostsOf != tab) return;
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
        : target == FolderList || target == GroupList || target == FolderDropZone || target == GroupDropZone ? TabPlace.Folder
        : target == PinnedList || target == PinnedDropZone ? TabPlace.Pinned
        : target == TabList ? TabPlace.Regular
        : null;

    private void Tabs_DragOver(object sender, DragEventArgs e)
    {
        if ((_dragTab is null && _dragFolder is null) || DropPlace(sender) is not { } place) return;
        // A split's row moves among the tabs only: it can't be pinned or filed as one (Split.cs).
        if (_dragTab is { IsSplitRow: true } && place != TabPlace.Regular) return;
        // Groups only move among the groups (pinned or not).
        if (_dragFolder is not null && sender != FolderList && sender != GroupList && sender != FolderDropZone && sender != GroupDropZone) return;
        // An empty group list's zone takes only a group.
        if (_dragFolder is null && (sender == FolderDropZone || sender == GroupDropZone)) return;
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
        if (sender == FolderList || sender == GroupList)
        {
            FolderList_DragOver((ListView)sender, e);
            return;
        }
        if (sender == FolderDropZone || sender == GroupDropZone)
        {
            HideFolderHighlight();
            ShowDropZone((Grid)sender);
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
        if (sender == FolderList || sender == GroupList) HideFolderHighlight();
        if (sender == EssentialList) ResetEssentialsPreview(animate: true);
    }

    private void Tabs_Drop(object sender, DragEventArgs e)
    {
        var glide = DropGlideFrom(sender, e);
        DropTab(sender, e);
        // Dropped: once the lists have laid out, it glides from where its picture was let go (PlaySlides).
        _dropGlide = e.Handled ? glide : null;
    }

    /// <summary>A dropped tab, and where the picture under the pointer was when it was let go.</summary>
    private (BrowserTab Tab, Point GhostAt)? _dropGlide;

    /// <summary>
    /// Where the dragged tab's picture is, from the pointer and where the picture's held
    /// (RenderGhostsAsync): a row by its icon, a tile just below its top. None without a picture of
    /// our own, or into a folder.
    /// </summary>
    private (BrowserTab, Point)? DropGlideFrom(object target, DragEventArgs e)
    {
        if (_dragTab is not { } tab || target == FolderList || target == GroupList) return null;
        var at = e.GetPosition(Root);
        if (target == EssentialList)
            return _tileGhost is null ? null : (tab, new Point(at.X - GhostStage.Tile.Width / 2 - 4, at.Y - 4 - 4));
        return _rowGhost is null ? null : (tab, new Point(at.X - 19, at.Y - GhostStage.Row.ActualHeight / 2));
    }

    /// <summary>
    /// The dropped tab's row (or tile), in its new place, starts where its picture was let go and
    /// glides home, rather than the picture vanishing and the row being there.
    /// </summary>
    private void GlideDropped()
    {
        if (_dropGlide is not var (tab, ghost)) return;
        _dropGlide = null;
        var list = tab.Place switch
        {
            TabPlace.Essential => EssentialList,
            TabPlace.Pinned => PinnedList,
            TabPlace.Regular => (ListViewBase)TabList,
            _ => null,
        };
        if (list?.ContainerFromItem(tab) is not FrameworkElement row) return;
        var home = row.TransformToVisual(Root).TransformPoint(new Point(0, 0));
        var from = new Vector3((float)(ghost.X - home.X), (float)(ghost.Y - home.Y), 0);
        if (from.Length() < 1) return;
        Spring.To(row, "Translation", Vector3.Zero, duration: 0.3, bounce: 0, from: from);
    }

    private void DropTab(object sender, DragEventArgs e)
    {
        HideDropIndicator();
        // The dropped row lands where it was let go; the rest slide on from where they're shown
        // (making room for it) to their new places, which should be the same.
        _slideExcept = (object?)_dragTab ?? _dragFolder;
        NoteSlideStart();
        var landing = sender is ListViewBase over && over != EssentialList && over != FolderList && over != GroupList ? LandingIn(over, e).Gap : 0;
        ClearRowPreviews(animate: false);
        var essentialLanding = _essentialLanding;
        // The tiles go back to their places as the real order takes over, without animating.
        ResetEssentialsPreview(animate: false);
        if (sender == FolderList || sender == GroupList)
        {
            if (_dragTab is null && _dragFolder is null) return;
            e.Handled = true;
            FolderList_Drop((ListView)sender, e);
            return;
        }
        // A group onto an empty group list's zone: pinned or unpinned, last.
        if (_dragFolder is { } dropped && (sender == FolderDropZone || sender == GroupDropZone))
        {
            e.Handled = true;
            MoveFolder(dropped, null, int.MaxValue, pinned: sender == FolderDropZone);
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
        if (EssentialList.ItemsPanelRoot is not EssentialsPanel { ActualWidth: > 0 and var width } panel) return;
        var columns = panel.Columns;
        var count = EssentialTabs.Count + (arriving ? 1 : 0);

        // The slot under the pointer, from the cells the drop would give (the tiles may be mid-move).
        var at = e.GetPosition(panel);
        var land = Math.Clamp(EssentialsGrid.SlotAt(at.X, at.Y, count, columns, width), 0, count - 1);
        _essentialLanding = land;
        if (_essentialPreview == (land, columns, width)) return;
        _essentialPreview = (land, columns, width);

        var order = EssentialTabs.Where(t => t != tab).ToList();
        order.Insert(land, tab);
        for (var i = 0; i < EssentialTabs.Count; i++)
            if (EssentialList.ContainerFromIndex(i) is UIElement tile) tile.Opacity = EssentialTabs[i] == tab ? 0.4 : 1;

        if (arriving)
        {
            EssentialPlaceholderIcon.Source = tab.Favicon;
            EssentialPlaceholderGlyph.Visibility = tab.PlaceholderVisibility;
            panel.PlaceExtra = PlaceEssentialPlaceholder;
        }
        panel.ShowOrder([.. EssentialTabs.Select(t => order.IndexOf(t))], count, arriving ? land : null, duration: 0.3, bounce: 0.15);
    }

    /// <summary>The arriving tab's faded tile, over its slot in the Essentials (<paramref name="cell"/>, in their panel).</summary>
    private void PlaceEssentialPlaceholder(Rect cell)
    {
        if (EssentialList.ItemsPanelRoot is not EssentialsPanel panel || _essentialPreview is null) return;
        var origin = panel.TransformToVisual(DropIndicatorLayer).TransformPoint(new Point(cell.X + 4, cell.Y + 4));
        Canvas.SetLeft(EssentialPlaceholder, origin.X);
        Canvas.SetTop(EssentialPlaceholder, origin.Y);
        EssentialPlaceholder.Width = Math.Max(0, cell.Width - 8);
        EssentialPlaceholder.Visibility = Visibility.Visible;
    }

    /// <summary>The tiles back in their own places, and the grid back to its own size.</summary>
    private void ResetEssentialsPreview(bool animate)
    {
        if (_essentialPreview is null && _essentialsArriving == 0) return;
        _essentialPreview = null;
        _essentialLanding = null;
        if (EssentialList.ItemsPanelRoot is EssentialsPanel panel)
        {
            foreach (var tile in panel.Children) tile.Opacity = 1;
            panel.ClearOrder(animate);
        }
        EssentialPlaceholder.Visibility = Visibility.Collapsed;
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
        CurrentPage.FolderDropZoneOutline.Stroke = zone == FolderDropZone ? accent : normal;
        CurrentPage.GroupDropZoneOutline.Stroke = zone == GroupDropZone ? accent : normal;
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
    private void PeekToSplit_Click(object sender, RoutedEventArgs e) => PromotePeekToTab(split: true);

    // The close button on a tab row only shows while the row is hovered.
    internal void TabRow_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        SetCloseButtonOpacity(sender, 1);
        IntegrationAnchor_PointerEntered(sender, e);
    }

    internal void TabRow_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        SetCloseButtonOpacity(sender, 0);
        IntegrationAnchor_PointerExited(sender, e);
    }

    /// <summary>
    /// Shows or hides a row's ×/−. It sits over the end of the title, so while it shows, the title
    /// stops short of it.
    /// </summary>
    private static void SetCloseButtonOpacity(object row, double opacity)
    {
        if (row is not Grid grid || grid.Children.OfType<Button>().FirstOrDefault() is not { } close) return;
        close.Opacity = opacity;
        var shown = opacity > 0 && close.Visibility == Visibility.Visible;
        foreach (var title in grid.Children.OfType<Grid>().Where(g => Grid.GetColumn(g) == 1))
            title.Padding = shown ? new Thickness(0, 0, close.Width + 4, 0) : default;
    }

    // ───────────────────────── Window and sidebar ─────────────────────────

    internal void ToggleSidebar_Click(object sender, RoutedEventArgs e) => SetSidebarHidden(!_sidebarHidden);

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
        PlaceResizeHandle();

        UpdateDragRegions();
    }

    // How far the floating sidebar slides: from just past the window's edge, shadow included.
    private float SidebarSlide => (float)(FloatingInset + SidebarWidth + 4 + 16);

    /// <summary>
    /// The floating sidebar slides in like a drawer on a spring without bounce (a hover throws nothing), in about 250ms,
    /// pushed off rather than from rest so it's moving the moment it's asked for (like an ease-out).
    /// </summary>
    private void RevealFloatingSidebar()
    {
        Sidebar.Visibility = Visibility.Visible;
        Spring.To(Sidebar, "Translation", Vector3.Zero, duration: 0.25, bounce: 0,
            from: new Vector3(-SidebarSlide, 0, 0), kick: 0.5);
        // With Windows' animation effects off, the spring jumps: it fades in where it is instead.
        if (!_uiSettings.AnimationsEnabled)
        {
            Sidebar.Opacity = 0;
            Ui.Fade(Sidebar, 1);
        }
        _sidebarWatch.Start();
    }

    /// <summary>
    /// Back off the edge on a spring with no bounce, faster than it came; it collapses once it's out
    /// of sight. Coming back over it on the way (ReturnFloatingSidebar) retargets the same spring.
    /// </summary>
    private void DismissFloatingSidebar()
    {
        _sidebarDismissing = true;
        if (!_uiSettings.AnimationsEnabled)
        {
            Ui.Fade(Sidebar, 0, () =>
            {
                if (_sidebarDismissing) Sidebar.Visibility = Visibility.Collapsed;
                _sidebarDismissing = false;
                Sidebar.Opacity = 1;
            });
            return;
        }
        Spring.To(Sidebar, "Translation", new Vector3(-SidebarSlide, 0, 0), duration: 0.2, bounce: 0);
        // A spring has no completed event to wait for; by now it's off the edge, shadow and all.
        _sidebarDismissTimer.Start();
    }

    /// <summary>Back over the panel while it's leaving: the spring turns around from wherever it got to.</summary>
    private void ReturnFloatingSidebar()
    {
        _sidebarDismissing = false;
        _sidebarDismissTimer.Stop();
        if (!_uiSettings.AnimationsEnabled) Ui.Fade(Sidebar, 1);
        else Spring.To(Sidebar, "Translation", Vector3.Zero, duration: 0.25, bounce: 0);
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

    internal void SidebarMenu_Opened(object? sender, object e) => _openSidebarMenus++;

    // Once the menu closes, SidebarWatch_Tick decides from where the pointer is: picking an item or
    // clicking back on the sidebar keeps it, clicking away on the page sends it off.
    internal void SidebarMenu_Closed(object? sender, object e) => _openSidebarMenus = Math.Max(0, _openSidebarMenus - 1);

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

    /// <summary>
    /// Where the pointer is, in the window's coordinates, when it's over Drift's window or one of Drift's
    /// own (a menu's or list's window, which can reach past the window's edge); null over another app.
    /// PointerOverWindow counts the main window only, so on a list of its own the pointer was "off".
    /// </summary>
    private Point? PointerOverDrift()
    {
        if (Root.XamlRoot is null || !GetCursorPos(out var screen)) return null;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var under = WindowFromPoint(screen);
        if (under == IntPtr.Zero) return null;
        var root = GetAncestor(under, GetAncestorRoot);
        GetWindowThreadProcessId(root, out var owner);
        if (root != hwnd && owner != Environment.ProcessId) return null;
        var client = screen;
        if (!ScreenToClient(hwnd, ref client)) return null;
        var scale = Root.XamlRoot.RasterizationScale;
        return new Point(client.X / scale, client.Y / scale);
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int process);

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
    /// settle back on release, without a rebound: nothing was thrown, and it happens many times a day. Springs on the element's own Scale: no layout
    /// work, and a quick press-release retargets smoothly mid-motion.
    /// </summary>
    private void AddPressFeedback(UIElement element, float pressedScale)
    {
        _pressFeedback.Add(element);

        void Press(object sender, PointerRoutedEventArgs e)
        {
            if (!_uiSettings.AnimationsEnabled) return;
            // A split's row: the half pressed shrinks instead (SplitHalf_Loaded).
            if (element is ListViewItem { Content: BrowserTab { IsSplitRow: true } }) return;
            // A folder's own row only opens or closes it, and its tabs slide in or out below: that's the response.
            if (element is ListViewItem { Content: TabFolder }) return;
            var center = new Vector3(element.ActualSize / 2, 0);
            foreach (var part in PressedParts(element))
            {
                // About the row's middle, whichever part of it this is.
                part.CenterPoint = center - (part == element ? Vector3.Zero : new Vector3(part.ActualOffset.X, part.ActualOffset.Y, 0));
                // Down fast, so the press shows at once.
                Spring.To(part, "Scale", new Vector3(pressedScale, pressedScale, 1), duration: 0.15, bounce: 0);
            }
        }
        void Release(object sender, PointerRoutedEventArgs e)
        {
            foreach (var part in PressedParts(element))
                if (part.Scale != Vector3.One) Spring.To(part, "Scale", Vector3.One, duration: 0.25, bounce: 0);
        }

        // Buttons and list items mark pointer events handled, so listen to handled ones too.
        element.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Press), true);
        element.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(Release), true);
        element.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(Release), true);
        element.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(Release), true);
        element.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler(Release), true);
    }

    /// <summary>
    /// What shrinks when an element is pressed: the element, or, for a row in a folder, the row's own parts and not
    /// the pieces of the folder's card drawn behind it (IndentFolderRow), which stays still as the row shrinks.
    /// </summary>
    private static IEnumerable<UIElement> PressedParts(UIElement element)
    {
        if (element is ListViewItem item && VisualTreeHelper.GetChildrenCount(item) > 0 && VisualTreeHelper.GetChild(item, 0) is Grid root
            && root.Children.Any(c => c is Border { Tag: CardTag }))
            return root.Children.Where(c => c is not Border { Tag: CardTag }).ToList();
        return [element];
    }

    private void ListItem_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (IsFolderList(sender) && !args.InRecycleQueue) IndentFolderRow(args.ItemContainer, args.Item);
        // A split's row has no card or padding of its own (SplitRowStyle); a recycled container may have held either kind.
        if (IsTabList(sender) && !args.InRecycleQueue && args.ItemContainer is ListViewItem row)
        {
            var style = (Style)Application.Current.Resources[args.Item is BrowserTab { IsSplitRow: true } ? "SplitRowStyle" : "TabRowStyle"];
            if (row.Style != style) row.Style = style;
        }
        // Containers are recycled; wire each one once.
        if (args.InRecycleQueue || _pressFeedback.Contains(args.ItemContainer)) return;
        AddPressFeedback(args.ItemContainer, sender is GridView ? 0.96f : 0.98f);
        args.ItemContainer.DragStarting += Row_DragStarting;
        // A plain press switches at once (TabPressing.cs); the list's own selection comes on release.
        args.ItemContainer.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Row_PointerPressed), true);
    }

    internal void Menu_Opening(object? sender, object e)
    {
        Toolbar.SidebarMenuItem.Text = _sidebarHidden ? "Show sidebar" : "Hide sidebar";
        Toolbar.NewTabMenuItem.KeyboardAcceleratorTextOverride = ShortcutText("newtab");
        Toolbar.ReopenMenuItem.KeyboardAcceleratorTextOverride = ShortcutText("reopen");
        Toolbar.ReopenMenuItem.IsEnabled = _closedTabs.Count > 0;
        Toolbar.NewFolderMenuItem.KeyboardAcceleratorTextOverride = ShortcutText("folder");
        // One's enough: they'd hold the same pull requests (LiveFolders.cs).
        Toolbar.NewPullsFolderMenuItem.Visibility = Ui.Shown(!_private && !LiveFolders.Any());
        Toolbar.PrivateMenuItem.KeyboardAcceleratorTextOverride = ShortcutText("private");
        Toolbar.DownloadsMenuItem.KeyboardAcceleratorTextOverride = ShortcutText("downloads");
        Toolbar.HistoryMenuItem.KeyboardAcceleratorTextOverride = ShortcutText("history");
        Toolbar.SidebarMenuItem.KeyboardAcceleratorTextOverride = ShortcutText("sidebar");
        Toolbar.SettingsMenuItem.KeyboardAcceleratorTextOverride = ShortcutText("settings");
    }

    /// <summary>The + menu, across from the Library button: a tab, a folder, or a live folder.</summary>
    private void PlusMenu_Opening(object? sender, object e)
    {
        PlusNewTabItem.KeyboardAcceleratorTextOverride = ShortcutText("newtab");
        PlusNewFolderItem.KeyboardAcceleratorTextOverride = ShortcutText("folder");
        // Not in a private window; and one pull requests folder is enough (LiveFolders.cs).
        PlusLiveFolderItem.Visibility = Ui.Shown(!_private);
        PlusGitHubItem.IsEnabled = !LiveFolders.Any();
        // A private window has the one sidebar.
        PlusNewProfileItem.Visibility = Ui.Shown(!_private);
    }

    private void PlusMenu_Closed(object? sender, object e)
    {
        SidebarMenu_Closed(sender, e);
        // Its submenu's window can be left at the window's corner (GhostPopups).
        _ghostPopups.MenuClosed(PlusGitHubItem);
    }

    // As the system minimizes (GhostPopups), so any menu open is closed first.
    internal void Minimize_Click(object sender, RoutedEventArgs e) => _ghostPopups.Minimize();

    internal void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter) return;
        if (presenter.State == OverlappedPresenterState.Maximized) presenter.Restore();
        else presenter.Maximize();
    }

    internal void CloseWindow_Click(object sender, RoutedEventArgs e) => RequestClose("Click again to close");

    internal void Settings_Click(object sender, RoutedEventArgs e) => SettingsWindow.Open(this);

    internal void DragLayout_SizeChanged(object sender, SizeChangedEventArgs e)
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
        if (!_sidebarHidden && !Library.Panel.IsHitTestVisible) rects.Add(BoundsOf(Toolbar.TitleDragSpacer));
        // The window buttons, while they're down, are buttons; Peek's grabber, while it's open, a grabber;
        // a split page's handle, where it reaches up into the gap above the page, a handle.
        foreach (var hole in new[] { CaptionBarArea, PeekGrabberArea }.Concat(PageHandleAreas().Select(a => (Area?)a)))
            if (hole is { } cut)
                rects = rects
                    .SelectMany(r => Areas.Carve(new Area(r.X, r.Y, r.Width, r.Height), cut))
                    .Select(a => new Rect(a.X, a.Y, a.Width, a.Height))
                    .ToList();
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
