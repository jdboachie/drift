using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace Drift.Models;

/// <summary>Where a tab lives in the sidebar.</summary>
public enum TabPlace
{
    /// <summary>A favicon tile at the top, like Zen's Essentials.</summary>
    Essential,
    /// <summary>A row under the tiles, kept like an Essential but shown with its title.</summary>
    Pinned,
    /// <summary>An ordinary tab below New Tab.</summary>
    Regular,
    /// <summary>In a folder (TabFolder), between the Essentials and the pinned tabs; kept like a pinned tab.</summary>
    Folder,
}

/// <summary>What's gone wrong with a tab's page, if anything (MainWindow.OnProcessFailed).</summary>
public enum TabCrash
{
    None,
    /// <summary>The page's process died; reloading brings it back.</summary>
    Crashed,
    /// <summary>The page has stopped responding; it may still recover.</summary>
    Unresponsive,
    /// <summary>The whole web engine stopped, so the tab needs a new view.</summary>
    EngineStopped,
}

/// <summary>Two tabs side by side: Left's page on the left, Right's on the right.</summary>
public sealed class TabSplit(BrowserTab left, BrowserTab right)
{
    public BrowserTab Left { get; } = left;
    public BrowserTab Right { get; } = right;

    /// <summary>Its row in the tab list (a BrowserTab with IsSplitRow, standing for both pages).</summary>
    public BrowserTab? Row { get; set; }

    /// <summary>The left page's share of the width.</summary>
    public double Ratio { get; set; } = 0.5;

    public BrowserTab Other(BrowserTab tab) => tab == Left ? Right : Left;
}

/// <summary>One tab: its own WebView2 plus the bits the sidebar displays.</summary>
public sealed class BrowserTab : INotifyPropertyChanged, IFolderTab
{
    private string _title = "New tab";
    private BitmapImage? _favicon;
    private BitmapImage? _peekFavicon;
    private bool _hasPeek;
    private bool _isUnloaded;

    // Segoe Fluent Icons: a minus to unload, a cross to close.
    private static readonly string UnloadGlyph = ((char)0xE738).ToString();
    private static readonly string CloseGlyph = ((char)0xE711).ToString();

    public BrowserTab(WebView2 view, TabPlace place)
    {
        View = view;
        Place = place;
    }

    /// <summary>The tab's page. Unloading a tab swaps in a fresh, empty one (MainWindow.UnloadTab).</summary>
    public WebView2 View { get; set; }

    /// <summary>
    /// No page loaded: restored tabs that haven't been opened yet, and pinned tabs that were unloaded.
    /// They show dimmed, and load when they're next opened.
    /// </summary>
    public bool IsUnloaded
    {
        get => _isUnloaded;
        set
        {
            if (_isUnloaded == value) return;
            _isUnloaded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ContentOpacity));
            OnPropertyChanged(nameof(PinnedActionGlyph));
            OnPropertyChanged(nameof(PinnedActionName));
            OnPropertyChanged(nameof(HalfOpacity));
        }
    }

    public double ContentOpacity => _isUnloaded ? 0.5 : 1;

    /// <summary>A pinned row's hover button: "−" unloads it; once unloaded, "×" removes it.</summary>
    public string PinnedActionGlyph => _isUnloaded ? CloseGlyph : UnloadGlyph;

    public string PinnedActionName => _isUnloaded ? "Close tab" : "Unload tab";

    public TabPlace Place { get; set; }

    /// <summary>The folder it's in, for a tab whose Place is Folder.</summary>
    public TabFolder? Folder { get; set; }

    /// <summary>How far a row is indented per folder level.</summary>
    public const double IndentStep = 14;

    private int _depth;

    /// <summary>Folder levels it sits under in the sidebar (0 outside folders).</summary>
    public int Depth
    {
        get => _depth;
        set
        {
            if (_depth == value) return;
            _depth = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Essentials and pinned tabs: they belong to a page (HomeUri), links leaving its site open in
    /// Peek, and closing one sends it back to its page rather than closing it.
    /// </summary>
    public bool IsPinned => Place != TabPlace.Regular;

    /// <summary>For Essentials and pinned tabs: the page the tab belongs to. Links leaving its site open in Peek.</summary>
    public Uri? HomeUri { get; set; }

    /// <summary>
    /// A navigation the app started itself (startup, address bar, back to pinned page).
    /// WebView2 reports those as user-initiated, so Peek would otherwise catch them.
    /// </summary>
    public Uri? AppNavigation { get; set; }

    /// <summary>
    /// Where the tab will go once it loads: set while its view is starting, and for tabs restored
    /// from the last session, which only load when they're first shown.
    /// </summary>
    public Uri? PendingUri { get; set; }

    /// <summary>The address the tab is on, or will be on once it loads.</summary>
    public string? Url => PendingUri?.ToString() ?? Source ?? LastUrl;

    // A view whose engine has died throws when asked where it is.
    private string? Source
    {
        get
        {
            try { return View.CoreWebView2?.Source; }
            catch { return null; }
        }
    }

    /// <summary>The last address the page reported, kept for when its engine is gone (a crash).</summary>
    public string? LastUrl { get; set; }

    public TabCrash Crash { get; set; }

    /// <summary>A site whose certificate the engine rejected, which the page went on to anyway.</summary>
    public string? CertificateErrorHost { get; set; }

    /// <summary>Where the last link press in this page landed (in the view's coordinates), so Peek can grow from it.</summary>
    public Point? LinkPress { get; set; }

    /// <summary>Whether the page is loading: the reload button is a stop button meanwhile.</summary>
    public PageLoad Load { get; } = new();

    /// <summary>The page's zoom (Chromium's own, as the page reports it; 1 is 100%).</summary>
    public double Zoom { get; set; } = 1;

    private TabSplit? _split;

    /// <summary>
    /// The split view it's in, if any (MainWindow's Split.cs). A split is a row of its own in the tab
    /// list (its Row); its regular tabs are only in that row, while Essentials, pinned and folder tabs
    /// keep their own rows too.
    /// </summary>
    public TabSplit? Split
    {
        get => _split;
        set
        {
            if (_split == value) return;
            _split = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SplitVisibility));
            OnPropertyChanged(nameof(SingleVisibility));
        }
    }

    /// <summary>
    /// A split's row in the tab list, not a tab of its own: it has no page (its View is never started
    /// or shown), stands for both of Split's pages, and is never the tab you're on.
    /// </summary>
    public bool IsSplitRow { get; private set; }

    /// <summary>A new split's row (Split.cs).</summary>
    public static BrowserTab SplitRow(TabSplit split) => new(new WebView2(), TabPlace.Regular) { IsSplitRow = true, Split = split };

    /// <summary>A row's split layout (both pages), or its usual one.</summary>
    public Visibility SplitVisibility => IsSplitRow ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SingleVisibility => IsSplitRow ? Visibility.Collapsed : Visibility.Visible;

    private bool _isFront = true;

    /// <summary>In a split, whether this is the page you're on: its half of the row is the brighter one.</summary>
    public bool IsFront
    {
        get => _isFront;
        set
        {
            if (_isFront == value) return;
            _isFront = value;
            OnPropertyChanged(nameof(HalfOpacity));
        }
    }

    public double HalfOpacity => (_isFront ? 1 : 0.55) * ContentOpacity;

    /// <summary>When LinkPress happened, from Environment.TickCount64.</summary>
    public long LinkPressTime { get; set; }

    private bool _isPlayingAudio;
    private bool _isMuted;

    /// <summary>Its page is playing sound (the engine's own measure, muted or not). MediaControls.cs.</summary>
    public bool IsPlayingAudio
    {
        get => _isPlayingAudio;
        set
        {
            if (_isPlayingAudio == value) return;
            _isPlayingAudio = value;
            OnAudioChanged();
        }
    }

    /// <summary>Its page's sound is off (the speaker on its row, or the mini player's mute).</summary>
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (_isMuted == value) return;
            _isMuted = value;
            OnAudioChanged();
        }
    }

    /// <summary>
    /// When it last started or stopped playing sound (Environment.TickCount64), or null if it never
    /// has: the mini player is for the tab that started last, and stays a while once it stops.
    /// </summary>
    public long? AudioSince { get; set; }

    private TabAudioIcon AudioIcon => TabAudio.Icon(_isPlayingAudio, _isMuted);

    /// <summary>The row's speaker (Segoe Fluent Icons): Volume while it plays, Mute while it's muted.</summary>
    public string AudioGlyph => AudioIcon == TabAudioIcon.Muted ? ((char)0xE74F).ToString() : ((char)0xE767).ToString();
    public Visibility AudioVisibility => AudioIcon == TabAudioIcon.None ? Visibility.Collapsed : Visibility.Visible;
    public string AudioName => _isMuted ? "Unmute tab" : "Mute tab";

    private void OnAudioChanged()
    {
        OnPropertyChanged(nameof(AudioGlyph));
        OnPropertyChanged(nameof(AudioVisibility));
        OnPropertyChanged(nameof(AudioName));
    }

    public string Title
    {
        get => _title;
        set
        {
            if (_title == value) return;
            _title = value;
            OnPropertyChanged();
        }
    }

    public BitmapImage? Favicon
    {
        get => _favicon;
        set
        {
            _favicon = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlaceholderVisibility));
        }
    }

    /// <summary>Shows the globe glyph until the site's favicon loads.</summary>
    public Visibility PlaceholderVisibility => _favicon is null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>True while a page this tab opened is showing in Peek.</summary>
    public bool HasPeek
    {
        get => _hasPeek;
        set
        {
            if (_hasPeek == value) return;
            _hasPeek = value;
            if (!value) _peekFavicon = null;
            OnPropertyChanged(nameof(PeekFavicon));
            OnPropertyChanged(nameof(PeekPlaceholderVisibility));
            OnPropertyChanged(nameof(PeekBadgeVisibility));
            OnPropertyChanged(nameof(RowButtonMargin));
        }
    }

    /// <summary>
    /// A row's ×/− keeps to the row's end, and steps aside for the Peek badge, which shares its
    /// column (an extra column would leave a gap even when empty).
    /// </summary>
    public Thickness RowButtonMargin => _hasPeek ? new Thickness(0, 0, 28, 0) : default;

    /// <summary>The icon of the page open in Peek, shown as a badge on the tile.</summary>
    public BitmapImage? PeekFavicon
    {
        get => _peekFavicon;
        set
        {
            _peekFavicon = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PeekPlaceholderVisibility));
        }
    }

    public Visibility PeekBadgeVisibility => _hasPeek ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Globe glyph on the badge until (or unless) the Peek page has an icon.</summary>
    public Visibility PeekPlaceholderVisibility => _peekFavicon is null ? Visibility.Visible : Visibility.Collapsed;

    private string? _meetingText;

    /// <summary>
    /// A calendar tab's next meeting (GoogleCalendar.cs): "in 5m" under its icon, or Join once it
    /// can be joined (<see cref="MeetingJoinUrl"/>).
    /// </summary>
    public void SetMeeting(string? text, string? joinUrl)
    {
        if (_meetingText == text && MeetingJoinUrl == joinUrl) return;
        _meetingText = text;
        MeetingJoinUrl = text is null ? null : joinUrl;
        OnPropertyChanged(nameof(MeetingText));
        OnPropertyChanged(nameof(MeetingTextVisibility));
        OnPropertyChanged(nameof(JoinVisibility));
        OnPropertyChanged(nameof(FaviconMargin));
    }

    public string? MeetingJoinUrl { get; private set; }

    public string MeetingText => _meetingText ?? "";

    public Visibility MeetingTextVisibility => _meetingText is not null && MeetingJoinUrl is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility JoinVisibility => MeetingJoinUrl is not null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>On an Essentials tile, the icon moves up to make room for the countdown below it.</summary>
    public Thickness FaviconMargin => _meetingText is null ? new Thickness(0) : new Thickness(0, 0, 0, 16);

    // List items fall back to ToString() for their accessible name.
    public override string ToString() => Title;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
