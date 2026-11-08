using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;


namespace Drift;

/// <summary>
/// Rows slide to their new places when the sidebar's lists change (a tab moved, closed or dropped,
/// a folder opened or closed), on the same spring as the Essentials tiles. Where each row was is
/// noted as the change starts; after the next layout, SlidePlan says which rows moved and how far,
/// and each springs from its old place to its new one. The groups below a list that grew or shrank
/// slide along too. A folder's rows unfold from under its header; a dropped tab lands where it was
/// dropped.
/// </summary>
public sealed partial class MainWindow
{
    private const double SlideSeconds = 0.3;
    private const double SlideBounce = 0.15;

    private Dictionary<ListViewBase, List<(object, double)>>? _slideRows;
    private List<(object, double)>? _slideGroups;

    /// <summary>The row being dropped, which goes straight to where it was let go.</summary>
    private object? _slideExcept;

    private void WatchForSlides()
    {
        FolderRows.CollectionChanged += (_, _) => NoteSlideStart();
        EssentialTabs.CollectionChanged += (_, _) => NoteSlideStart();
        PinnedTabs.CollectionChanged += (_, _) => NoteSlideStart();
        Tabs.CollectionChanged += (_, _) => NoteSlideStart();
    }

    private IEnumerable<ListViewBase> SlidingLists => new ListViewBase[] { FolderList, PinnedList, TabList };

    /// <summary>A list is changing: where everything is now (layout hasn't caught up yet), once per batch of changes.</summary>
    private void NoteSlideStart()
    {
        if (!_restored || _slideRows is not null) return;
        _slideRows = SlidingLists.ToDictionary(l => l, RowsWere);
        NoteGroupSlideStart();
    }

    /// <summary>A group's size is about to change (a list making room for a dragged row): the groups below slide.</summary>
    private void NoteGroupSlideStart()
    {
        if (!_restored || _slideGroups is not null) return;
        _slideGroups = Groups();
        Sidebar.LayoutUpdated += PlaySlides;
    }

    /// <summary>
    /// Each shown row's item and top, from the containers on screen (the list's items have already
    /// changed); mid-drag, where the rows are shown making room rather than where they're laid out.
    /// </summary>
    private List<(object, double)> RowsWere(ListViewBase list)
    {
        if (_previewTops.TryGetValue(list, out var shown))
            return shown.OrderBy(t => t.Value).Select(t => (t.Key, t.Value)).ToList();
        if (list.ItemsPanelRoot is not { } panel || list.Visibility != Visibility.Visible) return new();
        var seen = new HashSet<object>();
        return panel.Children.OfType<ContentControl>()
            // Recycled containers wait off screen.
            .Where(c => c.Content is not null && c.Visibility == Visibility.Visible && c.ActualOffset.Y is >= 0 and < 100_000)
            .OrderBy(c => c.ActualOffset.Y)
            .Where(c => seen.Add(c.Content))
            .Select(c => (c.Content, (double)c.ActualOffset.Y))
            .ToList();
    }

    private static List<(object, double)> RowsNow(ListViewBase list)
    {
        if (list.Visibility != Visibility.Visible) return new();
        var rows = new List<(object, double)>();
        foreach (var item in list.Items)
            if (list.ContainerFromItem(item) is UIElement row) rows.Add((item, row.ActualOffset.Y));
        return rows;
    }

    /// <summary>The sidebar's groups below the Essentials (the lists, drop zones, divider, New Tab), and where each is.</summary>
    private List<(object, double)> Groups() =>
        Sidebar.Children
            .Where(c => Grid.GetRow((FrameworkElement)c) is >= 2 and <= 7 && c.Visibility == Visibility.Visible)
            .OrderBy(c => c.ActualOffset.Y)
            .Select(c => ((object)c, (double)c.ActualOffset.Y))
            .ToList();

    private void PlaySlides(object? sender, object e)
    {
        Sidebar.LayoutUpdated -= PlaySlides;
        var rowsWere = _slideRows;
        var groupsWere = _slideGroups;
        var except = _slideExcept;
        _slideRows = null;
        _slideGroups = null;
        _slideExcept = null;
        // Rows moved under a drag's preview: it's worked out again on the next move.
        _rowPreview.Clear();
        _previewTops.Clear();

        foreach (var list in rowsWere is null ? Enumerable.Empty<ListViewBase>() : SlidingLists)
        {
            var was = rowsWere![list];
            var now = RowsNow(list);
            var known = new HashSet<object>(was.Select(r => r.Item1));
            var plan = SlidePlan.For(was, now, except, unfold: list == FolderList);
            var planned = new HashSet<object>(plan.Select(s => s.Key));
            foreach (var (item, _) in now)
            {
                // A new row in a recycled container mustn't carry on its last row's slide.
                if (!known.Contains(item) && !planned.Contains(item) && list.ContainerFromItem(item) is UIElement row)
                    Spring.Jump(row, "Translation", Vector3.Zero);
            }
            foreach (var slide in plan)
                if (list.ContainerFromItem(slide.Key) is UIElement row) SlideFrom(row, slide);
        }

        if (groupsWere is null) return;
        foreach (var slide in SlidePlan.For(groupsWere, Groups()))
            if (slide.Key is UIElement group) SlideFrom(group, slide);
    }

    private void SlideFrom(UIElement element, Slide slide)
    {
        Spring.To(element, "Translation", Vector3.Zero, SlideSeconds, SlideBounce, from: new Vector3(0, (float)slide.From, 0));
        if (slide.Fade) FadeIn(element);
    }

    /// <summary>
    /// Hidden while it's still under the row above (it's drawn over it), then in. Through the
    /// element's own Opacity: animating its composition visual as well as its Translation (the
    /// springs) isn't allowed, and throws the next time either is used.
    /// </summary>
    private void FadeIn(UIElement element)
    {
        element.OpacityTransition = null;
        element.Opacity = 0;
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(70);
        timer.IsRepeating = false;
        timer.Tick += (t, _) =>
        {
            t.Stop();
            element.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(170) };
            element.Opacity = 1;
        };
        timer.Start();
    }
}
