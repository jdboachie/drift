using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Drift.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Drift;

/// <summary>
/// Every Profile has a sidebar of its own (Controls/ProfilePageView.xaml), always showing its Essentials, folders, pinned
/// tabs and tabs. They sit on ProfileStrip side by side, a gap apart, in the Profiles' order, and the strip is moved so the one
/// you're on is in view; the others are cut off at the sidebar's edges. Going to another Profile, or swiping, slides the
/// strip (Profiles.cs) and the real sidebar slides in, never a picture of it (the user, 2026-11-24: "it should feel as
/// natural as sliding a piece of paper across a table. why tf are we using a picture?"), as Zen lays its spaces out
/// (translateX by index) and as Arc's look.
/// <para>
/// The window's code works with the sidebar on screen by the names it always had: EssentialList, PinnedList, TabList, FolderList,
/// FolderRows and the rest are the current page's. Every one is drawn all the time, cut off beside the one in view, so a
/// Profile slides in whole from its first frame; only the one on screen can be clicked, focused or read out
/// (ProfilePageView.OnScreen).
/// </para>
/// </summary>
public sealed partial class MainWindow
{
    private readonly Dictionary<Profile, ProfilePageView> _profilePages = new();

    /// <summary>The sidebar on screen: the Profile you're on's.</summary>
    private ProfilePageView CurrentPage => PageOf(_profileShown);

    private ScrollViewer SidebarScroll => CurrentPage.Scroll;
    private Grid SidebarScrollContent => CurrentPage.Rows;
    private Grid ProfileHeader => CurrentPage.Header;
    private TextBlock ProfileHeaderName => CurrentPage.HeaderName;
    private TextBox ProfileNameBox => CurrentPage.NameBox;
    private Canvas FolderCardLayer => CurrentPage.FolderCardLayer;
    private GridView EssentialList => CurrentPage.EssentialList;
    private Grid EssentialDropZone => CurrentPage.EssentialDropZone;
    private Microsoft.UI.Xaml.Shapes.Rectangle EssentialDropZoneOutline => CurrentPage.EssentialDropZoneOutline;
    private ListView FolderList => CurrentPage.FolderList;
    private ListView GroupList => CurrentPage.GroupList;
    private ListView PinnedList => CurrentPage.PinnedList;
    private ListView TabList => CurrentPage.TabList;
    private Grid PinnedDropZone => CurrentPage.PinnedDropZone;
    private Microsoft.UI.Xaml.Shapes.Rectangle PinnedDropZoneOutline => CurrentPage.PinnedDropZoneOutline;
    private Grid FolderDropZone => CurrentPage.FolderDropZone;
    private Grid GroupDropZone => CurrentPage.GroupDropZone;

    /// <summary>What the folder list on screen shows: folder headers (TabFolder) and the tabs under them (BrowserTab).</summary>
    public ObservableCollection<object> FolderRows => CurrentPage.FolderRows;

    /// <summary>The sidebar on screen's two group lists: the pinned groups' and the others'.</summary>
    private IEnumerable<ListView> GroupLists => [FolderList, GroupList];

    /// <summary>Which side a group list's row is on: a group's own, a tab's its group's (a live folder's note, pinned).</summary>
    private static bool PinnedSide(object row) => row switch
    {
        TabFolder folder => folder.IsPinned,
        BrowserTab { Folder: { } folder } => folder.IsPinned,
        _ => true,
    };

    /// <summary>The group list on screen a row's in (or would be): its side's.</summary>
    private ListView GroupListOf(object row) => CurrentPage.GroupListFor(PinnedSide(row));

    /// <summary>What the group list on screen a row's in shows.</summary>
    private ObservableCollection<object> GroupRowsOf(object row) => CurrentPage.GroupRowsFor(PinnedSide(row));

    /// <summary>What a group list shows (one of any Profile's).</summary>
    private ObservableCollection<object> RowsOfList(object list) =>
        _profilePages.Values.FirstOrDefault(p => p.GroupList == list) is { } page ? page.GroupRows
        : _profilePages.Values.FirstOrDefault(p => p.FolderList == list)?.FolderRows ?? FolderRows;

    /// <summary>Whether a group list is the pinned groups' (FolderList) rather than the others' (GroupList).</summary>
    private bool IsPinnedGroupList(object list) => !_profilePages.Values.Any(p => p.GroupList == list);

    /// <summary>The layer a group list's cards are drawn whole on mid-drag (DragCards.cs).</summary>
    private Canvas CardLayerOf(ListViewBase list) =>
        _profilePages.Values.FirstOrDefault(p => p.GroupList == list)?.GroupCardLayer
        ?? _profilePages.Values.FirstOrDefault(p => p.FolderList == list)?.FolderCardLayer ?? FolderCardLayer;

    /// <summary>A Profile's sidebar, made the first time it's wanted.</summary>
    private ProfilePageView PageOf(Profile profile)
    {
        if (_profilePages.TryGetValue(profile, out var page)) return page;
        page = new ProfilePageView(profile);
        _profilePages[profile] = page;
        WirePage(page);
        ProfileStrip.Children.Add(page);
        page.ShowHeader(ProfileHeaderShown(profile));
        page.NewTabHint.Text = NewTabHintText();
        return page;
    }

    /// <summary>The header's …: the sidebar's menu (the Profile's options, as its right-click), under it.</summary>
    private void ShowProfileOptions(ProfilePageView page)
    {
        if (ProfileViewport.ContextFlyout is not MenuFlyout menu) return;
        page.MenuOpen = true;
        void Closed(object? sender, object e)
        {
            menu.Closed -= Closed;
            page.MenuOpen = false;
            page.HeaderMoreButton.Visibility = Visibility.Collapsed;
        }
        menu.Closed += Closed;
        menu.ShowAt(page.HeaderMoreButton, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
        {
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedLeft,
        });
    }

    /// <summary>What's done in a sidebar goes to the window's code, as it did when there was one sidebar.</summary>
    private void WirePage(ProfilePageView page)
    {
        AddPressFeedback(page.NewTabButton, 0.98f);
        page.NewTabButton.Click += NewTab_Click;
        page.Header.DoubleTapped += ProfileHeader_DoubleTapped;
        // Its icon changes the icon (a double-click on it isn't a rename), its … opens its menu.
        page.HeaderIconButton.Click += (_, _) => PickProfileIcon(page.Profile, page.HeaderIconButton);
        page.HeaderIconButton.DoubleTapped += (_, e) => e.Handled = true;
        page.HeaderMoreButton.Click += (_, _) => ShowProfileOptions(page);
        page.NameBox.KeyDown += ProfileName_KeyDown;
        page.NameBox.LostFocus += ProfileName_LostFocus;
        page.FolderList.SelectionChanged += (s, e) => { if (page == CurrentPage) FolderList_SelectionChanged(s, e); };
        page.GroupList.SelectionChanged += (s, e) => { if (page == CurrentPage) FolderList_SelectionChanged(s, e); };
        // Where a split's row in a group was grabbed, for its own drag (GroupRowDrag.cs).
        page.GroupList.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(GroupList_PointerPressed), handledEventsToo: true);
        // Its Essentials fill the sidebar's width, as many across as fit (LayoutEssentials).
        page.EssentialList.SizeChanged += (_, _) => LayoutEssentials(page);
        page.Profile.Essentials.CollectionChanged += (_, _) => LayoutEssentials(page);
        foreach (var list in new ListViewBase[] { page.EssentialList, page.FolderList, page.PinnedList, page.GroupList, page.TabList })
        {
            if (list != page.FolderList && list != page.GroupList) list.SelectionChanged += (s, e) => { if (page == CurrentPage) TabList_SelectionChanged(s, e); };
            list.DragItemsStarting += Tabs_DragItemsStarting;
            list.DragItemsCompleted += Tabs_DragItemsCompleted;
            list.ContainerContentChanging += ListItem_ContainerContentChanging;
            // The lists handle drags themselves (to reorder), marking drops from other lists handled too, so these
            // listen to handled events as well. Entering too, not only moving: the ghost and what a drop would do show
            // from the start.
            list.AddHandler(UIElement.DragEnterEvent, new DragEventHandler(Tabs_DragOver), handledEventsToo: true);
            list.AddHandler(UIElement.DragOverEvent, new DragEventHandler(Tabs_DragOver), handledEventsToo: true);
            list.AddHandler(UIElement.DropEvent, new DragEventHandler(Tabs_Drop), handledEventsToo: true);
            list.AddHandler(UIElement.DragLeaveEvent, new DragEventHandler(Tabs_DragLeave), handledEventsToo: true);
        }
        foreach (var zone in new[] { page.EssentialDropZone, page.PinnedDropZone, page.FolderDropZone, page.GroupDropZone })
        {
            zone.DragOver += Tabs_DragOver;
            zone.DragLeave += Tabs_DragLeave;
            zone.Drop += Tabs_Drop;
        }
        // The folder list on screen's rows changing slide (Slides.cs); another Profile's change out of sight.
        page.FolderRows.CollectionChanged += (_, _) => { if (page == CurrentPage) NoteSlideStart(); };
        page.GroupRows.CollectionChanged += (_, _) => { if (page == CurrentPage) NoteSlideStart(); };
    }

    /// <summary>One of the Profiles' group lists, pinned or not.</summary>
    private bool IsFolderList(object sender) => _profilePages.Values.Any(p => p.FolderList == sender || p.GroupList == sender);

    /// <summary>One of the Profiles' pinned or regular tab lists.</summary>
    private bool IsTabList(object sender) => _profilePages.Values.Any(p => p.PinnedList == sender || p.TabList == sender);

    /// <summary>A Profile gone: its sidebar too.</summary>
    private void DropGoneProfilePages()
    {
        foreach (var gone in _profilePages.Keys.Where(s => !Profiles.Contains(s)).ToList())
        {
            ProfileStrip.Children.Remove(_profilePages[gone]);
            _profilePages.Remove(gone);
        }
    }

    // The strip is being moved (a swipe, or a page turning): it isn't put back.
    private bool _profilesMoving;

    /// <summary>Where the strip is with this Profile in view.</summary>
    private double StripAt(Profile profile) => ProfileOrder.StripAt(Profiles.IndexOf(profile), ProfileViewport.ActualWidth);

    /// <summary>
    /// Each Profile's sidebar in its place, a page apart, as wide and tall as the sidebar's room for them; the one you're
    /// on is the one on screen. At rest, the strip with it in view.
    /// </summary>
    private void PlaceProfilePages()
    {
        if (ProfileStrip is null) return;
        DropGoneProfilePages();
        var width = ProfileViewport.ActualWidth;
        for (var i = 0; i < Profiles.Count; i++)
        {
            var page = PageOf(Profiles[i]);
            Canvas.SetLeft(page, ProfileOrder.PageLeft(i, width));
            page.Width = width;
            page.Height = ProfileViewport.ActualHeight;
            page.OnScreen = Profiles[i] == _profileShown;
        }
        if (!_profilesMoving) StopPageSpring(ProfileStrip, StripAt(_profileShown));
    }

    /// <summary>Whether a Profile's sidebar shows its header (Hide Profile header in its menu; while it's renamed, it does).</summary>
    private bool ProfileHeaderShown(Profile profile) => !_private && (Settings.Current.ShowProfileHeader || profile.IsRenaming);

    /// <summary>The shortcut New Tab shows at its end while hovered, like Arc's.</summary>
    private static string NewTabHintText() =>
        KeyHint.From(Settings.ShortcutFor(Settings.Actions.First(a => a.Id == "newtab")).ToString());
}
