using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Drift.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

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
            // The last row's room around it too, as the others have (to the next row's top), so a row
            // shown after it sits as far below it as the rows above do.
            var height = i + 1 < containers.Count
                ? containers[i + 1].ActualOffset.Y - containers[i].ActualOffset.Y
                : containers[i].ActualHeight + containers[i].Margin.Top + containers[i].Margin.Bottom;
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
        return 38;
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
        // A list's rows have changed (a folder springing open under the drag) and aren't laid out yet: where
        // they are isn't known until then (PlaySlides, which has them worked out again).
        if (_slideRows is not null) return;
        if (_rowPreview.TryGetValue(list, out var showing) ? showing == slot : slot is null && DraggedRows(list).Count == 0)
            return;
        _rowPreview[list] = slot;
        _rowPreviewShown = true;

        var (rows, top) = LayoutOf(list);
        var dragged = DraggedRows(list);
        var arrivingRow = dragged.Count == 0 && slot is not null ? ArrivingHeight() : 0;
        var arriving = arrivingRow;
        // The folders' cards fit the rows as they're shown.
        var cards = list == FolderList ? PreviewFolderCards(ref rows, dragged, slot, ref arriving) : null;
        var tops = RowPreview.Tops(rows, dragged, slot, arriving);
        _previewTops[list] = tops.ToDictionary(t => t.Key, t => t.Value + top);

        foreach (var row in rows)
        {
            if (list.ContainerFromItem(row.Key) is not FrameworkElement container) continue;
            if (tops.TryGetValue(row.Key, out var to))
            {
                var by = (float)(to + top - container.ActualOffset.Y);
                Spring.To(container, "Translation", new Vector3(0, by, 0), SlideSeconds, SlideBounce);
                FadeRow(list, container, dragged.Contains(row.Key) ? DraggedRowOpacity : 1);
            }
            else container.Opacity = 0;
        }
        if (cards is not null)
        {
            // Where each row is shown, for its card's edges; a row arriving from another list has its gap's top.
            var heights = rows.ToDictionary(r => r.Key, r => r.Height, ReferenceEqualityComparer.Instance);
            var shown = new List<(object? Key, double Top, double Height, CardRow Cards)>();
            double gapTop = top, gapRoom = 0;
            foreach (var (key, depth, cardRow) in cards)
            {
                if (key is null)
                {
                    shown.Add((null, gapTop + gapRoom, arrivingRow, cardRow));
                    (gapTop, gapRoom) = (gapTop + gapRoom, arriving);
                    continue;
                }
                if (FolderList.ContainerFromItem(key) is not ListViewItem item || !tops.TryGetValue(key, out var at)) continue;
                shown.Add((key, at + top, item.ActualHeight, cardRow));
                (gapTop, gapRoom) = (at + top, heights[key]);
                // The dragged rows sit in from the side as they will where they'd land.
                if (dragged.Contains(key)) IndentShownRow(item, depth, cardRow);
            }
            ShowDragCards(FolderCard.Spans(shown, CardRoom));
        }

        // Room at the end for a row from elsewhere; the groups below move down to make it.
        var room = arriving > 0 ? BaseHeight(list) + arriving : 0;
        if (list.MinHeight != room)
        {
            NoteGroupSlideStart();
            list.MinHeight = room;
        }
    }

    /// <summary>
    /// Mid-drag in the folder list, the folders' cards as they'll be once it's dropped (FolderCard.Preview):
    /// each card one surface round its rows, growing and shrinking with them, never going with the dragged
    /// row. Where a card's end comes or goes, the room under it comes or goes too; a row arriving from
    /// another list opens a row's gap, with the room under it its cards need.
    /// </summary>
    private List<(object? Key, int Depth, CardRow Cards)> PreviewFolderCards(ref List<RowLayout> rows, HashSet<object> dragged, int? slot, ref double arriving)
    {
        var inputs = rows.Select(r => CardInput(r.Key) is var (depth, isFolder, open) ? (r.Key, depth, isFolder, open) : default).ToList();
        var shown = FolderCard.Preview(inputs, dragged, slot, _folderLandingDepth, arriving > 0);

        var ends = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        foreach (var (key, _, cards) in shown) if (key is not null) ends[key] = cards.Ends;
        rows = rows.Select(r => ends.TryGetValue(r.Key, out var now) && _folderCardRows.TryGetValue(r.Key, out var was)
            ? r with { Height = r.Height + (now - was.Ends) * CardRoom }
            : r).ToList();
        if (shown.FindIndex(s => s.Key is null) is var at and >= 0)
            arriving = RowGap + arriving + RowGap + shown[at].Cards.Ends * CardRoom;
        return shown;
    }

    /// <summary>
    /// A row shown faded (the dragged one, in its slot) or not. In the folder list, only the row itself:
    /// its pieces of the folders' cards stay, as the cards are the folders', not the row's.
    /// </summary>
    private void FadeRow(ListViewBase list, UIElement container, double opacity)
    {
        if (list != FolderList)
        {
            container.Opacity = opacity;
            return;
        }
        container.Opacity = 1;
        foreach (var part in RowOwnParts(container)) part.Opacity = opacity;
    }

    /// <summary>A folder list row's own parts: all but its pieces of the cards (Folders.cs).</summary>
    private static List<UIElement> RowOwnParts(UIElement container) =>
        container is ListViewItem item && VisualTreeHelper.GetChildrenCount(item) > 0 && VisualTreeHelper.GetChild(item, 0) is Grid root
            ? root.Children.Where(c => c is not Border { Tag: CardTag }).ToList()
            : [container];

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
                    FadeRow(list, row, 1);
                }
            }
            // The rows' own sides, and their pieces drawing the cards again once they're back (DragCards.cs).
            if (list == FolderList && _dragCardsShown)
            {
                foreach (var row in FolderRows) IndentFolderRow(FolderList.ContainerFromItem(row), row);
                HandBackDragCards(animate);
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
