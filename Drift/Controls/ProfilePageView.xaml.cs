using System.Collections.Generic;
using System.Collections.ObjectModel;
using Drift.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Drift;

/// <summary>
/// One Profile's sidebar (ProfilePageView.xaml): its lists show that Profile's Essentials, folders, pinned tabs and tabs, always. The
/// window wires up what's done in them (ProfilePages.cs).
/// </summary>
public sealed partial class ProfilePageView : UserControl
{
    public ProfilePageView(Profile profile)
    {
        Profile = profile;
        InitializeComponent();
        IsHitTestVisible = false;
        EssentialList.ItemsSource = profile.Essentials;
        FolderList.ItemsSource = FolderRows;
        GroupList.ItemsSource = GroupRows;
        PinnedList.ItemsSource = profile.Pinned;
        TabList.ItemsSource = profile.Tabs;
        // Off screen, it's drawn (to slide in whole) but the keyboard can't go into it.
        GettingFocus += (_, e) => { if (!OnScreen) e.TryCancel(); };
        // Passes a sideways swipe on to the Library's; too few rows to scroll, a swipe up or down moves nothing.
        ScrollHold.Attach(Scroll, Rows);
    }

    /// <summary>The Profile it's the sidebar of.</summary>
    internal Profile Profile { get; }

    /// <summary>What its folder list shows: folder headers (TabFolder) and the tabs under them (BrowserTab).</summary>
    internal ObservableCollection<object> FolderRows { get; } = new();

    /// <summary>What its unpinned group list shows, as FolderRows does for the pinned groups.</summary>
    internal ObservableCollection<object> GroupRows { get; } = new();

    /// <summary>The group list for a side: the pinned groups', between the Essentials and the pinned tabs, or the others', above the tabs.</summary>
    internal ListView GroupListFor(bool pinned) => pinned ? FolderList : GroupList;

    /// <summary>What that list shows.</summary>
    internal ObservableCollection<object> GroupRowsFor(bool pinned) => pinned ? FolderRows : GroupRows;

    private bool _onScreen;

    /// <summary>
    /// The Profile's sidebar is the one in view. The others are drawn too, cut off beside it, so one slides in whole
    /// from its first frame (shown only as a slide began, it came in a piece at a time, its icons last); they can't be
    /// clicked, focused or read out.
    /// </summary>
    internal bool OnScreen
    {
        get => _onScreen;
        set
        {
            if (_onScreen == value) return;
            _onScreen = value;
            IsHitTestVisible = value;
            // UI Automation asks again for what's in it.
            FrameworkElementAutomationPeer.FromElement(this)?.InvalidatePeer();
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ProfilePagePeer(this);

    /// <summary>A Profile's sidebar to UI Automation: its rows only while it's the one on screen.</summary>
    private sealed class ProfilePagePeer(ProfilePageView owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override IList<AutomationPeer> GetChildrenCore() =>
            owner.OnScreen ? base.GetChildrenCore() : new List<AutomationPeer>();

        protected override bool IsOffscreenCore() => !owner.OnScreen || base.IsOffscreenCore();
    }

    /// <summary>Its header, as the Profile is now: shown or not, its icon and name.</summary>
    internal void ShowHeader(bool shown)
    {
        Header.Visibility = Ui.Shown(shown);
        HeaderName.Text = Profile.Name;
        HeaderGlyph.Glyph = Profile.Glyph;
        HeaderGlyph.FontFamily = Ui.IconFont(Profile.Glyph);
        HeaderGlyph.Visibility = Ui.Shown(!Profile.HasEmoji);
        HeaderEmoji.Text = Profile.Emoji;
        HeaderEmoji.Visibility = Ui.Shown(Profile.HasEmoji);
    }

    /// <summary>The tab list fills what's left below the tabs.</summary>
    private void Scroll_SizeChanged(object sender, SizeChangedEventArgs e) => Rows.MinHeight = e.NewSize.Height;

    // Hovered, the header has a tab's hover fill (DESIGN.md › every clickable row has a hover state), and its … shows.
    private void Header_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        HeaderFill.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["DriftHoverBrush"];
        HeaderMoreButton.Visibility = Visibility.Visible;
    }

    private void Header_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        HeaderFill.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        // Its menu open, the … stays (Zen's [open='true']); MenuClosed hides it.
        if (!MenuOpen) HeaderMoreButton.Visibility = Visibility.Collapsed;
    }

    /// <summary>The header's menu is open from its …</summary>
    internal bool MenuOpen { get; set; }

    private void NewTabButton_PointerEntered(object sender, PointerRoutedEventArgs e) => NewTabHint.Opacity = 1;

    private void NewTabButton_PointerExited(object sender, PointerRoutedEventArgs e) => NewTabHint.Opacity = 0;
}
