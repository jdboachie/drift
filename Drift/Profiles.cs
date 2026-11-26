using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using Drift.Models;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Drift;

/// <summary>
/// Profiles, as Dia's: a sidebar of its own for each part of your life,
/// with its own Essentials, folders, pinned tabs and tabs, and its own data (ProfileData.cs). Arc's Spaces' model,
/// menus and wording were read from Arc for Windows 1.133 itself; its behaviour from its release notes (ProfileOrder).
/// <list type="bullet">
/// <item>The sidebar shows the Profile you're on, headed with its icon and name (Hide Profile header in its menu). The
/// others keep their tabs as they were, loaded or not, and the tab you were last on in each.</item>
/// <item>Go to another: its icon between the Library and + (only with two or more), Ctrl+1–9, the mouse's back and
/// forward buttons over the sidebar, or two fingers sideways over it (Library.cs), the next Profile being to the right.
/// Swiping right from the first opens the Library, as that swipe did before Profiles. A tab picked from the command bar
/// in another Profile takes you there.</item>
/// <item>New Profile (+, first, as Arc's) takes the sidebar's place with Create a Profile: its name and icon, Create
/// Profile or Cancel. It goes next to the one you're on, and you go to it.</item>
/// <item>Its menu (right-click below the tabs, or its icon): Rename Profile (in its header), Change Profile icon…, Hide
/// Profile header, Delete Profile… (asked first; any but your default). A tab's menu: Move to Profile.</item>
/// </list>
/// While a tab in another Profile changes (a page there closing itself, a live folder refreshing), that Profile is
/// worked in for the moment (InProfile): _profile is where tabs are added and taken, _profileShown is on screen.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The Profiles, in order (the switcher's).</summary>
    public ObservableCollection<Profile> Profiles { get; } = new();

    // The Profile being worked in: the one on screen, but for a moment while a tab in another changes (InProfile).
    private Profile _profile = new() { Name = ProfileIds.DefaultName, IsCurrent = true };
    // The Profile on screen.
    private Profile _profileShown = null!;
    // Set while the Profile on screen changes: its rows don't slide row by row (Slides.cs).
    private bool _switchingProfile;

    /// <summary>Before the window's made: the one Profile there always is.</summary>
    private void SetUpProfiles()
    {
        _profileShown = _profile;
        Profiles.Add(_profile);
        WatchProfile(_profile);
    }

    /// <summary>Once the window's made: the mouse's back and forward buttons over the sidebar, and the switcher's room.</summary>
    private void WireProfiles()
    {
        Sidebar.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Sidebar_ProfileButtons), handledEventsToo: true);
        Sidebar.SizeChanged += (_, _) => FitProfileSwitcher();
        // A Profile added, gone or moved: each sidebar in its place (ProfilePages.cs).
        Profiles.CollectionChanged += (_, _) =>
        {
            FinishPaging();
            ShowProfileHeader();
        };
        ProfileCreation.Owner = this;
        // The other Profiles' sidebars are cut off at the sidebar's edges, never drawn over the page.
        var viewport = ElementCompositionPreview.GetElementVisual(ProfileViewport);
        viewport.Clip = viewport.Compositor.CreateInsetClip();
        ProfileViewport.SizeChanged += (_, e) =>
        {
            // Narrower or wider mid-slide, the Profile it was going to is somewhere else now: it's there at once.
            if (_profilesMoving && e.NewSize.Width != e.PreviousSize.Width) FinishPaging();
            else PlaceProfilePages();
        };
        PlaceProfilePages();
        ShowProfileHeader();
    }

    private void WatchProfile(Profile profile)
    {
        profile.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not (nameof(Profile.Name) or nameof(Profile.Icon))) return;
            if (_profilePages.TryGetValue(profile, out var page)) page.ShowHeader(ProfileHeaderShown(profile));
        };
    }

    /// <summary>The Profile a tab is in.</summary>
    private Profile? ProfileOf(BrowserTab tab) => Profiles.FirstOrDefault(s => TabsIn(s).Contains(tab));

    /// <summary>In the sidebar now: a tab in the Profile on screen.</summary>
    private bool IsShown(BrowserTab tab) => ProfileOf(tab) == _profileShown;

    /// <summary>The folders a folder is among: its Profile's.</summary>
    private FolderTree TreeOf(TabFolder folder) =>
        Profiles.FirstOrDefault(s => s.Tree.AllFolders.Contains(folder))?.Tree ?? _folderTree;

    /// <summary>Works on another Profile's tabs for a moment, as if it were the one on screen; the sidebar doesn't change.</summary>
    private void InProfile(Profile profile, Action action)
    {
        var was = _profile;
        _profile = profile;
        try
        {
            action();
        }
        finally
        {
            _profile = was;
        }
    }

    /// <summary>
    /// The Profiles from last time; the first is the one the sidebar's lists were made with. Each has its own engine
    /// profile: where Profiles shared one, the first keeps it and the others start fresh (ProfileIds.Unshare).
    /// </summary>
    private List<Profile> RestoreProfiles(List<SessionProfile> saved)
    {
        var profiles = new List<Profile>();
        var engineProfiles = ProfileIds.Unshare(saved.Select(s => s.Engine).ToList());
        for (var i = 0; i < saved.Count; i++)
        {
            var profile = i == 0 ? _profile : new Profile();
            profile.Name = string.IsNullOrWhiteSpace(saved[i].Name) ? ProfileOrder.DefaultName(profiles.Select(s => s.Name).ToList()) : saved[i].Name;
            profile.Icon = saved[i].Icon;
            profile.Engine = new EngineProfile(engineProfiles[i]);
            if (i > 0)
            {
                Profiles.Add(profile);
                WatchProfile(profile);
                WatchProfileForSlides(profile);
            }
            profiles.Add(profile);
        }
        return profiles;
    }

    // ───────────────────────── Going to a Profile ─────────────────────────

    /// <summary>
    /// To another Profile, paging as Arc's sidebar does: the one you're on slides off to one side as that one comes in
    /// beside it from the other, a later Profile from the right (Paging, below). With <paramref name="show"/>, that tab
    /// in front.
    /// </summary>
    internal void GoToProfile(Profile profile, BrowserTab? show = null)
    {
        if (profile == _profileShown)
        {
            if (show is not null) Activate(show);
            return;
        }
        if (!CanPage)
        {
            FinishPaging();
            ShowProfile(profile, show);
            return;
        }
        // Mid-slide, it turns round from where it's drawn, as fast as it was going; the Profile it was going to is no
        // longer where you're going.
        _pageArrival = null;
        var (x, velocity) = CatchStrip();
        PageTo(profile, show, x, velocity);
    }

    /// <summary>
    /// The Profile on screen, at once: its sidebar in view, and a tab of it in front (the one asked for, else the one you
    /// were last on there). One with none shows no page. Anything sliding gets where it
    /// was going first.
    /// </summary>
    private void ShowProfile(Profile to, BrowserTab? show = null)
    {
        if (_profilesMoving || _pageArrival is not null) FinishPaging();
        SwitchProfile(to, show);
    }

    /// <summary>ShowProfile, but the strip left as it is: a paging arriving (ArriveInProfile) slides on.</summary>
    private void SwitchProfile(Profile to, BrowserTab? show)
    {
        var from = _profileShown;
        if (to != from)
        {
            EndProfileRename(keep: true);
            _switchingProfile = true;
            var syncing = _syncingSelection;
            _syncingSelection = true;
            try
            {
                // The sidebar left keeps its tab picked: it's the one you'll come back to (Profile.LastActive).
                from.IsCurrent = false;
                to.IsCurrent = true;
                _profile = _profileShown = to;
                RefreshFolderRows();
            }
            finally
            {
                _syncingSelection = syncing;
                _switchingProfile = false;
            }
            PlaceProfilePages();
            ShowProfileHeader();
        }
        var tab = show ?? (to.LastActive is { } last && TabsIn(to).Contains(last) ? last : null);
        if (tab is not null) Activate(tab);
        else ShowNoTab();
        SaveSessionSoon();
    }

    /// <summary>Nothing in front: a Profile with no tabs yet.</summary>
    private void ShowNoTab()
    {
        if (_active is null) return;
        if (_peekView is not null && _closingPeekView is null) HidePeekLayer();
        foreach (var page in _shownPages) page.View.Visibility = Visibility.Collapsed;
        _shownPages.Clear();
        _shownSplit = null;
        LayoutPages();
        PlaceSplitCards();
        _active = null;
        var syncing = _syncingSelection;
        _syncingSelection = true;
        EssentialList.SelectedItem = null;
        PinnedList.SelectedItem = null;
        TabList.SelectedItem = null;
        FolderList.SelectedItem = null;
        GroupList.SelectedItem = null;
        _syncingSelection = syncing;
        UpdateChrome();
    }

    // ── Paging, as Arc's and Zen's ──
    //
    // The Profiles' sidebars sit side by side on ProfileStrip, a page apart (ProfilePages.cs). Going to one slides the strip
    // till it's in view, on a spring, from wherever the strip is and as fast as it was going: a click from rest, a
    // swipe from where the fingers let go. What slides in is the Profile's own sidebar, as it is. Recorded against Arc
    // for Windows 1.133 on 2026-11-24: about 160ms from a click, both Profiles at full strength the whole way, and a swipe
    // follows the fingers with the next Profile beside it. Zen's (ZenSpaceManager) the same: a spring, no bounce.

    // A Profile pages in on this spring (critically damped): from a click, at rest in about 160ms, as Arc's.
    private const double ProfilePageSeconds = 0.18;
    // Each paging, counted: a newer one (or a swipe, or anything that finishes one early) makes an older one stop.
    private int _pageTurn;
    // The spring moving the strip, to stop when something takes over, and what it was set going with, to work out
    // where it's got to (StripNow).
    private readonly Dictionary<UIElement, (Vector3NaturalMotionAnimation Spring, Microsoft.UI.Dispatching.DispatcherQueueTimer AtRest,
        double From, double To, double Velocity, long StartedAt)> _pageSprings = new();
    // The Profile a paging is going to, till its tab's been brought to the front (PageTo).
    private (Profile To, BrowserTab? Show)? _pageArrival;

    /// <summary>The sidebars are on screen to page: animations on, the sidebar showing, not Create a Profile.</summary>
    private bool CanPage =>
        _uiSettings.AnimationsEnabled && SidebarShowing && ProfileViewport.Visibility == Visibility.Visible && ProfileViewport.ActualWidth > 0;

    /// <summary>
    /// Pages to <paramref name="to"/>: the strip slides from <paramref name="x"/>, at <paramref name="velocity"/> (the
    /// fingers' as they let go, or the strip's own, turned round mid-slide), till its sidebar's in view.
    /// </summary>
    private void PageTo(Profile to, BrowserTab? show, double x, double velocity)
    {
        var turn = ++_pageTurn;
        _profilesMoving = true;
        SlidePage(ProfileStrip, x, StripAt(to), velocity, () =>
        {
            if (turn != _pageTurn) return;
            SettleProfilePages();
            FlushSwipeLog();
        });
        // The strip is on its way before the Profile's tab comes to the front, which can hold the window up a frame or
        // two: from a swipe, it carries straight on from the fingers rather than waiting where they let go.
        _pageArrival = (to, show);
        AfterFrames(2, ArriveInProfile);
    }

    /// <summary>The Profile a paging is going to, on screen: its tab in front.</summary>
    private void ArriveInProfile()
    {
        if (_pageArrival is not { } arrival) return;
        _pageArrival = null;
        if (Profiles.Contains(arrival.To)) SwitchProfile(arrival.To, arrival.Show);
    }

    /// <summary>At rest: the strip exactly where it has the sidebar on screen in view.</summary>
    private void SettleProfilePages()
    {
        _profilesMoving = false;
        PlaceProfilePages();
    }

    /// <summary>
    /// Any paging or swipe under way ends where it was going, at once: for a Profile added or gone, the sidebar resized,
    /// Create a Profile, an instant switch. A click or a swipe catches it instead, where it's drawn (CatchStrip).
    /// </summary>
    private void FinishPaging()
    {
        ArriveInProfile();
        _pageTurn++;
        StopProfileFollow(StripAt(_profileShown));
        SettleProfilePages();
    }

    /// <summary>
    /// Slides an element sideways on the page spring, from <paramref name="from"/> (shown from the first frame) to
    /// <paramref name="to"/>, leaving at <paramref name="velocity"/>; <paramref name="done"/> once it's at rest.
    /// Not Spring.To: that sets the end value first, and the end showed for a frame or two before the spring took over.
    /// </summary>
    private void SlidePage(UIElement element, double from, double to, double velocity, Action? done = null)
    {
        StopPageSpring(element, from);
        var seconds = ProfileOrder.SettleSeconds(from - to, velocity, ProfilePageSeconds);
        if (!_uiSettings.AnimationsEnabled || seconds == 0)
        {
            StopPageSpring(element, to);
            done?.Invoke();
            return;
        }
        var spring = CompositionTarget.GetCompositorForCurrentThread().CreateSpringVector3Animation();
        spring.Target = "Translation";
        spring.InitialValue = new Vector3((float)from, 0, 0);
        spring.FinalValue = new Vector3((float)to, 0, 0);
        spring.InitialVelocity = new Vector3((float)velocity, 0, 0);
        spring.DampingRatio = 1;
        // The compositor's Period is 1/ω (Spring.To).
        spring.Period = TimeSpan.FromSeconds(ProfilePageSeconds / (2 * Math.PI));
        var rest = DispatcherQueue.CreateTimer();
        rest.IsRepeating = false;
        rest.Interval = TimeSpan.FromSeconds(seconds);
        rest.Tick += (timer, _) =>
        {
            timer.Stop();
            if (!_pageSprings.TryGetValue(element, out var now) || now.AtRest != timer) return;
            StopPageSpring(element, to);
            done?.Invoke();
        };
        _pageSprings[element] = (spring, rest, from, to, velocity, System.Diagnostics.Stopwatch.GetTimestamp());
        element.StartAnimation(spring);
        rest.Start();
    }

    /// <summary>Stops the page spring on an element, if there is one, and puts it at <paramref name="at"/>.</summary>
    private void StopPageSpring(UIElement element, double at)
    {
        if (!_pageSprings.Remove(element, out var running))
        {
            element.Translation = new Vector3((float)at, 0, 0);
            return;
        }
        running.AtRest.Stop();
        element.StopAnimation(running.Spring);
        Land(element, at);
    }

    /// <summary>
    /// Where the strip is drawn now and how fast it's going: worked out from its spring, if one's moving it (the
    /// compositor moves it, and the window can't ask). The spring's first frame can come a frame after it was started,
    /// so this can be a frame ahead: a pixel or two, early in a slide.
    /// </summary>
    private (double X, double Velocity) StripNow()
    {
        if (!_pageSprings.TryGetValue(ProfileStrip, out var s)) return (ProfileStrip.Translation.X, 0);
        var seconds = System.Diagnostics.Stopwatch.GetElapsedTime(s.StartedAt).TotalSeconds;
        var (offset, velocity) = ProfileOrder.SpringAt(s.From - s.To, s.Velocity, seconds, ProfilePageSeconds);
        return (s.To + offset, velocity);
    }

    /// <summary>
    /// The strip's slide stops where it's drawn now, nothing landing at its end, for a click or the fingers to take
    /// it on from there: a Profile mid-slide can be caught, as a sheet of paper can (the user, 2026-11-24: "it should
    /// feel as natural as sliding a piece of paper across a table").
    /// </summary>
    private (double X, double Velocity) CatchStrip()
    {
        var now = StripNow();
        _pageTurn++;
        StopPageSpring(ProfileStrip, now.X);
        return now;
    }

    /// <summary>
    /// An element an animation was moving, put exactly at <paramref name="x"/>. Stopped, it stays wherever the animation
    /// had got to, and setting the property to what it already holds changes nothing (Spring.Jump): a 1ms animation
    /// lands it on the value.
    /// </summary>
    private static void Land(UIElement element, double x)
    {
        var value = new Vector3((float)x, 0, 0);
        element.Translation = value;
        var snap = CompositionTarget.GetCompositorForCurrentThread().CreateVector3KeyFrameAnimation();
        snap.Target = "Translation";
        snap.InsertKeyFrame(1, value);
        snap.Duration = TimeSpan.FromMilliseconds(1);
        element.StartAnimation(snap);
    }

    /// <summary>Runs <paramref name="action"/> once this many more frames have been drawn.</summary>
    private static void AfterFrames(int frames, Action action)
    {
        EventHandler<object>? tick = null;
        tick = (_, _) =>
        {
            if (--frames > 0) return;
            CompositionTarget.Rendering -= tick;
            action();
        };
        CompositionTarget.Rendering += tick;
    }

    /// <summary>The mouse's back and forward buttons over the sidebar go from Profile to Profile, as in Arc.</summary>
    private void Sidebar_ProfileButtons(object sender, PointerRoutedEventArgs e)
    {
        if (_private || Profiles.Count < 2) return;
        var point = e.GetCurrentPoint(Sidebar).Properties;
        var by = point.IsXButton1Pressed ? -1 : point.IsXButton2Pressed ? 1 : 0;
        if (by == 0) return;
        e.Handled = true;
        if (ProfileOrder.Step(Profiles.IndexOf(_profileShown), Profiles.Count, by) is { } at) GoToProfile(Profiles[at]);
    }

    internal void ProfileIcon_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is Profile profile) GoToProfile(profile);
    }

    // ───────────────────────── Its header and the switcher ─────────────────────────

    /// <summary>Each Profile's header, in its sidebar, and the switcher.</summary>
    private void ShowProfileHeader()
    {
        if (ProfileStrip is null) return;
        foreach (var (profile, page) in _profilePages) page.ShowHeader(ProfileHeaderShown(profile));
        ProfileSwitcher.Visibility = Ui.Shown(!_private && Profiles.Count > 1);
        FitProfileSwitcher();
    }

    /// <summary>Icons while they fit between the Library and +; dots past that.</summary>
    private void FitProfileSwitcher()
    {
        var room = Sidebar.ActualWidth - Sidebar.Padding.Left - Sidebar.Padding.Right - ProfileSwitcher.Margin.Left - ProfileSwitcher.Margin.Right;
        var dots = room > 0 && ProfileOrder.Dots(Profiles.Count, room);
        foreach (var profile in Profiles) profile.IsDot = dots;
    }

    // ── Renaming, in its header ──

    internal void ProfileHeader_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => BeginProfileRename();

    private void BeginProfileRename()
    {
        var profile = _profileShown;
        profile.IsRenaming = true;
        ShowProfileHeader();
        ProfileNameBox.Text = profile.Name;
        ProfileNameBox.Visibility = Visibility.Visible;
        ProfileHeaderName.Visibility = Visibility.Collapsed;
        ProfileNameBox.Focus(FocusState.Programmatic);
        ProfileNameBox.SelectAll();
    }

    private void EndProfileRename(bool keep)
    {
        var profile = _profileShown;
        if (!profile.IsRenaming) return;
        profile.IsRenaming = false;
        var name = ProfileNameBox.Text.Trim();
        if (keep && name.Length > 0 && name != profile.Name)
        {
            profile.Name = name;
            SaveSessionSoon();
        }
        ProfileNameBox.Visibility = Visibility.Collapsed;
        ProfileHeaderName.Visibility = Visibility.Visible;
        ShowProfileHeader();
    }

    internal void ProfileName_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not (VirtualKey.Enter or VirtualKey.Escape)) return;
        e.Handled = true;
        EndProfileRename(keep: e.Key == VirtualKey.Enter);
        _active?.View.Focus(FocusState.Programmatic);
    }

    internal void ProfileName_LostFocus(object sender, RoutedEventArgs e) => EndProfileRename(keep: true);

    // ── Its menu ──

    // The sidebar's menu (right-click below the tabs): the Profile on screen's.
    private void ProfileRename_Click(object sender, RoutedEventArgs e) => DispatcherQueue.TryEnqueue(BeginProfileRename);

    private void ProfileChangeIcon_Click(object sender, RoutedEventArgs e) =>
        DispatcherQueue.TryEnqueue(() => PickProfileIcon(_profileShown, ProfileHeader.Visibility == Visibility.Visible ? ProfileHeader : SidebarScroll));

    private void ProfileHeaderToggle_Click(object sender, RoutedEventArgs e)
    {
        Settings.Update(s => s.ShowProfileHeader = !s.ShowProfileHeader);
        ShowProfileHeader();
    }

    private void ProfileDelete_Click(object sender, RoutedEventArgs e) => AskDeleteProfile(_profileShown);

    /// <summary>A Profile's icon's menu: that Profile's, as Arc's Spaces' (August 2024).</summary>
    internal void ProfileMenu_Opening(object? sender, object e)
    {
        if (sender is not MenuFlyout { Target: FrameworkElement { Tag: Profile profile } icon } menu) return;
        FillMenu(menu,
        [
            Command("Rename Profile", () => AfterMenu(menu, () => { ShowProfile(profile); BeginProfileRename(); })),
            Command("Change Profile icon…", () => AfterMenu(menu, () => PickProfileIcon(profile, icon))),
        ],
        [
            ProfileIds.CanDelete(profile.Engine.Id) ? Command("Delete Profile…", () => AskDeleteProfile(profile)) : null,
        ]);
    }

    private void PickProfileIcon(Profile profile, FrameworkElement anchor) =>
        ShowIconPicker(anchor, profile.Icon, "Remove icon", icon =>
        {
            profile.Icon = icon;
            SaveSessionSoon();
        });

    /// <summary>A Profile in the switcher, at <paramref name="index"/>, watched as the others are.</summary>
    private void AddProfile(Profile profile, int index)
    {
        WatchProfile(profile);
        WatchProfileForSlides(profile);
        Profiles.Insert(index, profile);
    }

    // ── Moving a tab ──

    /// <summary>
    /// Move to Profile: the tab goes to the top of that Profile's tabs (pinned, among its pinned tabs), its page as it
    /// was, starting again in that Profile's data. Out of a folder, it's pinned there. This one carries on without it.
    /// </summary>
    private void MoveTabToProfile(BrowserTab tab, Profile to)
    {
        if (tab.Place == TabPlace.Essential || to == _profile || !Profiles.Contains(to)) return;
        var wasActive = tab == _active;
        if (tab == _peekOwner) ClosePeek(animate: false);
        var fromFolder = tab.Place == TabPlace.Folder;
        DetachTab(tab);
        if (fromFolder) RefreshFolderRows();
        tab.Place = fromFolder ? TabPlace.Pinned : tab.Place;
        if (tab.Place == TabPlace.Pinned) to.Pinned.Add(tab);
        else to.Tabs.Insert(0, tab);
        _recentTabs.Remove(tab);
        if (wasActive)
        {
            to.LastActive = tab;
            tab.View.Visibility = Visibility.Collapsed;
            _shownPages.Remove(tab);
            _active = null;
            var next = _recentTabs.FirstOrDefault(t => !t.IsUnloaded && IsShown(t)) ?? ShownTabs.FirstOrDefault();
            if (next is not null) Activate(next);
            else UpdateChrome();
        }
        // Its page starts again in that Profile's data (ProfileData.cs).
        InProfile(to, () => MoveToEngine(tab, to.Engine));
        SaveSessionSoon();
        CopiedToast.Show($"Moved to “{to.Name}”", TimeSpan.FromSeconds(3), [new ToastAction("Go to Profile", () => { if (ProfileOf(tab) is { } profile) GoToProfile(profile, tab); else Activate(tab); })]);
    }

    // ───────────────────────── Swiping between them ─────────────────────────

    // A swipe over the sidebar (Library.cs): the Library's, or between Profiles, said by the way the fingers go first.
    private enum SwipeKind { Undecided, Library, Profile }
    private SwipeKind _swipeKind, _wheelKind;
    // The fingers decide once they've gone this far.
    private const double SwipeDecidesAt = 6;
    // The Profile a swipe goes to (none past the ends), and which way that is: +1 for fingers right.
    private Profile? _profileSwipeTarget;
    private int _profileSwipeDirection;
    private double _profileSwipeMoved;
    private bool _profileSwiping;
    // How far off its place the strip was when the fingers picked it up mid-slide (0 from rest).
    private double _profileSwipeCarried;
    // From the wheel: how far the fingers have gone, and lately, for their speed.
    private double _profileWheelMoved;
    private readonly List<(long Ms, double Moved)> _profileWheelTrail = new();

    /// <summary>A swipe can go between Profiles: there's more than one, and the Library isn't open.</summary>
    private bool ProfileSwipesOn => !_private && Profiles.Count > 1 && !_libraryOpen && ProfileCreation.Visibility != Visibility.Visible;

    /// <summary>
    /// Which swipe it is, from the way the fingers first went: right from the first Profile, the Library; otherwise
    /// between Profiles (to none past the ends, where the sidebar stays put).
    /// </summary>
    private SwipeKind DecideSwipe(bool fingersRight)
    {
        // Mid-slide, the fingers pick the strip up where it's drawn: from the Profile it was going to, so far off its
        // place. A follow still running (the wheel's) stops where it is.
        ArriveInProfile();
        StopProfileFollow(ProfileStrip.Translation.X);
        var (caught, _) = CatchStrip();
        _profilesMoving = false;
        _profileSwipeCarried = caught - StripAt(_profileShown);
        var at = Profiles.IndexOf(_profileShown);
        var goes = ProfileOrder.Swipe(at, Profiles.Count, fingersRight);
        if (goes == ProfileSwipe.Library)
        {
            // The Library's swipe moves the Library: the strip goes where it was going.
            FinishPaging();
            return SwipeKind.Library;
        }
        _profileSwipeDirection = fingersRight ? 1 : -1;
        _profileSwipeTarget = goes switch
        {
            ProfileSwipe.Next => Profiles[at + 1],
            ProfileSwipe.Previous => Profiles[at - 1],
            _ => null,
        };
        _profileSwipeMoved = 0;
        SwipeLog($"space swipe to {_profileSwipeTarget?.Name ?? "nowhere (an end)"}, caught {_profileSwipeCarried:0.#} off its place");
        if (_profileSwipeTarget is not null || _profileSwipeCarried != 0) _profilesMoving = true;
        else SettleProfilePages();
        return SwipeKind.Profile;
    }

    // While the touchpad drives a swipe, the compositor moves the strip with it (FollowProfileSwipeOnTracker).
    private ExpressionAnimation? _profileFollow;

    /// <summary>
    /// The touchpad's swipe: the compositor moves the strip, the Profile you're on and the one coming beside it, with
    /// the tracker itself, every frame, as the Library's swipe does. Moved from the window's thread they lagged the
    /// fingers, and jumped to catch up as the fingers lifted (the user, 2026-11-24: "its not a smooth.. .linear
    /// interaction. it choppy and weird. it seems to jumpe when i let go of the touchpad"). Past the first or last
    /// Profile nothing follows: it stays put.
    /// </summary>
    private void FollowProfileSwipeOnTracker()
    {
        _profileSwiping = true;
        // Nowhere to go and nothing caught mid-slide: it stays put.
        if (_swipeTracker is null || (_profileSwipeTarget is null && _profileSwipeCarried == 0)) return;
        // ProfileOrder.FollowFrom, written out: from where it was caught, with the fingers, towards the Profile no further
        // than its place, and past an end only back towards where it rests.
        var (low, high) = ProfileOrder.FollowRange(_profileSwipeCarried, _profileSwipeDirection, _profileSwipeTarget is not null,
            ProfileOrder.PageStep(ProfileViewport.ActualWidth));
        var follow = _swipeTracker.Compositor.CreateExpressionAnimation("Vector3(b + Clamp(c + o - t.Position.X, lo, hi), 0, 0)");
        follow.SetReferenceParameter("t", _swipeTracker);
        follow.SetScalarParameter("o", _swipeOrigin);
        follow.SetScalarParameter("b", (float)StripAt(_profileShown));
        follow.SetScalarParameter("c", (float)_profileSwipeCarried);
        follow.SetScalarParameter("lo", (float)low);
        follow.SetScalarParameter("hi", (float)high);
        follow.Target = "Translation";
        _profileFollow = follow;
        ProfileStrip.StartAnimation(follow);
    }

    /// <summary>The compositor's following stops, the strip left where it had it, at <paramref name="x"/>.</summary>
    private void StopProfileFollow(double x)
    {
        if (_profileFollow is not { } follow) return;
        _profileFollow = null;
        ProfileStrip.StopAnimation(follow);
        Land(ProfileStrip, x);
    }

    /// <summary>
    /// The swipe from the touchpad has lifted, at this speed (the tracker's: fingers right negative), the tracker at
    /// <paramref name="at"/> as they lifted, where it stays (SetUpSwipe: no coasting on). The strip is worked out from
    /// that, which is where the compositor drew it, and goes on from exactly there.
    /// </summary>
    private void EndTrackerProfileSwipe(double trackerVelocity, double? at)
    {
        var moved = (at ?? _swipeTracker?.Position.X) is { } position ? _swipeOrigin - position : _profileSwipeMoved;
        _profileSwipeMoved = moved;
        var x = StripAt(_profileShown) + ProfileOrder.FollowFrom(_profileSwipeCarried, moved, _profileSwipeDirection, _profileSwipeTarget is not null, ProfileOrder.PageStep(ProfileViewport.ActualWidth));
        StopProfileFollow(x);
        EndProfileSwipe(-trackerVelocity, x);
    }

    /// <summary>
    /// A sideways wheel's swipe (a mouse's tilt wheel; a touchpad comes through the tracker): the strip goes with it,
    /// the Profile it's going to coming in beside the one you're on; past the first or last Profile it stays put
    /// (ProfileOrder.Follow).
    /// </summary>
    private void FollowProfileSwipe(double moved)
    {
        _profileSwiping = true;
        _profileSwipeMoved = moved;
        var x = StripAt(_profileShown) + ProfileOrder.FollowFrom(_profileSwipeCarried, moved, _profileSwipeDirection, _profileSwipeTarget is not null, ProfileOrder.PageStep(ProfileViewport.ActualWidth));
        ProfileStrip.Translation = new Vector3((float)x, 0, 0);
    }

    /// <summary>
    /// The fingers have lifted, at this speed (fingers right positive), the strip at <paramref name="at"/>: on to the
    /// Profile (ProfileOrder.Commits), carrying on as fast as the fingers went, or back to where it was.
    /// </summary>
    private void EndProfileSwipe(double speed, double? at = null)
    {
        var target = _profileSwipeTarget;
        var direction = _profileSwipeDirection;
        _profileSwipeTarget = null;
        var moved = _profileSwipeCarried + _profileSwipeMoved;
        _profileSwipeCarried = 0;
        if (!_profileSwiping)
        {
            // Decided, but the fingers never went on: wherever a slide was caught, it goes back to its place.
            var still = ProfileStrip.Translation.X;
            var turn0 = ++_pageTurn;
            SlidePage(ProfileStrip, still, StripAt(_profileShown), 0, () => { if (turn0 == _pageTurn) SettleProfilePages(); });
            return;
        }
        _profileSwiping = false;
        var x = at ?? ProfileStrip.Translation.X;
        var carry = Math.Clamp(speed, -4000, 4000);
        var on = target is not null && ProfileOrder.Commits(moved, speed, Math.Max(1, ProfileViewport.ActualWidth), direction, canGo: true);
        SwipeLog($"space swipe let go at {x:0.#} (moved {moved:0.#}) speed {speed:0}: {(on ? "on" : "back")}");
        if (on)
        {
            PageTo(target!, null, x, carry);
            return;
        }
        // Back where it was, the Profile it would have gone to going back beside it.
        var turn = ++_pageTurn;
        SlidePage(ProfileStrip, x, StripAt(_profileShown), carry, () =>
        {
            if (turn != _pageTurn) return;
            SettleProfilePages();
            FlushSwipeLog();
        });
    }

    // ───────────────────────── Create a Profile ─────────────────────────

    // The icon chosen in Create a Profile, till it's made.
    private string? _newProfileIcon;
    // What Create a Profile covers while it's up, as it was.
    private readonly List<UIElement> _hiddenForCreation = new();

    internal void NewProfile_Click(object sender, RoutedEventArgs e) => DispatcherQueue.TryEnqueue(ShowProfileCreation);

    private void ShowProfileCreation()
    {
        if (_private || ProfileCreation.Visibility == Visibility.Visible) return;
        FinishPaging();
        if (_libraryOpen) MoveLibrary(false);
        _newProfileIcon = null;
        ShowNewProfileIcon();
        ProfileCreation.NameBox.Text = "";
        ProfileCreation.CreateButton.IsEnabled = false;
        // Everything below the address bar steps aside for it.
        _hiddenForCreation.Clear();
        foreach (var element in new UIElement[] { ProfileViewport, MediaCard, LibraryButton, ProfileSwitcher, PlusButton })
        {
            if (element.Visibility != Visibility.Visible) continue;
            element.Visibility = Visibility.Collapsed;
            _hiddenForCreation.Add(element);
        }
        ProfileCreation.Visibility = Visibility.Visible;
        ProfileCreation.NameBox.Focus(FocusState.Programmatic);
    }

    private void HideProfileCreation()
    {
        ProfileCreation.Visibility = Visibility.Collapsed;
        foreach (var element in _hiddenForCreation) element.Visibility = Visibility.Visible;
        _hiddenForCreation.Clear();
    }

    private void ShowNewProfileIcon()
    {
        var icon = _newProfileIcon;
        var emoji = icon is not null && !CustomIcon.IsSymbol(icon);
        ProfileCreation.IconGlyph.Glyph = icon is not null && !emoji ? icon : "";
        ProfileCreation.IconGlyph.FontFamily = Ui.IconFont(ProfileCreation.IconGlyph.Glyph);
        ProfileCreation.IconGlyph.Visibility = Ui.Shown(!emoji);
        ProfileCreation.IconEmoji.Text = emoji ? icon! : "";
        ProfileCreation.IconEmoji.Visibility = Ui.Shown(emoji);
    }

    internal void ProfileCreationIcon_Click(object sender, RoutedEventArgs e) =>
        ShowIconPicker(ProfileCreation.IconButton, _newProfileIcon, "Remove icon", icon =>
        {
            _newProfileIcon = icon;
            ShowNewProfileIcon();
        });

    /// <summary>A name, and not another Profile's (as Settings › Profiles asks it).</summary>
    internal void ProfileCreationName_TextChanged(object sender, TextChangedEventArgs e) =>
        ProfileCreation.CreateButton.IsEnabled = ProfileIds.CleanName(ProfileCreation.NameBox.Text) is { } name
            && !ProfileIds.NameTaken(name, Profiles.Select(s => s.Name));

    internal void ProfileCreationName_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ProfileCreation.CreateButton.IsEnabled)
        {
            e.Handled = true;
            CreateProfile();
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            HideProfileCreation();
        }
    }

    internal void ProfileCreationCreate_Click(object sender, RoutedEventArgs e) => CreateProfile();

    internal void ProfileCreationCancel_Click(object sender, RoutedEventArgs e) => HideProfileCreation();

    /// <summary>Create Profile: next to the one you're on, its data fresh, and you go to it.</summary>
    private void CreateProfile()
    {
        if (!ProfileCreation.CreateButton.IsEnabled || ProfileIds.CleanName(ProfileCreation.NameBox.Text) is not { } name) return;
        var profile = new Profile { Name = name, Icon = _newProfileIcon, Engine = NewEngineProfile() };
        AddProfile(profile, ProfileOrder.NewIndex(Profiles.IndexOf(_profileShown), Profiles.Count));
        HideProfileCreation();
        ShowProfile(profile);
        SaveSessionSoon();
    }
}
