using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
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

/// <summary>
/// Two to four tabs side by side (SplitShares.MaxPages), left to right in Pages, each with its share of
/// the width in Shares.
/// </summary>
public sealed class TabSplit : INotifyPropertyChanged
{
    private readonly List<BrowserTab> _pages;

    public TabSplit(IEnumerable<BrowserTab> pages, IReadOnlyList<double>? shares = null)
    {
        _pages = pages.ToList();
        Shares = SplitShares.Checked(shares, _pages.Count);
    }

    /// <summary>Left to right.</summary>
    public IReadOnlyList<BrowserTab> Pages => _pages;

    /// <summary>Each page's share of the width (adding up to 1), in the order of Pages.</summary>
    public double[] Shares { get; set; }

    public int Count => _pages.Count;

    /// <summary>No room for another page.</summary>
    public bool IsFull => _pages.Count >= SplitShares.MaxPages;

    public int IndexOf(BrowserTab page) => _pages.IndexOf(page);

    /// <summary>A page joins at <paramref name="at"/>, taking an even share.</summary>
    public void Insert(int at, BrowserTab page)
    {
        at = Math.Clamp(at, 0, _pages.Count);
        Shares = SplitShares.Insert(Shares, at);
        _pages.Insert(at, page);
        Changed();
    }

    /// <summary>A page leaves; the others widen into its place.</summary>
    public void Remove(BrowserTab page)
    {
        if (_pages.IndexOf(page) is not (>= 0 and var at)) return;
        Shares = SplitShares.Remove(Shares, at);
        _pages.RemoveAt(at);
        Changed();
    }

    /// <summary>A page moves to another place, every page keeping its width.</summary>
    public void Move(int from, int to)
    {
        if (from == to) return;
        Shares = SplitShares.Moved(Shares, from, to);
        var page = _pages[from];
        _pages.RemoveAt(from);
        _pages.Insert(Math.Clamp(to, 0, _pages.Count), page);
        Changed();
    }

    /// <summary>The page beside one (on its left, else its right): the one you're on after it goes.</summary>
    public BrowserTab? Beside(BrowserTab page) =>
        _pages.IndexOf(page) is var at && at > 0 ? _pages[at - 1] : _pages.Count > 1 ? _pages[1] : null;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Its row in the tab list (a BrowserTab with IsSplitRow, standing for all its pages).</summary>
    public BrowserTab? Row { get; set; }

    // The row's places for its pages (MainWindow.xaml): the page in each, or none, and their columns'
    // widths, a place with no page taking no room.
    public BrowserTab? Page1 => At(0);
    public BrowserTab? Page2 => At(1);
    public BrowserTab? Page3 => At(2);
    public BrowserTab? Page4 => At(3);
    public GridLength Slot3Width => SlotWidth(2);
    public GridLength Slot4Width => SlotWidth(3);
    public GridLength Gap3Width => new(_pages.Count > 2 ? 3 : 0);
    public GridLength Gap4Width => new(_pages.Count > 3 ? 3 : 0);
    public Visibility Slot3Visibility => _pages.Count > 2 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility Slot4Visibility => _pages.Count > 3 ? Visibility.Visible : Visibility.Collapsed;

    private BrowserTab? At(int i) => i < _pages.Count ? _pages[i] : null;

    private GridLength SlotWidth(int i) => i < _pages.Count ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

    private void Changed()
    {
        foreach (var name in new[] { nameof(Pages), nameof(Count), nameof(Page1), nameof(Page2), nameof(Page3), nameof(Page4),
                     nameof(Slot3Width), nameof(Slot4Width), nameof(Gap3Width), nameof(Gap4Width), nameof(Slot3Visibility), nameof(Slot4Visibility) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
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
            OnPropertyChanged(nameof(RowActionGlyph));
            OnPropertyChanged(nameof(RowActionName));
        }
    }

    public double ContentOpacity => _isUnloaded ? 0.5 : 1;

    /// <summary>
    /// A pinned row's hover button: "−" unloads it; once unloaded, "×" removes it. While it has a page
    /// in Peek, "×", as that goes first (TabCloses).
    /// </summary>
    public string PinnedActionGlyph => _isUnloaded || _hasPeek ? CloseGlyph : UnloadGlyph;

    public string PinnedActionName => TabCloses.Name(_hasPeek, pinned: true, _isUnloaded);

    /// <summary>A regular row's × (and a split half's): the tab, or first its Peek.</summary>
    public string CloseName => TabCloses.Name(_hasPeek, pinned: false, unloaded: false);

    public TabPlace Place
    {
        get => _place;
        set
        {
            if (_place == value) return;
            _place = value;
            OnPropertyChanged(nameof(RowActionGlyph));
            OnPropertyChanged(nameof(RowActionName));
        }
    }

    private TabPlace _place;

    /// <summary>
    /// The end button of a row in the sidebar's tab lists (TabRowTemplate): a pinned tab's −/× (PinnedAction…),
    /// a regular tab's × (Close…).
    /// </summary>
    public string RowActionGlyph => IsPinned ? PinnedActionGlyph : CloseGlyph;

    public string RowActionName => IsPinned ? PinnedActionName : CloseName;

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

    /// <summary>The pressed link's box, the part of it that shows (in the view's coordinates), for Peek's card to grow out of.</summary>
    public Area? LinkBox { get; set; }

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

    /// <summary>In a split, whether this is the page you're on: the selected-tab card sits on its half of the row.</summary>
    public bool IsFront
    {
        get => _isFront;
        set
        {
            if (_isFront == value) return;
            _isFront = value;
            OnPropertyChanged();
        }
    }

    /// <summary>When LinkPress happened, from Environment.TickCount64.</summary>
    public long LinkPressTime { get; set; }

    /// <summary>
    /// When a link was last Ctrl+clicked or middle-clicked here (Environment.TickCount64), for the
    /// new tab it opens to follow the "switch to new tabs" setting; 0 once used or after a plain press.
    /// </summary>
    public long AsidePressTime { get; set; }
    /// <summary>Shift was held with it, which turns the setting around for that click.</summary>
    public bool AsidePressShift { get; set; }

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

    /// <summary>
    /// Its sound is media (MediaWorth), not just a notification's chime: only then does the mini player
    /// come for it. Till it goes to another page.
    /// </summary>
    public bool IsMedia { get; set; }

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

    /// <summary>What its row says: the name you gave it (Rename…), else its page's title.</summary>
    public string Title
    {
        get => _name ?? _title;
        set
        {
            if (_title == value) return;
            _title = value;
            if (_name is null) OnPropertyChanged();
        }
    }

    /// <summary>Its page's own title, whatever it's been named.</summary>
    public string PageTitle => _title;

    private string? _name;

    /// <summary>The name you gave it (its menu's Rename…); null for its page's title.</summary>
    public string? Name
    {
        get => _name;
        set
        {
            value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (_name == value) return;
            _name = value;
            OnPropertyChanged(nameof(Title));
        }
    }

    private bool _isRenaming;

    /// <summary>Its title is being edited in place (Rename…).</summary>
    public bool IsRenaming
    {
        get => _isRenaming;
        set
        {
            if (_isRenaming == value) return;
            _isRenaming = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Its icon: the one you chose (Change icon…), else its site's.</summary>
    public BitmapImage? Favicon
    {
        get => _iconImage ?? _favicon;
        set
        {
            _favicon = value;
            if (_iconImage is not null) return;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlaceholderVisibility));
        }
    }

    private BitmapImage? _iconImage;

    /// <summary>The icon you chose (an emoji, or a symbol: CustomIcon); null for its site's.</summary>
    public string? Icon { get; private set; }

    /// <summary>Its own icon, drawn (MainWindow's IconPicker.cs); null for its site's again.</summary>
    public void SetIcon(string? icon, BitmapImage? image)
    {
        Icon = image is null ? null : icon;
        _iconImage = image;
        OnPropertyChanged(nameof(Favicon));
        OnPropertyChanged(nameof(PlaceholderVisibility));
    }

    /// <summary>Shows the globe glyph until the site's favicon loads.</summary>
    public Visibility PlaceholderVisibility => Favicon is null ? Visibility.Visible : Visibility.Collapsed;

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
            OnPropertyChanged(nameof(TitleEndMargin));
            OnPropertyChanged(nameof(PinnedActionGlyph));
            OnPropertyChanged(nameof(PinnedActionName));
            OnPropertyChanged(nameof(CloseName));
            OnPropertyChanged(nameof(RowActionGlyph));
            OnPropertyChanged(nameof(RowActionName));
        }
    }

    /// <summary>
    /// A row's ×/− and Peek badge sit over the end of the title's column, so the title has the row's
    /// width when neither shows (a column for them would keep its room while they're hidden). The
    /// ×/− keeps to the row's end, and steps aside for the badge.
    /// </summary>
    public Thickness RowButtonMargin => _hasPeek ? new Thickness(0, 0, 28, 0) : default;

    /// <summary>The title stops short of the Peek badge while there is one; the ×/− makes its own room on hover.</summary>
    public Thickness TitleEndMargin => _hasPeek ? new Thickness(0, 0, 28, 0) : default;

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

    private bool _isNew;

    /// <summary>In a live folder: arrived since you last looked, marked with a dot until it's opened (LiveFolders.cs).</summary>
    public bool IsNew
    {
        get => _isNew;
        set
        {
            if (_isNew == value) return;
            _isNew = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NewVisibility));
        }
    }

    public Visibility NewVisibility => _isNew ? Visibility.Visible : Visibility.Collapsed;

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
