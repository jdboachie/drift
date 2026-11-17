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

/// <summary>Split view's divider: a strip with the left-right resize pointer.</summary>
/// <summary>
/// Something dragged sideways to resize: the split view's divider, the sidebar's edge. A Grid with the
/// left-right resize pointer over it (the cursor's only settable from inside the element).
/// </summary>
public sealed partial class ResizeGrip : Grid
{
    public ResizeGrip() => ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
}

/// <summary>
/// A page's handle (SplitHandles.cs): while it's being dragged to swap the pages, the move pointer,
/// arrowheads on all four points (the cursor's only settable from inside the element).
/// </summary>
public sealed partial class PageHandleButton : Button
{
    public void ShowMoving(bool moving) =>
        ProtectedCursor = moving ? InputSystemCursor.Create(InputSystemCursorShape.SizeAll) : null;
}

/// <summary>
/// Split view, like Arc's: two tabs side by side, each its own card, a divider between them to
/// share the width differently (double-click for half and half). In the sidebar it's a row of its
/// own in the tab list with both pages, the one you're on brighter; click a half to go to it, or into
/// a page. Start one by dragging a tab onto a half of the page, or a tab's "Split with current tab".
/// Any two tabs, and a tab is in one place in the sidebar, never two (as in Arc): a regular tab is only
/// in the split's row (TabSplit.Row) while it's split; with a pinned tab in it, the row is in the pinned
/// list where that tab was, and the tab comes back there when they part; an Essential or folder tab
/// keeps its own place, and the row is in the tab list. The right page's view is in the
/// right-hand card (SplitHost). Moving either tab elsewhere, or closing one, separates them first.
/// </summary>
public sealed partial class MainWindow
{
    private const double MinPaneWidth = 320;
    private const double SplitGap = 8;

    // The tabs whose pages are showing: the active tab, or both halves of its split.
    private readonly List<BrowserTab> _shownPages = new();
    private TabSplit? _shownSplit;
    private bool _draggingDivider;

    private static bool CanSplit(BrowserTab a, BrowserTab b) =>
        a != b && !a.IsSplitRow && !b.IsSplitRow && a.Split is null && b.Split is null;

    /// <summary>A tab's row in the tab list, if it has one there.</summary>
    private int? TabListIndex(BrowserTab tab) =>
        tab.Place == TabPlace.Regular && Tabs.IndexOf(tab) is >= 0 and var at ? at : null;

    /// <summary>A tab's row in the pinned list, if it has one there.</summary>
    private int? PinnedListIndex(BrowserTab tab) =>
        tab.Place == TabPlace.Pinned && PinnedTabs.IndexOf(tab) is >= 0 and var at ? at : null;

    /// <summary>The page of a split you were last on (its brighter half).</summary>
    private static BrowserTab FrontOf(TabSplit split) => split.Right.IsFront && !split.Left.IsFront ? split.Right : split.Left;

    /// <summary>
    /// Puts two tabs side by side. With a pinned tab in it, the split's row takes that tab's place in
    /// the pinned list (the higher one's, for two). Otherwise it goes where the higher of their rows in
    /// the tab list was, or at <paramref name="row"/> (a saved split), or at the end when neither has one there.
    /// </summary>
    private void SplitTabs(BrowserTab left, BrowserTab right, double ratio = 0.5, BrowserTab? front = null, int? row = null)
    {
        if (!CanSplit(left, right)) return;
        var pinned = new[] { left, right }.Where(p => p.Place == TabPlace.Pinned).ToList();
        var host = pinned.Count > 0 ? PinnedTabs : Tabs;
        var at = pinned.Count > 0
            ? SplitLayout.RowIndex(PinnedListIndex(left), PinnedListIndex(right), PinnedTabs.Count)
            : row ?? SplitLayout.RowIndex(TabListIndex(left), TabListIndex(right), Tabs.Count);
        var split = new TabSplit(left, right) { Ratio = ratio };
        // The row: a stand-in tab with no page of its own (its view is never started or shown).
        var standIn = BrowserTab.SplitRow(split);
        split.Row = standIn;
        _syncingSelection = true;
        Tabs.Remove(left);
        Tabs.Remove(right);
        foreach (var page in pinned) PinnedTabs.Remove(page);
        host.Insert(System.Math.Clamp(at, 0, host.Count), standIn);
        _syncingSelection = false;

        left.Split = right.Split = split;
        NameSplitRow(split);
        left.PropertyChanged += SplitPage_PropertyChanged;
        right.PropertyChanged += SplitPage_PropertyChanged;
        MoveView(right.View, SplitHost);
        SaveSessionSoon();
        if (front is not null) Activate(front);
        else if (_active == left || _active == right) Activate(_active);
    }

    /// <summary>The row's name, for screen readers: both pages' titles.</summary>
    private static void NameSplitRow(TabSplit split)
    {
        if (split.Row is { } row) row.Title = $"{split.Left.Title} | {split.Right.Title}";
    }

    private void SplitPage_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BrowserTab.Title) && sender is BrowserTab { Split: { } split }) NameSplitRow(split);
    }

    /// <summary>
    /// Two tabs again, left first where the split's row was: in the tab list, its regular tabs; in the
    /// pinned list, its pinned tabs, and a regular one at the end of the tab list. Essentials and folder
    /// tabs were in their places all along.
    /// </summary>
    private void SeparateTabs(TabSplit split)
    {
        var (left, right) = (split.Left, split.Right);
        left.Split = right.Split = null;
        left.IsFront = right.IsFront = true;
        left.PropertyChanged -= SplitPage_PropertyChanged;
        right.PropertyChanged -= SplitPage_PropertyChanged;
        MoveView(right.View, WebHost);
        _syncingSelection = true;
        var host = split.Row is { } kept && PinnedTabs.Contains(kept) ? PinnedTabs : Tabs;
        var at = split.Row is { } row && host.IndexOf(row) is >= 0 and var index ? index : host.Count;
        if (split.Row is { } standIn) host.Remove(standIn);
        split.Row = null;
        foreach (var page in new[] { left, right })
        {
            if (page.Place == TabPlace.Pinned) PinnedTabs.Insert(System.Math.Min(at++, PinnedTabs.Count), page);
            else if (page.Place == TabPlace.Regular && host == PinnedTabs) Tabs.Add(page);
            else if (page.Place == TabPlace.Regular) Tabs.Insert(System.Math.Min(at++, Tabs.Count), page);
        }
        _syncingSelection = false;
        SaveSessionSoon();
        if (_shownSplit == split)
        {
            if (_active == left || _active == right) Activate(_active);
            else
            {
                _shownSplit = null;
                LayoutPages();
            }
        }
    }

    /// <summary>A tab about to move or close leaves its split first.</summary>
    private void LeaveSplit(BrowserTab tab)
    {
        if (tab.Split is { } split) SeparateTabs(split);
    }

    /// <summary>Where a tab's page lives: the right-hand card for a split's right page, the page area otherwise.</summary>
    private Panel HostFor(BrowserTab tab) => tab.Split?.Right == tab ? SplitHost : WebHost;

    private static void MoveView(WebView2 view, Panel host)
    {
        if (view.Parent == host) return;
        (view.Parent as Panel)?.Children.Remove(view);
        host.Children.Add(view);
    }

    /// <summary>Shows the tab's page, beside its split partner if it has one; every other page is hidden.</summary>
    private void ShowPages(BrowserTab tab)
    {
        var split = tab.Split;
        BrowserTab[] showing = split is null ? [tab] : [split.Left, split.Right];
        foreach (var shown in _shownPages.Where(t => !showing.Contains(t)))
            shown.View.Visibility = Visibility.Collapsed;
        _shownPages.Clear();
        _shownPages.AddRange(showing);
        foreach (var page in showing) page.View.Visibility = Visibility.Visible;
        if (split is not null)
        {
            split.Left.IsFront = split.Left == tab;
            split.Right.IsFront = split.Right == tab;
        }
        _shownSplit = split;
        HidePageControls();
        LayoutPages();
        PlaceSplitCards();
    }

    private void LayoutPages()
    {
        if (_shownSplit is not { } split)
        {
            LeftPaneColumn.Width = new GridLength(1, GridUnitType.Star);
            SplitGapColumn.Width = RightPaneColumn.Width = new GridLength(0);
            SplitCard.Visibility = SplitDivider.Visibility = Visibility.Collapsed;
            ShowPageHandles();
            return;
        }
        LeftPaneColumn.Width = new GridLength(split.Ratio, GridUnitType.Star);
        SplitGapColumn.Width = new GridLength(SplitGap);
        RightPaneColumn.Width = new GridLength(1 - split.Ratio, GridUnitType.Star);
        SplitCard.Visibility = SplitDivider.Visibility = Visibility.Visible;
        ShowPageHandles();
    }

    /// <summary>A click into one of the split's pages makes it the one you're on (address bar, shortcuts).</summary>
    private void WireSplitFocus(BrowserTab tab, WebView2 view) =>
        view.GotFocus += (_, _) =>
        {
            if (tab.View == view && tab != _active && tab.Split is { } split && split == _shownSplit) Activate(tab);
        };

    // ── The sidebar ──

    /// <summary>Marks the pill on a split row's half in front (PlaceSplitCard).</summary>
    private sealed class FrontHalf;

    /// <summary>Marks the hover on a split row's half (ShowHalfHover).</summary>
    private sealed class HoverHalf;

    /// <summary>
    /// A split row's pill of one kind, made the first time: the selected card on the half you're on, or
    /// the hover on another; a couple of pixels in from the half, on the row's card, under the halves.
    /// </summary>
    private static Border HalfPill(Grid row, System.Type kind)
    {
        if (row.Children.OfType<Border>().FirstOrDefault(b => b.Tag?.GetType() == kind) is { } made) return made;
        var front = kind == typeof(FrontHalf);
        var pill = new Border
        {
            Tag = front ? new FrontHalf() : new HoverHalf(),
            Margin = new Thickness(0, 3, 0, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
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
        Grid.SetColumnSpan(pill, 3);
        // Just above the row's card (its first child), under the halves.
        row.Children.Insert(1, pill);
        return pill;
    }

    /// <summary>A half of a split's row: that page.</summary>
    private void SplitHalf_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not BrowserTab tab) return;
        // After the row's own selection, which goes to the split's left page.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (tab.Split is null) return;
            Activate(tab);
            tab.View.Focus(FocusState.Programmatic);
        });
    }

    /// <summary>A half, the first time it's shown: it shrinks a little while pressed, by itself.</summary>
    private void SplitHalf_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is UIElement half && !_pressFeedback.Contains(half)) AddPressFeedback(half, 0.97f);
    }

    /// <summary>
    /// Hovered, a half lights up as a tab in a folder does (not the one with the selected card on it), and
    /// each half's × shows at its end, as Arc's do (before its Peek badge, if it has one), the title
    /// stopping short of it. Not on a half narrower than Zen allows one.
    /// </summary>
    private void SplitHalf_PointerEntered(object sender, PointerRoutedEventArgs e) => ShowHalfHover(sender, true);
    private void SplitHalf_PointerExited(object sender, PointerRoutedEventArgs e) => ShowHalfHover(sender, false);

    // A half narrower than this has no × (Zen's limit).
    private const double HalfCloseWidth = 70;

    private void ShowHalfHover(object sender, bool over)
    {
        if (sender is not Grid { Tag: BrowserTab page, Parent: Grid row } hovered) return;
        var carded = page.Split is { } split && split == _shownSplit && FrontOf(split) == page;
        // The hover's the size of the selected card on a half (a couple of pixels in), not the whole half.
        var fill = HalfPill(row, typeof(HoverHalf));
        if (over && !carded && hovered.ActualWidth > 0)
        {
            fill.Width = System.Math.Max(0, hovered.ActualWidth - 4);
            Spring.Jump(fill, "Translation", new Vector3((float)hovered.ActualOffset.X + 2, 0, 0));
            fill.Opacity = 1;
        }
        else if (!over) fill.Opacity = 0;
        foreach (var half in row.Children.OfType<Grid>().Where(h => h.Tag is BrowserTab))
        {
            if (half.Children.OfType<Button>().FirstOrDefault() is not { } close
                || half.Children.OfType<TextBlock>().FirstOrDefault() is not { } title) continue;
            // As in Zen: on any half wide enough, even if the title has to give up all its room to it.
            var shown = over && half.ActualWidth >= HalfCloseWidth;
            close.Opacity = shown ? 1 : 0;
            close.IsHitTestVisible = shown;
            title.Padding = shown ? new Thickness(0, 0, close.Width + 4, 0) : default;
        }
    }

    // ── The card on the half you're on ──

    private void SplitCard_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Border card) PlaceSplitCard(card);
    }

    private void SplitRow_SizeChanged(object sender, SizeChangedEventArgs e)
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
    /// While the split's the tab you're on, it's on a card of its own, the folders' (their colour and
    /// border), and the half you're on has a selected tab's card on it, a couple of pixels in, there at
    /// once when you switch halves. Otherwise it's a row as any tab's is: both cards fade away. No line
    /// between the halves.
    /// </summary>
    private void PlaceSplitCard(Border card)
    {
        if (card.Tag is not BrowserTab { Split: { } split } || card.Parent is not Grid row || row.ActualWidth <= 0) return;
        var shown = split == _shownSplit;
        card.Width = row.ActualWidth;
        Spring.Jump(card, "Translation", Vector3.Zero);
        card.Opacity = shown ? 1 : 0;
        if (row.Children.OfType<Grid>().FirstOrDefault(h => h.Tag == FrontOf(split)) is { ActualWidth: > 0 } half)
        {
            var pill = HalfPill(row, typeof(FrontHalf));
            pill.Width = System.Math.Max(0, half.ActualWidth - 4);
            Spring.Jump(pill, "Translation", new Vector3((float)half.ActualOffset.X + 2, 0, 0));
            pill.Opacity = shown ? 1 : 0;
        }
        if (row.Children.OfType<Border>().FirstOrDefault(b => Grid.GetColumn(b) == 1) is { } hairline) hairline.Opacity = 0;
        // The selected card's arrived (you've just picked a half): its hover goes.
        if (shown && row.Children.OfType<Border>().FirstOrDefault(b => b.Tag is HoverHalf) is { } hover) hover.Opacity = 0;
    }

    /// <summary>
    /// A half's ×: that page leaves the split. A regular tab closes; an Essential, pinned or folder
    /// tab stays where it is in the sidebar. The other page stays on its own.
    /// </summary>
    private void CloseSplitHalf_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not BrowserTab tab) return;
        CloseSplitPage(tab);
        // Its Peek gone and the pointer still on the half: the title takes back the badge's room.
        if ((sender as FrameworkElement)?.Parent is Grid half && tab.Split is not null)
            DispatcherQueue.TryEnqueue(() => ShowHalfHover(half, true));
    }

    /// <summary>A page of a split closes (from its half's × or its handle's menu); the other stays on its own.</summary>
    private void CloseSplitPage(BrowserTab tab)
    {
        if (tab.Split is not { } split) return;
        // Its Peek goes first, as on a tab's own row.
        if (ClosePeekOf(tab)) return;
        if (tab.IsPinned)
        {
            var other = split.Other(tab);
            SeparateTabs(split);
            if (_active == tab) Activate(other);
        }
        else CloseTab(tab);
    }

    /// <summary>The row's Close tabs: its regular tabs close; Essentials, pinned and folder tabs just leave it.</summary>
    private void CloseSplit(TabSplit split, bool remember = true)
    {
        var (left, right) = (split.Left, split.Right);
        SeparateTabs(split);
        // A page that stays (an Essential, pinned or folder tab) is the one you're on after.
        if (new[] { left, right }.FirstOrDefault(p => p.IsPinned) is { } staying && (_active == left || _active == right))
            Activate(staying);
        foreach (var page in new[] { left, right }.Where(p => !p.IsPinned)) RemoveTab(page, remember);
    }

    private void SplitWithCurrent_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not BrowserTab tab || _active is not { } current || !CanSplit(current, tab)) return;
        var width = PageColumns.ActualWidth;
        SplitTabs(current, tab, front: current);
        // The new page settles in on the right, as if dropped there.
        AnimateSplitIn(SplitSide.Right, width, tab);
    }

    private void SeparateTabs_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not BrowserTab { Split: { } split }) return;
        var rightShown = split == _shownSplit && _active == split.Right;
        var rightStart = LeftPaneColumn.ActualWidth + SplitGap;
        SeparateTabs(split);
        if (rightShown) AnimateSeparate(rightStart);
    }

    // ── The divider ──

    private bool _overDivider;

    private void SplitDivider_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _overDivider = true;
        ShowGrip();
    }

    private void SplitDivider_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _overDivider = false;
        ShowGrip();
    }

    // Out of sight until the pointer's on the gap (where the pointer turns to the resize arrows).
    private void ShowGrip() => ShowGrip(SplitGrip, SplitGripAccent, _overDivider, _draggingDivider, atRest: 0);

    /// <summary>
    /// A resize grip (the split divider's, the sidebar edge's): at rest faint (the divider's) or out of
    /// sight (the sidebar's), brighter under the pointer, accent-coloured and a little taller while held.
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
        if (_shownSplit is null) return;
        // Grabbing it stops it springing back to the middle.
        StopEvenSpring();
        _draggingDivider = SplitDivider.CapturePointer(e.Pointer);
        ShowGrip();
        e.Handled = true;
    }

    private void SplitDivider_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_draggingDivider || _shownSplit is not { } split) return;
        var x = e.GetCurrentPoint(PageColumns).Position.X;
        // Past a page's minimum it gives a little, with resistance; it springs back on letting go.
        split.Ratio = SplitLayout.Stretched(x - SplitGap / 2, PageColumns.ActualWidth - SplitGap, MinPaneWidth);
        LayoutPages();
        e.Handled = true;
    }

    private void SplitDivider_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_draggingDivider) return;
        _draggingDivider = false;
        SplitDivider.ReleasePointerCapture(e.Pointer);
        ShowGrip();
        SpringBackInside();
        SaveSessionSoon();
        UpdateDragRegions();
        e.Handled = true;
    }

    private void SplitDivider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_draggingDivider) return;
        _draggingDivider = false;
        ShowGrip();
        SpringBackInside();
    }

    /// <summary>Let go stretched past a page's minimum: back to it, on the same spring as double-clicking.</summary>
    private void SpringBackInside()
    {
        if (_shownSplit is not { } split) return;
        var width = PageColumns.ActualWidth - SplitGap;
        var inside = SplitLayout.Ratio(split.Ratio * width, width, MinPaneWidth);
        if (System.Math.Abs(inside - split.Ratio) * width >= 0.5) SpringToRatio(split, inside);
    }

    /// <summary>Half and half again.</summary>
    private void SplitDivider_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_shownSplit is not { } split) return;
        SpringToRatio(split, 0.5);
        SaveSessionSoon();
    }

    // ── Dragging a tab onto the page ──
    // As in Arc: over a half of the page, the page there makes room. It takes half the width at once
    // (a page can't follow a width that changes every frame) and springs over to the other half,
    // leaving the half under the pointer empty for the tab; crossing to the other half, it springs
    // back. The empty half is outlined. Under the pointer the tab is a card of its page (its icon and title). Let go, and the new
    // page settles into the empty half; drag away, and the page takes the whole width again.
    // Dragging the tab you're on: the tab you were on before takes its place, and makes room for it.

    private const double SplitSpringSeconds = 0.3;
    private const double SplitSpringBounce = 0.15;

    // The half the page has made room on, while a tab is dragged over it.
    private SplitSide? _dropSide;

    /// <summary>The tab a dragged tab would be split with (SplitLayout.Partner), or none.</summary>
    private BrowserTab? SplitPartner(BrowserTab dragged)
    {
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
        AnimateSplitIn(side, width, dragged);
    }

    /// <summary>
    /// The new page settles into its half, from a touch smaller. Until it's laid out for the half's
    /// width it stays out of sight (the half shows empty a moment, as in Arc): it was last drawn at
    /// another width, and showed that for a frame or two before snapping to the new one.
    /// </summary>
    private async void AnimateSplitIn(SplitSide side, double width, BrowserTab page)
    {
        var half = (float)SplitLayout.HalfWidth(width, SplitGap);
        var height = (float)PageColumns.ActualHeight;
        var entering = side == SplitSide.Left ? MainCard : SplitCard;
        if (page.View.CoreWebView2 is { } core)
        {
            entering.Opacity = 0;
            try
            {
                PageColumns.UpdateLayout();
                await Deadline.Within(LaidOutAsync(core, PeekExpand.CssWidth(page.View.ActualWidth, page.Zoom)),
                    System.TimeSpan.FromMilliseconds(300), fallback: null);
            }
            catch
            {
                // Shown as it is, rather than not at all.
            }
            entering.Opacity = 1;
        }
        entering.CenterPoint = new Vector3(half / 2, height / 2, 0);
        Spring.To(entering, "Scale", Vector3.One, SplitSpringSeconds, SplitSpringBounce, from: new Vector3(0.96f, 0.96f, 1));
    }

    /// <summary>Separated while you're on the right-hand page: it slides back over to the left as it widens.</summary>
    private void AnimateSeparate(double rightPageStart)
    {
        Spring.To(MainCard, "Translation", Vector3.Zero, SplitSpringSeconds, SplitSpringBounce,
            from: new Vector3((float)rightPageStart, 0, 0));
    }

    // ── Half and half again (double-click the divider): the divider springs back to the middle ──

    private TabSplit? _evenSplit;
    private SpringMotion? _evenSpring;
    private double _evenTarget;
    private long _evenFrame;

    /// <summary>The divider springs to a ratio: the middle (double-click), or back inside a page's minimum.</summary>
    private void SpringToRatio(TabSplit split, double ratio)
    {
        var width = PageColumns.ActualWidth - SplitGap;
        if (!_uiSettings.AnimationsEnabled || width <= 0)
        {
            split.Ratio = ratio;
            LayoutPages();
            return;
        }
        StopEvenSpring();
        _evenSplit = split;
        _evenTarget = ratio;
        // In pixels of the left page's width, so it comes to rest within half a pixel.
        _evenSpring = new SpringMotion(SplitSpringSeconds, SplitSpringBounce) { Position = split.Ratio * width, Target = ratio * width };
        _evenFrame = System.Environment.TickCount64;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += EvenFrame;
    }

    private void EvenFrame(object? sender, object e)
    {
        if (_evenSpring is not { } spring || _evenSplit is not { } split || split != _shownSplit)
        {
            StopEvenSpring();
            return;
        }
        var now = System.Environment.TickCount64;
        spring.Step(System.Math.Min(0.05, (now - _evenFrame) / 1000.0));
        _evenFrame = now;
        var width = PageColumns.ActualWidth - SplitGap;
        if (width <= 0 || spring.AtRest)
        {
            split.Ratio = _evenTarget;
            LayoutPages();
            StopEvenSpring();
            UpdateDragRegions();
            return;
        }
        split.Ratio = System.Math.Clamp(spring.Position / width, 0.05, 0.95);
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
        var saved = session.Splits.Select(s => (s.Left, s.Right, s.Row)).ToList();
        foreach (var (l, r, row) in SplitLayout.Restorable(saved, tabs.Count))
        {
            var ratio = session.Splits.First(s => s.Left == l && s.Right == r).Ratio;
            if (tabs[l] is { } left && tabs[r] is { } right) SplitTabs(left, right, ratio, row: row);
        }
        // Saved by an older Drift: each marked tab beside the one before it.
        foreach (var (l, r) in SplitLayout.Pairs(session.Tabs.Select(s => s.SplitWithPrevious).ToList()))
        {
            if (tabs[l] is { } left && tabs[r] is { } right)
                SplitTabs(left, right, session.Tabs[r].SplitRatio ?? 0.5);
        }
    }
}
