using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Drift.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Drift;

/// <summary>
/// While a tab (or folder) is dragged over the sidebar's lists, their rows slide in real time to show
/// where it would land, as the Essentials tiles do: the rest make room, and the dragged row itself
/// (faded) sits in the space. Over another list, or outside, the dragged row is lifted out of its own
/// list and the rows there close up. Where it lands is worked out by RowPreview (Core, tested), on
/// the rows' laid-out places, so the rows moving don't change the answer.
/// </summary>
public sealed partial class MainWindow
{
    private const double DraggedRowOpacity = 0.4;

    /// <summary>The slot each list is showing (null: the dragged row lifted out, or not there).</summary>
    private readonly Dictionary<ListViewBase, int?> _rowPreview = new();

    /// <summary>Some list's rows are moved for a drag (and go back if it's cancelled).</summary>
    private bool _rowPreviewShown;

    /// <summary>Where each list is showing its rows, from the top of its panel.</summary>
    private readonly Dictionary<ListViewBase, Dictionary<object, double>> _previewTops = new();

    /// <summary>The rows of a list as laid out: each row's height runs to the next row's top.</summary>
    private static (List<RowLayout> Rows, double Top) LayoutOf(ListViewBase list)
    {
        var rows = new List<RowLayout>();
        var containers = new List<FrameworkElement>();
        foreach (var item in list.Items)
            if (list.ContainerFromItem(item) is FrameworkElement row) containers.Add(row);
        for (var i = 0; i < containers.Count; i++)
        {
            var height = i + 1 < containers.Count
                ? containers[i + 1].ActualOffset.Y - containers[i].ActualOffset.Y
                : containers[i].ActualHeight;
            var item = list.ItemFromContainer(containers[i]);
            rows.Add(new RowLayout(item, height, IsHeader: item is TabFolder));
        }
        return (rows, containers.Count > 0 ? containers[0].ActualOffset.Y : 0);
    }

    /// <summary>The rows being dragged in this list: the tab, or the folder with everything shown under it.</summary>
    private HashSet<object> DraggedRows(ListViewBase list)
    {
        var rows = new HashSet<object>();
        if (_dragTab is { } tab && list.Items.Contains(tab)) rows.Add(tab);
        if (_dragFolder is { } folder && list == FolderList && FolderRows.IndexOf(folder) is var at and >= 0)
        {
            rows.Add(folder);
            for (var i = at + 1; i < FolderRows.Count; i++)
            {
                var depth = FolderRows[i] switch { TabFolder f => f.Depth, BrowserTab t => t.Depth, _ => 0 };
                if (depth <= folder.Depth) break;
                rows.Add(FolderRows[i]);
            }
        }
        return rows;
    }

    /// <summary>How much room a row arriving from another list needs.</summary>
    private double ArrivingHeight()
    {
        foreach (var list in SlidingLists)
            if (_dragTab is { } tab && list.ContainerFromItem(tab) is FrameworkElement row) return row.ActualHeight;
        return 36;
    }

    /// <summary>Where a drop at the pointer would land in this list.</summary>
    private Landing LandingIn(ListViewBase list, DragEventArgs e)
    {
        var (rows, top) = LayoutOf(list);
        var y = list.ItemsPanelRoot is { } panel ? e.GetPosition(panel).Y - top : 0;
        return RowPreview.Landing(rows, DraggedRows(list), y);
    }

    /// <summary>Slides a list's rows to show the dragged row at a slot (or lifted out, for null).</summary>
    private void ShowRowPreview(ListViewBase list, int? slot)
    {
        if (_rowPreview.TryGetValue(list, out var showing) ? showing == slot : slot is null && DraggedRows(list).Count == 0)
            return;
        _rowPreview[list] = slot;
        _rowPreviewShown = true;

        var (rows, top) = LayoutOf(list);
        var dragged = DraggedRows(list);
        var arriving = dragged.Count == 0 && slot is not null ? ArrivingHeight() : 0;
        var tops = RowPreview.Tops(rows, dragged, slot, arriving);
        _previewTops[list] = tops.ToDictionary(t => t.Key, t => t.Value + top);

        foreach (var row in rows)
        {
            if (list.ContainerFromItem(row.Key) is not FrameworkElement container) continue;
            if (tops.TryGetValue(row.Key, out var to))
            {
                var by = (float)(to + top - container.ActualOffset.Y);
                Spring.To(container, "Translation", new Vector3(0, by, 0), SlideSeconds, SlideBounce);
                container.Opacity = dragged.Contains(row.Key) ? DraggedRowOpacity : 1;
            }
            else container.Opacity = 0;
        }

        // Room at the end for a row from elsewhere; the groups below move down to make it.
        var room = arriving > 0 ? BaseHeight(list) + arriving : 0;
        if (list.MinHeight != room)
        {
            NoteGroupSlideStart();
            list.MinHeight = room;
        }
    }

    private readonly Dictionary<ListViewBase, double> _baseHeights = new();

    /// <summary>The list's height before any room was made in it.</summary>
    private double BaseHeight(ListViewBase list)
    {
        if (!_baseHeights.TryGetValue(list, out var height))
            _baseHeights[list] = height = list.ActualHeight;
        return height;
    }

    /// <summary>Every list's rows back in their own places.</summary>
    private void ClearRowPreviews(bool animate)
    {
        foreach (var list in SlidingLists)
        {
            if (list.ItemsPanelRoot is { } panel)
            {
                foreach (var row in panel.Children)
                {
                    if (animate) Spring.To(row, "Translation", Vector3.Zero, SlideSeconds, SlideBounce);
                    else Spring.Jump(row, "Translation", Vector3.Zero);
                    row.Opacity = 1;
                }
            }
            if (list.MinHeight != 0)
            {
                if (animate) NoteGroupSlideStart();
                list.MinHeight = 0;
            }
        }
        _rowPreview.Clear();
        _previewTops.Clear();
        _baseHeights.Clear();
        _rowPreviewShown = false;
    }

    /// <summary>The dragged row is over somewhere other than these lists: it's lifted out of its own, and gaps close.</summary>
    private void ShowRowPreviewsExcept(object? over)
    {
        foreach (var list in SlidingLists)
            if (list != over) ShowRowPreview(list, null);
    }
}
