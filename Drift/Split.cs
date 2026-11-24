using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Drift.Models;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace Drift;

/// <summary>
/// Something dragged sideways to resize: the split view's dividers, the sidebar's edge. A Grid with the
/// left-right resize pointer over it (the cursor's only settable from inside the element).
/// </summary>
public sealed partial class ResizeGrip : Grid
{
    public ResizeGrip() => ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
}

/// <summary>
/// A page's handle (SplitHandles.cs): while it's being dragged to move the page, the move pointer,
/// arrowheads on all four points (the cursor's only settable from inside the element).
/// </summary>
public sealed partial class PageHandleButton : Button
{
    public void ShowMoving(bool moving) =>
        ProtectedCursor = moving ? InputSystemCursor.Create(InputSystemCursorShape.SizeAll) : null;
}

/// <summary>
/// Split view, like Arc's and Zen's: two to four tabs side by side (SplitShares.MaxPages), each its own
/// card, a divider between each two to share their width differently (double-click for half and half).
/// In the sidebar it's a row of its own in the tab list with all its pages, the one you're on carded;
/// click one to go to it, or into a page. Start one by dragging a tab onto a half of the page, or a
/// tab's "Split with current tab"; while a split's showing, a tab dragged onto the page's left or right
/// half joins it at that end (as in Zen), and "Add split view" puts a new page to the right of one.
/// Any tabs, and a tab is in one place in the sidebar, never two (as in Arc): a regular tab is only in
/// the split's row (TabSplit.Row) while it's split; with a pinned tab in it, the row is in the pinned
/// list where that tab was, and the tab comes back there when it leaves; an Essential or folder tab
/// keeps its own place, and the row is in the tab list. Each page's view is in its card's host
/// (PagePane). Moving a tab elsewhere, or closing it, takes it out first; the others stay together if
/// two or more are left.
/// </summary>
public sealed partial class MainWindow
{
    private const double MinPaneWidth = 320;
    private const double SplitGap = 8;

    /// <summary>
    /// The narrowest a divider can make a page: 320, or with more pages than leave room for that, a
    /// share of the width one more page would have (so each can still give up some of its width).
    /// </summary>
    private double MinPane(int pages) =>
        System.Math.Min(MinPaneWidth, (PageColumns.ActualWidth - SplitGap * (pages - 1)) / (pages + 1));

    /// <summary>
    /// One of the four places for a split's pages on screen (MainWindow.xaml), left to right: its card,
    /// the face its view sits in, its column, the stronger border of the page you're on, and its handle.
    /// The first is MainCard, where a page on its own is too.
    /// </summary>
    private sealed record PagePane(PageCardView Card, Panel Host, ColumnDefinition Column, PageMarkerView Marker)
    {
        public Border Face => Card.Face;
        public Border Ring => Marker.Ring;
        public PageHandleButton Handle => Marker.Button;
        public Border Tab => Marker.Tab;
        public UIElement Dots => Marker.Dots;
    }

    /// <summary>The gap before a pane (from the second on), with its divider.</summary>
    private sealed record PageGap(SplitDividerView Divider, ColumnDefinition Column)
    {
        public UIElement Grip => Divider.Grip;
        public UIElement Accent => Divider.Accent;
    }

    private PagePane[] _panes = [];
    private PageGap[] _gaps = [];

    private void SetUpPanes()
    {
        _panes =
        [
            new(MainCard, WebHost, LeftPaneColumn, LeftPageMarker),
            new(SplitCard, SplitHost, RightPaneColumn, RightPageMarker),
            new(SplitCard3, SplitHost3, Pane3Column, Page3Marker),
            new(SplitCard4, SplitHost4, Pane4Column, Page4Marker),
        ];
        _gaps =
        [
            new(SplitDivider, SplitGapColumn),
            new(SplitDivider2, Gap3Column),
            new(SplitDivider3, Gap4Column),
        ];
    }

    // The tabs whose pages are showing: the active tab, or all of its split's.
    private readonly List<BrowserTab> _shownPages = new();
    private TabSplit? _shownSplit;
    // The divider being dragged, if one is.
    private int? _dragDivider;

    /// <summary>A tab that's in no split (and isn't a split's row).</summary>
    private static bool Free(BrowserTab tab) => !tab.IsSplitRow && tab.Split is null;

    private static bool CanSplit(BrowserTab a, BrowserTab b) => a != b && Free(a) && Free(b);

    /// <summary>A tab can join a split with room for another page.</summary>
    private static bool CanJoin(TabSplit split, BrowserTab tab) => !split.IsFull && Free(tab);

    /// <summary>A tab can go beside a page: into the page's split, or into a new one with it.</summary>
    private static bool CanGoBeside(BrowserTab page, BrowserTab tab) =>
        page.Split is { } split ? CanJoin(split, tab) : CanSplit(page, tab);

    /// <summary>A tab's row in the tab list, if it has one there.</summary>
    private int? TabListIndex(BrowserTab tab) =>
        tab.Place == TabPlace.Regular && Tabs.IndexOf(tab) is >= 0 and var at ? at : null;

    /// <summary>A tab's row in the pinned list, if it has one there.</summary>
    private int? PinnedListIndex(BrowserTab tab) =>
        tab.Place == TabPlace.Pinned && PinnedTabs.IndexOf(tab) is >= 0 and var at ? at : null;

    /// <summary>The page of a split you were last on (the carded one), else its first.</summary>
    private static BrowserTab FrontOf(TabSplit split) =>
        split.Pages.Where(p => p.IsFront).ToList() is [var only] ? only : split.Pages[0];

    private void SplitTabs(BrowserTab left, BrowserTab right, BrowserTab? front = null) => SplitTabs([left, right], front: front);

    /// <summary>
    /// Puts tabs side by side, left to right. With a pinned tab in it, the split's row takes that tab's
    /// place in the pinned list (the highest one's). Otherwise it goes where the highest of their rows in
    /// the tab list was, or at <paramref name="row"/> (a saved split), or at the end when none has one there.
    /// </summary>
    private void SplitTabs(IReadOnlyList<BrowserTab> pages, IReadOnlyList<double>? shares = null, BrowserTab? front = null, int? row = null)
    {
        if (pages.Count is < 2 or > SplitShares.MaxPages || pages.Distinct().Count() != pages.Count || !pages.All(Free)) return;
        var pinned = pages.Where(p => p.Place == TabPlace.Pinned).ToList();
        var host = pinned.Count > 0 ? PinnedTabs : Tabs;
        var at = pinned.Count > 0
            ? pages.Select(PinnedListIndex).Min() ?? PinnedTabs.Count
            : row ?? pages.Select(TabListIndex).Min() ?? Tabs.Count;
        var split = new TabSplit(pages, shares);
        // The row: a stand-in tab with no page of its own (its view is never started or shown).
        var standIn = BrowserTab.SplitRow(split);
        split.Row = standIn;
        _syncingSelection = true;
        foreach (var page in pages) Tabs.Remove(page);
        foreach (var page in pinned) PinnedTabs.Remove(page);
        host.Insert(System.Math.Clamp(at, 0, host.Count), standIn);
        _syncingSelection = false;

        foreach (var page in pages)
        {
            page.Split = split;
            page.PropertyChanged += SplitPage_PropertyChanged;
        }
        NameSplitRow(split);
        HostPages(split);
        SaveSessionSoon();
        if (front is not null) Activate(front);
        else if (_active is { } active && pages.Contains(active)) Activate(active);
    }

    /// <summary>A tab joins a split at <paramref name="at"/> (0 for the left end), taking an even share of the width.</summary>
    private void JoinSplit(TabSplit split, BrowserTab tab, int at, BrowserTab? front = null)
    {
        if (!CanJoin(split, tab) || split.Row is not { } row) return;
        _syncingSelection = true;
        if (tab.Place == TabPlace.Pinned && Tabs.Contains(row))
        {
            // A pinned tab joining: the row moves to its place in the pinned list.
            var place = PinnedTabs.IndexOf(tab);
            Tabs.Remove(row);
            PinnedTabs.Remove(tab);
            PinnedTabs.Insert(System.Math.Clamp(place, 0, PinnedTabs.Count), row);
        }
        else
        {
            Tabs.Remove(tab);
            PinnedTabs.Remove(tab);
        }
        _syncingSelection = false;
        split.Insert(at, tab);
        tab.Split = split;
        tab.PropertyChanged += SplitPage_PropertyChanged;
        NameSplitRow(split);
        HostPages(split);
        SaveSessionSoon();
        if (front is not null) Activate(front);
        else if (_shownSplit == split && _active is { } active) Activate(active);
    }

    /// <summary>
    /// A tab goes beside a page: to its right in the page's split, or in a new split with it, then
    /// settles into its place. <paramref name="front"/> is the one you're on after (else the one you're on stays so).
    /// </summary>
    private void PutBeside(BrowserTab page, BrowserTab tab, BrowserTab? front = null, bool animate = true)
    {
        if (!CanGoBeside(page, tab)) return;
        if (page.Split is { } split) JoinSplit(split, tab, split.IndexOf(page) + 1, front);
        else SplitTabs(page, tab, front);
        if (animate) AnimateSplitIn(tab);
    }

    /// <summary>Each page's view in its card, left to right.</summary>
    private void HostPages(TabSplit split)
    {
        for (var i = 0; i < split.Count; i++) MoveView(split.Pages[i].View, _panes[i].Host);
    }

    /// <summary>The row's name, for screen readers: its pages' titles.</summary>
    private static void NameSplitRow(TabSplit split)
    {
        if (split.Row is { } row) row.Title = string.Join(" | ", split.Pages.Select(p => p.Title));
    }

    private void SplitPage_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BrowserTab.Title) && sender is BrowserTab { Split: { } split }) NameSplitRow(split);
    }

    /// <summary>
    /// Tabs of their own again, left first, where the split's row was: in the tab list, its regular tabs;
    /// in the pinned list, its pinned tabs, and regular ones at the end of the tab list. Essentials and
    /// folder tabs were in their places all along.
    /// </summary>
    private void SeparateTabs(TabSplit split)
    {
        var pages = split.Pages.ToList();
        foreach (var page in pages)
        {
            page.Split = null;
            page.IsFront = true;
            page.PropertyChanged -= SplitPage_PropertyChanged;
            MoveView(page.View, WebHost);
        }
        _syncingSelection = true;
        var host = split.Row is { } kept && PinnedTabs.Contains(kept) ? PinnedTabs : Tabs;
        var at = split.Row is { } row && host.IndexOf(row) is >= 0 and var index ? index : host.Count;
        if (split.Row is { } standIn) host.Remove(standIn);
        split.Row = null;
        foreach (var page in pages)
        {
            if (page.Place == TabPlace.Pinned) PinnedTabs.Insert(System.Math.Min(at++, PinnedTabs.Count), page);
            else if (page.Place == TabPlace.Regular && host == PinnedTabs) Tabs.Add(page);
            else if (page.Place == TabPlace.Regular) Tabs.Insert(System.Math.Min(at++, Tabs.Count), page);
        }
        _syncingSelection = false;
        SaveSessionSoon();
        if (_shownSplit == split)
        {
            if (_active is { } active && pages.Contains(active)) Activate(active);
            else
            {
                _shownSplit = null;
                LayoutPages();
            }
        }
    }

    /// <summary>
    /// One page leaves its split, back to a row of its own just below the split's (a pinned tab, in the
    /// pinned list); the others stay together, or with only one left, they part. Showing, you're on
    /// <paramref name="next"/> after, else the page you were on (on its own, if it was this one).
    /// </summary>
    private void TakeOutOfSplit(BrowserTab tab, BrowserTab? next = null)
    {
        if (tab.Split is not { } split) return;
        if (split.Count <= 2)
        {
            SeparateTabs(split);
            if (next is not null) Activate(next);
            return;
        }
        var shown = _shownSplit == split;
        var row = split.Row;
        var host = row is not null && PinnedTabs.Contains(row) ? PinnedTabs : Tabs;
        var at = row is not null && host.IndexOf(row) is >= 0 and var index ? index + 1 : host.Count;
        split.Remove(tab);
        tab.Split = null;
        tab.IsFront = true;
        tab.PropertyChanged -= SplitPage_PropertyChanged;
        MoveView(tab.View, WebHost);
        HostPages(split);
        _syncingSelection = true;
        if (tab.Place == TabPlace.Pinned) PinnedTabs.Insert(host == PinnedTabs ? System.Math.Min(at, PinnedTabs.Count) : PinnedTabs.Count, tab);
        else if (tab.Place == TabPlace.Regular && host == Tabs) Tabs.Insert(System.Math.Min(at, Tabs.Count), tab);
        else if (tab.Place == TabPlace.Regular) Tabs.Add(tab);
        // No pinned tab left in it: its row goes to the tab list.
        if (row is not null && host == PinnedTabs && split.Pages.All(p => p.Place != TabPlace.Pinned))
        {
            PinnedTabs.Remove(row);
            Tabs.Add(row);
        }
        _syncingSelection = false;
        NameSplitRow(split);
        SaveSessionSoon();
        if (next is not null) Activate(next);
        else if (shown && _active is { } active) Activate(active);
    }

    /// <summary>A tab about to move or close leaves its split first.</summary>
    private void LeaveSplit(BrowserTab tab) => TakeOutOfSplit(tab);

    /// <summary>Where a tab's page lives: its card's host in a split, the page area otherwise.</summary>
    private Panel HostFor(BrowserTab tab) =>
        tab.Split?.IndexOf(tab) is > 0 and var at ? _panes[at].Host : WebHost;

    private static void MoveView(WebView2 view, Panel host)
    {
        if (view.Parent == host) return;
        (view.Parent as Panel)?.Children.Remove(view);
        host.Children.Add(view);
    }

    /// <summary>Shows the tab's page, beside the rest of its split if it's in one; every other page is hidden.</summary>
    private void ShowPages(BrowserTab tab)
    {
        var split = tab.Split;
        BrowserTab[] showing = split is null ? [tab] : split.Pages.ToArray();
        foreach (var shown in _shownPages.Where(t => !showing.Contains(t)))
            shown.View.Visibility = Visibility.Collapsed;
        _shownPages.Clear();
        _shownPages.AddRange(showing);
        foreach (var page in showing) page.View.Visibility = Visibility.Visible;
        if (split is not null)
            foreach (var page in split.Pages) page.IsFront = page == tab;
        _shownSplit = split;
        HidePageControls();
        LayoutPages();
        PlaceSplitCards();
    }

    /// <summary>Each page its share of the width (one page: all of it); unused cards and dividers out of the way.</summary>
    private void LayoutPages()
    {
        var count = _shownSplit?.Count ?? 1;
        var shares = _shownSplit?.Shares ?? [1.0];
        for (var i = 0; i < _panes.Length; i++)
        {
            var used = i < count;
            _panes[i].Column.Width = used ? new GridLength(System.Math.Max(0.001, shares[i]), GridUnitType.Star) : new GridLength(0);
            if (i > 0) _panes[i].Card.Visibility = used ? Visibility.Visible : Visibility.Collapsed;
        }
        for (var g = 0; g < _gaps.Length; g++)
        {
            var used = g < count - 1;
            _gaps[g].Column.Width = new GridLength(used ? SplitGap : 0);
            _gaps[g].Divider.Visibility = used ? Visibility.Visible : Visibility.Collapsed;
        }
        ShowPageHandles();
    }

    /// <summary>Where the page you're on starts, if it's in the shown split and not first: for it to slide back from there.</summary>
    private double? ShownPageStart(TabSplit split) =>
        split == _shownSplit && _active is { } active && split.IndexOf(active) is > 0 and var at
            ? _panes[at].Card.ActualOffset.X : null;

    /// <summary>A click into one of the split's pages makes it the one you're on (address bar, shortcuts).</summary>
    private void WireSplitFocus(BrowserTab tab, WebView2 view) =>
        view.GotFocus += (_, _) =>
        {
            if (tab.View == view && tab != _active && tab.Split is { } split && split == _shownSplit) Activate(tab);
        };

    // ── The sidebar ──

    /// <summary>Marks the pill on a split row's page in front (PlaceSplitCard).</summary>
    private sealed class FrontHalf;

    /// <summary>Marks the hover on a split row's page (ShowHalfHover).</summary>
    private sealed class HoverHalf;

    /// <summary>
    /// A split row's pill of one kind, made the first time: the selected card on the page you're on, or
    /// the hover on another; a couple of pixels in from its place, on the row's card, under the pages.
    /// </summary>
    private static Border HalfPill(Grid row, System.Type kind)
    {
        if (row.Children.OfType<Border>().FirstOrDefault(b => b.Tag?.GetType() == kind) is { } made) return made;
        var front = kind == typeof(FrontHalf);
        var pill = new Border
        {
            Tag = front ? new FrontHalf() : new HoverHalf(),
            IsHitTestVisible = false,
            Opacity = 0,
            OpacityTransition = new ScalarTransition { Duration = System.TimeSpan.FromMilliseconds(front ? 150 : 100) },
        };
        if (front) pill.Style = (Style)Application.Current.Resources["SplitFrontCardStyle"];
        else
        {
            pill.CornerRadius = new CornerRadius(8);
            pill.Background = (Brush)Application.Current.Resources["DriftHoverBrush"];
        }
        Grid.SetRowSpan(pill, 4);
        Grid.SetColumnSpan(pill, 7);
        // Just above the row's card (its first child), under the pages.
        row.Children.Insert(1, pill);
        return pill;
    }

    private static bool SideBySide => Settings.Current.SplitPagesSideBySide;

    /// <summary>
    /// A split row's pages in their places (each a tab row, 38 tall): one under another, or side by side in the
    /// row's columns, each filling its own (Settings › Tabs). Setting what's already set changes nothing, so it's
    /// done every time the row's card is placed.
    /// </summary>
    private static void LayOutSplitRow(Grid row)
    {
        var side = SideBySide;
        var i = 0;
        foreach (var half in row.Children.OfType<SplitPageView>())
        {
            Grid.SetRow(half, side ? 0 : i);
            Grid.SetColumn(half, side ? i * 2 : 0);
            Grid.SetColumnSpan(half, side ? 1 : 7);
            FitHalf(half);
            i++;
        }
    }

    /// <summary>
    /// A pill (the selected card, or the hover) on a page's place, a few pixels in from its edges: along the
    /// row's length when the pages are one under another, across its width when they're side by side.
    /// </summary>
    private static void PlacePill(Border pill, SplitPageView half)
    {
        if (SideBySide)
        {
            pill.Margin = new Thickness(0, 3, 0, 3);
            pill.HorizontalAlignment = HorizontalAlignment.Left;
            pill.VerticalAlignment = VerticalAlignment.Stretch;
            pill.Height = double.NaN;
            pill.Width = System.Math.Max(0, half.ActualWidth - 4);
            Spring.Jump(pill, "Translation", new Vector3((float)half.ActualOffset.X + 2, 0, 0));
        }
        else
        {
            pill.Margin = new Thickness(3, 0, 3, 0);
            pill.HorizontalAlignment = HorizontalAlignment.Stretch;
            pill.VerticalAlignment = VerticalAlignment.Top;
            pill.Width = double.NaN;
            pill.Height = System.Math.Max(0, half.ActualHeight - 6);
            Spring.Jump(pill, "Translation", new Vector3(0, (float)half.ActualOffset.Y + 3, 0));
        }
    }

    /// <summary>A page's place in a split's row: that page.</summary>
    internal void SplitHalf_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not BrowserTab tab) return;
        // After the row's own selection, which goes to the split's page in front.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (tab.Split is null) return;
            Activate(tab);
            tab.View.Focus(FocusState.Programmatic);
        });
    }

    /// <summary>
    /// A page's place, the first time it's shown: it shrinks a little while pressed, by itself, and
    /// keeps its title only while there's room for some of it.
    /// </summary>
    internal void SplitHalf_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not SplitPageView half || _pressFeedback.Contains(half)) return;
        AddPressFeedback(half, 0.97f);
        half.SizeChanged += (_, _) =>
        {
            FitHalf(half);
            // A page added or taken out: the places are laid out again after the card was put on the page
            // in front (a new third page had no width yet, so the card stayed where the first one was).
            if (half.Parent is Grid row && row.Children.OfType<Border>().FirstOrDefault() is { } card) PlaceSplitCard(card);
        };
        FitHalf(half);
    }

    // A page's place narrower than this shows its icon alone, in the middle (four pages in a narrow sidebar).
    private const double HalfTitleWidth = 64;

    private static void FitHalf(SplitPageView half)
    {
        if (half.ActualWidth <= 0) return;
        var narrow = half.ActualWidth < HalfTitleWidth;
        half.TitleText.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumnSpan(half.Icon, narrow ? 2 : 1);
        half.Icon.HorizontalAlignment = narrow ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        half.Root.Padding = narrow ? new Thickness(0) : new Thickness(11, 0, 8, 0);
        // One under another, 16: a tab row's icon-to-title gap (its 10, and 6 beside its speaker's empty column), so
        // titles line up with the tabs'. Side by side there's nothing to line up with, and less room: 8.
        half.Root.ColumnSpacing = narrow ? 0 : SideBySide ? 8 : 16;
    }

    /// <summary>
    /// Hovered, a page's place lights up as a tab in a folder does (not the one with the selected card on
    /// it), and each one's × shows at its end, as Arc's do (before its Peek badge, if it has one), the
    /// title stopping short of it. Not on one narrower than Zen allows one.
    /// </summary>
    internal void SplitHalf_PointerEntered(object sender, PointerRoutedEventArgs e) => ShowHalfHover(sender, true);
    internal void SplitHalf_PointerExited(object sender, PointerRoutedEventArgs e) => ShowHalfHover(sender, false);

    // A page's place narrower than this has no × (Zen's limit).
    private const double HalfCloseWidth = 70;

    private void ShowHalfHover(object sender, bool over)
    {
        if (sender is not SplitPageView { Tag: BrowserTab page, Parent: Grid row } hovered) return;
        var carded = page.Split is { } split && split == _shownSplit && FrontOf(split) == page;
        // The hover's the size of the selected card on a page (a couple of pixels in), not its whole place.
        var fill = HalfPill(row, typeof(HoverHalf));
        if (over && !carded && hovered.ActualHeight > 0 && hovered.ActualWidth > 0)
        {
            PlacePill(fill, hovered);
            fill.Opacity = 1;
        }
        else if (!over) fill.Opacity = 0;
        foreach (var half in row.Children.OfType<SplitPageView>().Where(h => h.Tag is BrowserTab))
        {
            var (close, title) = (half.CloseButton, half.TitleText);
            // As in Zen: on any page wide enough, even if the title has to give up all its room to it.
            var shown = over && half.ActualWidth >= HalfCloseWidth;
            close.Opacity = shown ? 1 : 0;
            close.IsHitTestVisible = shown;
            title.Padding = shown ? new Thickness(0, 0, close.Width + 4, 0) : default;
        }
    }

    // ── The card on the page you're on ──

    internal void SplitCard_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Border card) PlaceSplitCard(card);
    }

    internal void SplitRow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Grid row && row.Children.OfType<Border>().FirstOrDefault() is { } card) PlaceSplitCard(card);
    }

    /// <summary>Every split row's card, after the page you're on changed: across to it, or away.</summary>
    private void PlaceSplitCards()
    {
        foreach (var row in Tabs.Where(t => t.IsSplitRow))
            if (TabList.ContainerFromItem(row) is ListViewItem item && FindSplitCard(item) is { } card)
                PlaceSplitCard(card);
        foreach (var row in PinnedTabs.Where(t => t.IsSplitRow))
            if (PinnedList.ContainerFromItem(row) is ListViewItem item && FindSplitCard(item) is { } card)
                PlaceSplitCard(card);
    }

    private static Border? FindSplitCard(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Border { Tag: BrowserTab { IsSplitRow: true } } card) return card;
            if (FindSplitCard(child) is { } found) return found;
        }
        return null;
    }

    /// <summary>
    /// A split's pages are on a card of their own, the folders' (their colour and border) (DESIGN.md › Surfaces).
    /// Always, one under another or side by side, so they read as one tab, in front or not (the user: "the
    /// horizontal split view tab doesn't have the folder background"). While the split's the tab you're on, the page
    /// you're on has a selected tab's card on it, a few pixels in, there at once when you switch pages; it fades
    /// away when you leave the split. No line between the pages.
    /// </summary>
    private void PlaceSplitCard(Border card)
    {
        if (card.Tag is not BrowserTab { Split: { } split } || card.Parent is not Grid row) return;
        LayOutSplitRow(row);
        if (row.ActualWidth <= 0) return;
        var shown = split == _shownSplit;
        card.Width = row.ActualWidth;
        Spring.Jump(card, "Translation", Vector3.Zero);
        card.Opacity = 1;
        if (row.Children.OfType<SplitPageView>().FirstOrDefault(h => h.Tag == FrontOf(split)) is { ActualHeight: > 0, ActualWidth: > 0 } half)
        {
            var pill = HalfPill(row, typeof(FrontHalf));
            PlacePill(pill, half);
            pill.Opacity = shown ? 1 : 0;
        }
        // The selected card's arrived (you've just picked a page): its hover goes.
        if (shown && row.Children.OfType<Border>().FirstOrDefault(b => b.Tag is HoverHalf) is { } hover) hover.Opacity = 0;
    }

    /// <summary>
    /// A page's ×: it leaves the split. A regular tab closes; an Essential, pinned or folder tab stays
    /// where it is in the sidebar. The others stay.
    /// </summary>
    internal void CloseSplitHalf_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not SplitPageView { Tag: BrowserTab tab } half) return;
        CloseSplitPage(tab);
        // Its Peek gone and the pointer still on it: the title takes back the badge's room.
        if (tab.Split is not null)
            DispatcherQueue.TryEnqueue(() => ShowHalfHover(half, true));
    }

    /// <summary>A page of a split closes (from its ×, or its handle's buttons); the others stay.</summary>
    private void CloseSplitPage(BrowserTab tab)
    {
        if (tab.Split is not { } split) return;
        // Its Peek goes first, as on a tab's own row.
        if (ClosePeekOf(tab)) return;
        if (tab.IsPinned) TakeOutOfSplit(tab, next: _active == tab ? split.Beside(tab) : null);
        else CloseTab(tab);
    }

    /// <summary>The row's Close tabs: its regular tabs close; Essentials, pinned and folder tabs just leave it.</summary>
    private void CloseSplit(TabSplit split, bool remember = true)
    {
        var pages = split.Pages.ToList();
        SeparateTabs(split);
        // A page that stays (an Essential, pinned or folder tab) is the one you're on after.
        if (pages.FirstOrDefault(p => p.IsPinned) is { } staying && _active is { } active && pages.Contains(active))
            Activate(staying);
        foreach (var page in pages.Where(p => !p.IsPinned)) RemoveTab(page, remember);
    }

    /// <summary>The split comes apart; showing, the page you're on slides back over to the left as it widens.</summary>
    private void SeparateAnimated(TabSplit split)
    {
        var start = ShownPageStart(split);
        SeparateTabs(split);
        if (start is { } at) AnimateSeparate(at);
    }

    // ── The dividers ──

    private int? _overDivider;

    private int DividerOf(object sender) => System.Array.FindIndex(_gaps, g => ReferenceEquals(g.Divider, sender));

    private void SplitDivider_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _overDivider = DividerOf(sender);
        ShowGrip(DividerOf(sender));
    }

    private void SplitDivider_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        var divider = DividerOf(sender);
        if (_overDivider == divider) _overDivider = null;
        ShowGrip(divider);
    }

    // Out of sight until the pointer's on the gap (where the pointer turns to the resize arrows).
    private void ShowGrip(int divider)
    {
        if (divider < 0) return;
        ShowGrip(_gaps[divider].Grip, _gaps[divider].Accent, _overDivider == divider, _dragDivider == divider, atRest: 0);
    }

    /// <summary>
    /// A resize grip (a split divider's, the sidebar edge's): at rest faint or out of sight, brighter
    /// under the pointer, accent-coloured and a little taller while held.
    /// </summary>
    private static void ShowGrip(UIElement grip, UIElement accent, bool over, bool held, double atRest)
    {
        grip.Opacity = held ? 0 : over ? 0.6 : atRest;
        accent.Opacity = held ? 1 : 0;
        var grow = held ? new Vector3(1, 1.25f, 1) : Vector3.One;
        foreach (var part in new[] { grip, accent })
        {
            part.CenterPoint = new Vector3(2, 16, 0);
            Spring.To(part, "Scale", grow, 0.3, held ? 0.2 : 0.3);
        }
    }

    private void SplitDivider_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var divider = DividerOf(sender);
        if (_shownSplit is null || divider < 0) return;
        // Grabbing it stops it springing back.
        StopEvenSpring();
        if (_gaps[divider].Divider.CapturePointer(e.Pointer)) _dragDivider = divider;
        ShowGrip(divider);
        e.Handled = true;
    }

    private void SplitDivider_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragDivider is not { } divider || divider != DividerOf(sender) || _shownSplit is not { } split || divider >= split.Count - 1) return;
        var x = e.GetCurrentPoint(PageColumns).Position.X;
        // Past a page's minimum it gives a little, with resistance; it springs back on letting go.
        split.Shares = SplitShares.Resized(split.Shares, divider, x, PageColumns.ActualWidth, SplitGap, MinPane(split.Count), stretch: true);
        LayoutPages();
        e.Handled = true;
    }

    private void SplitDivider_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragDivider is not { } divider || divider != DividerOf(sender)) return;
        _dragDivider = null;
        _gaps[divider].Divider.ReleasePointerCapture(e.Pointer);
        ShowGrip(divider);
        SpringBackInside();
        SaveSessionSoon();
        UpdateDragRegions();
        e.Handled = true;
    }

    private void SplitDivider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_dragDivider is not { } divider || divider != DividerOf(sender)) return;
        _dragDivider = null;
        ShowGrip(divider);
        SpringBackInside();
    }

    /// <summary>Let go stretched past a page's minimum: back to it, on the same spring as double-clicking.</summary>
    private void SpringBackInside()
    {
        if (_shownSplit is not { } split) return;
        SpringToShares(split, SplitShares.Inside(split.Shares, PageColumns.ActualWidth, SplitGap, MinPane(split.Count)));
    }

    /// <summary>The two pages beside it, half and half again.</summary>
    private void SplitDivider_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var divider = DividerOf(sender);
        if (_shownSplit is not { } split || divider < 0 || divider >= split.Count - 1) return;
        SpringToShares(split, SplitShares.Evened(split.Shares, divider));
        SaveSessionSoon();
    }

    // ── Dragging a tab onto the page ──
    // As in Arc: over a half of the page, the page there makes room. It takes half the width at once
    // (a page can't follow a width that changes every frame) and springs over to the other half,
    // leaving the half under the pointer empty for the tab; crossing to the other half, it springs
    // back. The empty half is outlined. Under the pointer the tab is a card of its page (its icon and title). Let go, and the new
    // page settles into the empty half; drag away, and the page takes the whole width again.
    // Dragging the tab you're on: the tab you were on before takes its place, and makes room for it.
    // Over a split with room for another page, as in Zen: over its left half, the pages make room at
    // the left end; over its right half, at the right end.

    private const double SplitSpringSeconds = 0.3;
    private const double SplitSpringBounce = 0.15;

    // The half the page has made room on, while a tab is dragged over it.
    private SplitSide? _dropSide;

    /// <summary>
    /// The tab a dragged tab would go beside: a page in a split with room for it, or the tab it would be
    /// split with (SplitLayout.Partner); none otherwise.
    /// </summary>
    private BrowserTab? SplitPartner(BrowserTab dragged)
    {
        if (_active is { Split: { } split } front && dragged != front) return CanJoin(split, dragged) ? front : null;
        var tabs = AllTabs.ToHashSet();
        return SplitLayout.Partner(dragged, _active, _recentTabs.Where(tabs.Contains), CanSplit);
    }

    /// <summary>While a tab is dragged: if it has a tab to go beside (SplitPartner), the page takes the drop.</summary>
    private void ShowSplitDrop(BrowserTab dragged)
    {
        _dropSide = null;
        _pairShown = false;
        var partner = SplitPartner(dragged);
        _pairPartner = dragged == _active ? partner : null;
        SplitDropLayer.Visibility = (partner is not null || CanSplitWithNew(dragged)) && !IsPeekShown
            ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// The tab you're on with no other to go beside: it makes room all the same (as in Zen), and dropped,
    /// the command bar opens for the page to go in the empty half.
    /// </summary>
    private bool CanSplitWithNew(BrowserTab dragged) => dragged == _active && dragged is { IsSplitRow: false, Split: null };

    // Room left for a page beside, while the command bar's open for it; and whether a page is on its way.
    private bool _dropRoomHeld;
    private bool _splitComing;
    private (BrowserTab With, SplitSide Side)? _splitBarAfterDrag;

    /// <summary>The page takes the whole width again, if room was left for a page beside it.</summary>
    private void ReleaseDropRoom()
    {
        if (!_dropRoomHeld) return;
        _dropRoomHeld = false;
        MakeRoom(null);
    }

    /// <summary>A page is going in the room left: the split takes over from where the page is, without a jump.</summary>
    private void TakeDropRoom()
    {
        if (!_dropRoomHeld) return;
        _dropRoomHeld = false;
        _dropSide = null;
        SplitDropSlot.Opacity = 0;
        MainCard.Margin = new Thickness(0);
        Spring.Jump(MainCard, "Translation", Vector3.Zero);
    }

    /// <summary>After the drag's done (it takes the focus back to the page): the command bar, if it was dropped for one.</summary>
    private void OpenSplitBarAfterDrag()
    {
        if (_splitBarAfterDrag is not { } pending) return;
        _splitBarAfterDrag = null;
        if (_active == pending.With) OpenSplitCommandBar(pending.With, pending.Side);
        else ReleaseDropRoom();
    }

    /// <summary>The drag's over: the page takes the whole width again, if it had made room.</summary>
    private void HideSplitDrop()
    {
        SplitDropLayer.Visibility = Visibility.Collapsed;
        if (!_dropRoomHeld) MakeRoom(null);
        ShowPairPartner(false);
        _pairPartner = null;
    }

    // While the tab you're on is dragged: the tab it'll go beside, and whether that's in front meanwhile.
    private BrowserTab? _pairPartner;
    private bool _pairShown;

    /// <summary>
    /// The tab you're on, dragged over the page: the tab you were on before takes its place in front,
    /// to make room for it as any page does (MakeRoom). Off the page again, the tab you're on is back.
    /// </summary>
    private void ShowPairPartner(bool shown)
    {
        if (shown == _pairShown || _active is not { } page || _pairPartner is not { } partner) return;
        _pairShown = shown;
        partner.View.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        page.View.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>The page makes room for a tab on <paramref name="side"/>, or (null) takes the whole width again.</summary>
    private void MakeRoom(SplitSide? side)
    {
        if (side == _dropSide) return;
        if (_shownSplit is not null || _roomInSplit)
        {
            MakeRoomInSplit(_shownSplit, side);
            return;
        }
        var from = _dropSide;
        _dropSide = side;
        var width = PageColumns.ActualWidth;
        var to = new Vector3((float)SplitLayout.PreviewOffset(side, width, SplitGap), 0, 0);
        ShowDropSlot(side, crossing: from is not null, width);
        if (from is not null && side is not null)
        {
            // Across to the other half: the spring carries on from wherever it's got to.
            Spring.To(MainCard, "Translation", to, SplitSpringSeconds, SplitSpringBounce);
            return;
        }
        // Its width changes at once, and it slides on from where it was.
        MainCard.Margin = new Thickness(0, 0, width - SplitLayout.PreviewWidth(side, width, SplitGap), 0);
        var was = new Vector3((float)SplitLayout.PreviewOffset(from, width, SplitGap), 0, 0);
        Spring.To(MainCard, "Translation", to, SplitSpringSeconds, SplitSpringBounce, from: was);
    }

    // While the pages of a split make room at one end: how far right they've been moved (all of them, to
    // leave room at the left end), so they can be drawn on from there.
    private bool _roomInSplit;
    private double _roomShift;

    /// <summary>
    /// A split's pages make room for a tab at one end: each takes its width with the new page in (at
    /// once, as a page on its own does) and slides on from where it was; the place left is outlined.
    /// Null: back to their own widths.
    /// </summary>
    private void MakeRoomInSplit(TabSplit? split, SplitSide? side)
    {
        _dropSide = side;
        var count = split?.Count ?? 0;
        // Where each card's drawn now.
        var was = Enumerable.Range(0, count).Select(i => _panes[i].Card.ActualOffset.X + _roomShift).ToArray();
        var width = PageColumns.ActualWidth;
        if (split is null || side is null)
        {
            _roomInSplit = false;
            _roomShift = 0;
            SplitDropSlot.Opacity = 0;
            LayoutPages();
        }
        else
        {
            _roomInSplit = true;
            var at = side == SplitSide.Left ? 0 : count;
            var widths = SplitShares.Widths(SplitShares.Insert(split.Shares, at), width, SplitGap);
            var lefts = SplitShares.Lefts(widths, SplitGap);
            // In pixels, leaving the new page's place (and a gap) empty: at the right end, after the
            // last column; at the left, the pages are all moved over by it.
            for (var i = 0; i < count; i++) _panes[i].Column.Width = new GridLength(widths[i + (at == 0 ? 1 : 0)]);
            // The gap after the last page takes what's left, so the columns still fill the page area
            // (and the drop layer over them all, the whole of it).
            _gaps[count - 1].Column.Width = new GridLength(1, GridUnitType.Star);
            _roomShift = at == 0 ? lefts[1] : 0;
            SplitDropSlot.Width = widths[at];
            var outline = new Vector3((float)lefts[at], 0, 0);
            if (SplitDropSlot.Opacity > 0) Spring.To(SplitDropSlot, "Translation", outline, SplitSpringSeconds, SplitSpringBounce);
            else Spring.Jump(SplitDropSlot, "Translation", outline);
            SplitDropSlot.Opacity = 1;
        }
        PageColumns.UpdateLayout();
        var shift = new Vector3((float)_roomShift, 0, 0);
        for (var i = 0; i < count; i++)
        {
            var from = new Vector3((float)(was[i] - _panes[i].Card.ActualOffset.X), 0, 0);
            foreach (var part in PaneParts(i))
                Spring.To(part, "Translation", shift, SplitSpringSeconds, SplitSpringBounce, from: from);
        }
        foreach (var gap in _gaps) Spring.Jump(gap.Divider, "Translation", shift);
    }

    /// <summary>A page and what goes with it: its card, its border, its handle.</summary>
    private UIElement[] PaneParts(int pane) => [_panes[pane].Card, _panes[pane].Marker];

    /// <summary>
    /// The empty half outlined, as in Arc, so it's plain where the tab will go; crossing to the other
    /// half, the outline goes along with the page. Null: it fades.
    /// </summary>
    private void ShowDropSlot(SplitSide? side, bool crossing, double width)
    {
        if (side is not { } half)
        {
            SplitDropSlot.Opacity = 0;
            return;
        }
        SplitDropSlot.Width = SplitLayout.HalfWidth(width, SplitGap);
        var at = new Vector3((float)SplitLayout.HalfOffset(half, width, SplitGap), 0, 0);
        if (crossing) Spring.To(SplitDropSlot, "Translation", at, SplitSpringSeconds, SplitSpringBounce);
        else Spring.Jump(SplitDropSlot, "Translation", at);
        SplitDropSlot.Opacity = 1;
    }

    private void SplitDrop_DragOver(object sender, DragEventArgs e)
    {
        if (_dragTab is null) return;
        e.AcceptedOperation = DataPackageOperation.Move;
        var ui = e.DragUIOverride;
        ui.IsGlyphVisible = false;
        ui.IsCaptionVisible = false;
        ui.IsContentVisible = true;
        if (_pageGhost is not null) ui.SetContentFromSoftwareBitmap(_pageGhost, _pageGhostAnchor);
        else if (_rowGhost is not null) ui.SetContentFromSoftwareBitmap(_rowGhost, _rowGhostAnchor);
        var side = SplitLayout.Side(e.GetPosition(SplitDropLayer).X, SplitDropLayer.ActualWidth);
        ShowPairPartner(true);
        MakeRoom(side);
    }

    private void SplitDrop_DragLeave(object sender, DragEventArgs e)
    {
        // Only leaving the page counts (not a pointer still over it).
        var at = e.GetPosition(SplitDropLayer);
        if (at.X >= 0 && at.Y >= 0 && at.X < SplitDropLayer.ActualWidth && at.Y < SplitDropLayer.ActualHeight) return;
        MakeRoom(null);
        ShowPairPartner(false);
    }

    private void SplitDrop_Drop(object sender, DragEventArgs e)
    {
        var dragged = _dragTab;
        var width = SplitDropLayer.ActualWidth;
        var side = SplitLayout.Side(e.GetPosition(SplitDropLayer).X, width);
        if (dragged is not null && _shownSplit is { } shown && CanJoin(shown, dragged))
        {
            // Into the split: its pages are already where they'll be, so they take over from there.
            e.AcceptedOperation = DataPackageOperation.Move;
            SplitDropLayer.Visibility = Visibility.Collapsed;
            _dropSide = null;
            _roomInSplit = false;
            _roomShift = 0;
            SplitDropSlot.Opacity = 0;
            for (var i = 0; i < _panes.Length; i++)
                foreach (var part in PaneParts(i)) Spring.Jump(part, "Translation", Vector3.Zero);
            foreach (var gap in _gaps) Spring.Jump(gap.Divider, "Translation", Vector3.Zero);
            JoinSplit(shown, dragged, side == SplitSide.Left ? 0 : shown.Count, front: dragged);
            AnimateSplitIn(dragged);
            return;
        }
        if (dragged is not null && SplitPartner(dragged) is null && CanSplitWithNew(dragged))
        {
            // Nothing to go beside: the room stays, and the command bar opens for a page to go in it.
            e.AcceptedOperation = DataPackageOperation.Move;
            SplitDropLayer.Visibility = Visibility.Collapsed;
            _dropRoomHeld = true;
            _splitBarAfterDrag = (dragged, side);
            return;
        }
        if (dragged is null || SplitPartner(dragged) is not { } partner)
        {
            HideSplitDrop();
            return;
        }
        e.AcceptedOperation = DataPackageOperation.Move;
        SplitDropLayer.Visibility = Visibility.Collapsed;
        // The tab you were on before is in front by now, if it's the tab you're on that's dropped.
        _pairShown = false;
        _pairPartner = null;
        // The page is already where the split puts it: the split takes over from here, without a jump.
        _dropSide = null;
        SplitDropSlot.Opacity = 0;
        MainCard.Margin = new Thickness(0);
        Spring.Jump(MainCard, "Translation", Vector3.Zero);
        if (side == SplitSide.Left) SplitTabs(dragged, partner, front: dragged);
        else SplitTabs(partner, dragged, front: dragged);
        AnimateSplitIn(dragged);
    }

    /// <summary>
    /// The new page settles into its place, from a touch smaller. Until it's laid out for its width it
    /// stays out of sight (the place shows empty a moment, as in Arc): it was last drawn at another
    /// width, and showed that for a frame or two before snapping to the new one.
    /// </summary>
    private async void AnimateSplitIn(BrowserTab page)
    {
        if (page.Split?.IndexOf(page) is not (>= 0 and var at)) return;
        var entering = _panes[at].Card;
        PageColumns.UpdateLayout();
        if (page.View.CoreWebView2 is { } core)
        {
            entering.Opacity = 0;
            try
            {
                await Deadline.Within(LaidOutAsync(core, PeekExpand.CssWidth(page.View.ActualWidth, page.Zoom)),
                    System.TimeSpan.FromMilliseconds(300), fallback: null);
            }
            catch
            {
                // Shown as it is, rather than not at all.
            }
            entering.Opacity = 1;
        }
        entering.CenterPoint = new Vector3((float)entering.ActualWidth / 2, (float)entering.ActualHeight / 2, 0);
        Spring.To(entering, "Scale", Vector3.One, SplitSpringSeconds, SplitSpringBounce, from: new Vector3(0.96f, 0.96f, 1));
    }

    /// <summary>Separated while you're on a page further right: it slides back over to the left as it widens.</summary>
    private void AnimateSeparate(double pageStart)
    {
        Spring.To(MainCard, "Translation", Vector3.Zero, SplitSpringSeconds, SplitSpringBounce,
            from: new Vector3((float)pageStart, 0, 0));
    }

    // ── The dividers springing to new places: half and half (double-click), or back inside a page's minimum ──

    private TabSplit? _evenSplit;
    private SpringMotion? _evenSpring;
    private double[] _evenFrom = [];
    private double[] _evenTarget = [];
    private double _evenTravel;
    private long _evenFrame;

    private void SpringToShares(TabSplit split, double[] target)
    {
        var room = PageColumns.ActualWidth - SplitGap * (split.Count - 1);
        // In pixels of the furthest any page's edge goes, so it comes to rest within half a pixel.
        var travel = split.Shares.Zip(target, (a, b) => System.Math.Abs(a - b)).Max() * room;
        if (travel < 0.5) return;
        if (!_uiSettings.AnimationsEnabled || room <= 0)
        {
            split.Shares = target;
            LayoutPages();
            return;
        }
        StopEvenSpring();
        _evenSplit = split;
        _evenFrom = split.Shares.ToArray();
        _evenTarget = target;
        _evenTravel = travel;
        _evenSpring = new SpringMotion(SplitSpringSeconds, SplitSpringBounce) { Position = 0, Target = travel };
        _evenFrame = System.Environment.TickCount64;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += EvenFrame;
    }

    private void EvenFrame(object? sender, object e)
    {
        if (_evenSpring is not { } spring || _evenSplit is not { } split || split != _shownSplit || split.Count != _evenTarget.Length)
        {
            StopEvenSpring();
            return;
        }
        var now = System.Environment.TickCount64;
        spring.Step(System.Math.Min(0.05, (now - _evenFrame) / 1000.0));
        _evenFrame = now;
        if (spring.AtRest)
        {
            split.Shares = _evenTarget;
            LayoutPages();
            StopEvenSpring();
            UpdateDragRegions();
            return;
        }
        var progress = spring.Position / _evenTravel;
        split.Shares = _evenFrom.Zip(_evenTarget, (a, b) => System.Math.Max(0.01, a + (b - a) * progress)).ToArray();
        LayoutPages();
    }

    private void StopEvenSpring()
    {
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= EvenFrame;
        _evenSpring = null;
        _evenSplit = null;
    }

    // ── Session ──

    /// <summary>Saved splits come back, each with its row where it was in the tab list.</summary>
    private void RestoreSplits(IReadOnlyList<BrowserTab?> tabs, SessionData session)
    {
        var saved = session.Splits.Select(s => (s.PageList(), s.Row)).ToList();
        foreach (var (index, pages, row) in SplitShares.Restorable(saved, tabs.Count))
        {
            var found = pages.Select(p => tabs[p]).OfType<BrowserTab>().ToList();
            if (found.Count < 2) continue;
            // Its widths, if every page came back.
            var shares = found.Count == saved[index].Item1.Length ? session.Splits[index].ShareList() : null;
            SplitTabs(found, shares, row: row);
        }
        // Saved by an older Drift: each marked tab beside the one before it.
        foreach (var (l, r) in SplitLayout.Pairs(session.Tabs.Select(s => s.SplitWithPrevious).ToList()))
        {
            if (tabs[l] is { } left && tabs[r] is { } right)
            {
                var ratio = session.Tabs[r].SplitRatio ?? 0.5;
                SplitTabs([left, right], [ratio, 1 - ratio]);
            }
        }
    }
}
