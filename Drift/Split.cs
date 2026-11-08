using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Drift.Models;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;

namespace Drift;

/// <summary>Split view's divider: a strip with the left-right resize pointer.</summary>
public sealed partial class SplitDivider : Grid
{
    public SplitDivider() => ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
}

/// <summary>
/// Split view, like Arc's: two tabs side by side, each its own card, a divider between them to
/// share the width differently (double-click for half and half). In the sidebar it's a row of its
/// own in the tab list with both pages, the one you're on brighter; click a half to go to it, or into
/// a page. Start one by dragging a tab onto a half of the page, or a tab's "Split with current tab".
/// Any two tabs: a regular tab is only in the split's row (TabSplit.Row) while it's split; an
/// Essential, pinned or folder tab keeps its own place too. The right page's view is in the
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

    /// <summary>The page of a split you were last on (its brighter half).</summary>
    private static BrowserTab FrontOf(TabSplit split) => split.Right.IsFront && !split.Left.IsFront ? split.Right : split.Left;

    /// <summary>
    /// Puts two tabs side by side. The split's row goes where the higher of their rows in the tab list
    /// was, or, at <paramref name="row"/> (a saved split), or at the end when neither has one there.
    /// </summary>
    private void SplitTabs(BrowserTab left, BrowserTab right, double ratio = 0.5, BrowserTab? front = null, int? row = null)
    {
        if (!CanSplit(left, right)) return;
        var at = row ?? SplitLayout.RowIndex(TabListIndex(left), TabListIndex(right), Tabs.Count);
        var split = new TabSplit(left, right) { Ratio = ratio };
        // The row: a stand-in tab with no page of its own (its view is never started or shown).
        var standIn = BrowserTab.SplitRow(split);
        split.Row = standIn;
        _syncingSelection = true;
        Tabs.Remove(left);
        Tabs.Remove(right);
        Tabs.Insert(System.Math.Clamp(at, 0, Tabs.Count), standIn);
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
    /// Two tabs again: regular ones come back to the tab list where the split's row was, left first;
    /// the others were in their places all along.
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
        var at = split.Row is { } row && Tabs.IndexOf(row) is >= 0 and var index ? index : Tabs.Count;
        if (split.Row is { } standIn) Tabs.Remove(standIn);
        split.Row = null;
        foreach (var page in new[] { left, right }.Where(p => p.Place == TabPlace.Regular))
            Tabs.Insert(System.Math.Min(at++, Tabs.Count), page);
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
        LayoutPages();
    }

    private void LayoutPages()
    {
        if (_shownSplit is not { } split)
        {
            LeftPaneColumn.Width = new GridLength(1, GridUnitType.Star);
            SplitGapColumn.Width = RightPaneColumn.Width = new GridLength(0);
            SplitCard.Visibility = SplitDivider.Visibility = Visibility.Collapsed;
            return;
        }
        LeftPaneColumn.Width = new GridLength(split.Ratio, GridUnitType.Star);
        SplitGapColumn.Width = new GridLength(SplitGap);
        RightPaneColumn.Width = new GridLength(1 - split.Ratio, GridUnitType.Star);
        SplitCard.Visibility = SplitDivider.Visibility = Visibility.Visible;
    }

    /// <summary>A click into one of the split's pages makes it the one you're on (address bar, shortcuts).</summary>
    private void WireSplitFocus(BrowserTab tab, WebView2 view) =>
        view.GotFocus += (_, _) =>
        {
            if (tab.View == view && tab != _active && tab.Split is { } split && split == _shownSplit) Activate(tab);
        };

    // ── The sidebar ──

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

    /// <summary>A half's close button shows while that half is hovered (it takes no room otherwise).</summary>
    private void SplitHalf_PointerEntered(object sender, PointerRoutedEventArgs e) => ShowHalfClose(sender, true);
    private void SplitHalf_PointerExited(object sender, PointerRoutedEventArgs e) => ShowHalfClose(sender, false);

    private static void ShowHalfClose(object half, bool show)
    {
        if (half is Grid grid && grid.Children.OfType<Button>().FirstOrDefault() is { } close)
            close.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// A half's ×: that page leaves the split. A regular tab closes; an Essential, pinned or folder
    /// tab stays where it is in the sidebar. The other page stays on its own.
    /// </summary>
    private void CloseSplitHalf_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not BrowserTab { Split: { } split } tab) return;
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
    private void CloseSplit(TabSplit split)
    {
        var (left, right) = (split.Left, split.Right);
        SeparateTabs(split);
        // A page that stays (an Essential, pinned or folder tab) is the one you're on after.
        if (new[] { left, right }.FirstOrDefault(p => p.IsPinned) is { } staying && (_active == left || _active == right))
            Activate(staying);
        foreach (var page in new[] { left, right }.Where(p => !p.IsPinned)) RemoveTab(page);
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

    /// <summary>
    /// A tab's menu: split it with the tab you're on. A split's row has only Separate tabs and Close
    /// tabs: it can't be pinned or moved into a folder as one.
    /// </summary>
    private void ShowSplitMenuItems(MenuFlyout menu)
    {
        foreach (var item in menu.Items.OfType<MenuFlyoutItemBase>())
        {
            if (item.Tag is not BrowserTab tab) continue;
            var text = (item as MenuFlyoutItem)?.Text ?? (item as MenuFlyoutSubItem)?.Text;
            if (text == "Split with current tab")
                item.Visibility = _active is { } current && CanSplit(current, tab) ? Visibility.Visible : Visibility.Collapsed;
            else if (text == "Separate tabs")
                item.Visibility = tab.IsSplitRow ? Visibility.Visible : Visibility.Collapsed;
            else if (text is "Close tab" or "Close tabs")
                ((MenuFlyoutItem)item).Text = tab.IsSplitRow ? "Close tabs" : "Close tab";
            else if (text is "Pin tab" or "Add to Essentials" or "Add to folder")
                item.Visibility = tab.IsSplitRow ? Visibility.Collapsed : Visibility.Visible;
        }
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

    /// <summary>The grip: faint at rest, brighter under the pointer, accent-coloured and a little taller while held.</summary>
    private void ShowGrip()
    {
        SplitGrip.Opacity = _draggingDivider ? 0 : _overDivider ? 0.6 : 0.3;
        SplitGripAccent.Opacity = _draggingDivider ? 1 : 0;
        var grow = _draggingDivider ? new Vector3(1, 1.25f, 1) : Vector3.One;
        foreach (var grip in new UIElement[] { SplitGrip, SplitGripAccent })
        {
            grip.CenterPoint = new Vector3(2, 16, 0);
            Spring.To(grip, "Scale", grow, 0.3, _draggingDivider ? 0.2 : 0.3);
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
        split.Ratio = SplitLayout.Ratio(x - SplitGap / 2, PageColumns.ActualWidth - SplitGap, MinPaneWidth);
        LayoutPages();
        e.Handled = true;
    }

    private void SplitDivider_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_draggingDivider) return;
        _draggingDivider = false;
        SplitDivider.ReleasePointerCapture(e.Pointer);
        ShowGrip();
        SaveSessionSoon();
        e.Handled = true;
    }

    private void SplitDivider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_draggingDivider) return;
        _draggingDivider = false;
        ShowGrip();
    }

    /// <summary>Half and half again.</summary>
    private void SplitDivider_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_shownSplit is not { } split) return;
        SpringToEven(split);
        SaveSessionSoon();
    }

    // ── Dragging a tab onto the page ──
    // As in Arc: over a half of the page, the page there makes room. It takes half the width at once
    // (a page can't follow a width that changes every frame) and springs over to the other half,
    // leaving the half under the pointer empty for the tab; crossing to the other half, it springs
    // back. The empty half is outlined. Under the pointer the tab is a card of its page (its icon and title). Let go, and the new
    // page settles into the empty half; drag away, and the page takes the whole width again.

    private const double SplitSpringSeconds = 0.3;
    private const double SplitSpringBounce = 0.15;

    // The half the page has made room on, while a tab is dragged over it.
    private SplitSide? _dropSide;

    /// <summary>While a tab is dragged: if it can go beside the page in front, the page takes the drop.</summary>
    private void ShowSplitDrop(BrowserTab dragged)
    {
        _dropSide = null;
        SplitDropLayer.Visibility = _active is { } current && CanSplit(current, dragged) && !IsPeekShown
            ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The drag's over: the page takes the whole width again, if it had made room.</summary>
    private void HideSplitDrop()
    {
        SplitDropLayer.Visibility = Visibility.Collapsed;
        MakeRoom(null);
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
        MakeRoom(SplitLayout.Side(e.GetPosition(SplitDropLayer).X, SplitDropLayer.ActualWidth));
    }

    private void SplitDrop_DragLeave(object sender, DragEventArgs e)
    {
        // Only leaving the page counts (not a pointer still over it).
        var at = e.GetPosition(SplitDropLayer);
        if (at.X >= 0 && at.Y >= 0 && at.X < SplitDropLayer.ActualWidth && at.Y < SplitDropLayer.ActualHeight) return;
        MakeRoom(null);
    }

    private void SplitDrop_Drop(object sender, DragEventArgs e)
    {
        var dragged = _dragTab;
        var width = SplitDropLayer.ActualWidth;
        var side = SplitLayout.Side(e.GetPosition(SplitDropLayer).X, width);
        if (dragged is null || _active is not { } current || !CanSplit(current, dragged))
        {
            HideSplitDrop();
            return;
        }
        e.AcceptedOperation = DataPackageOperation.Move;
        // The page is already where the split puts it: the split takes over from here, without a jump.
        _dropSide = null;
        SplitDropLayer.Visibility = Visibility.Collapsed;
        SplitDropSlot.Opacity = 0;
        MainCard.Margin = new Thickness(0);
        Spring.Jump(MainCard, "Translation", Vector3.Zero);
        if (side == SplitSide.Left) SplitTabs(dragged, current, front: dragged);
        else SplitTabs(current, dragged, front: dragged);
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
    private long _evenFrame;

    private void SpringToEven(TabSplit split)
    {
        var width = PageColumns.ActualWidth - SplitGap;
        if (!_uiSettings.AnimationsEnabled || width <= 0)
        {
            split.Ratio = 0.5;
            LayoutPages();
            return;
        }
        StopEvenSpring();
        _evenSplit = split;
        // In pixels of the left page's width, so it comes to rest within half a pixel.
        _evenSpring = new SpringMotion(SplitSpringSeconds, SplitSpringBounce) { Position = split.Ratio * width, Target = width / 2 };
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
            split.Ratio = 0.5;
            LayoutPages();
            StopEvenSpring();
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
