using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Drift.Models;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Drift;

/// <summary>
/// A split's row in an unpinned group (Split.cs HostInGroup) moves among the group's own rows only: dragged, it follows
/// the pointer 1:1 (not the system's drag, whose picture can't be held back), the group's other rows making room, and
/// past the group's first or last row it gives with more and more resistance (rubber banding), so the group's edge
/// reads as the end of where it can go. Let go, it settles into the place it's over, gliding there.
/// </summary>
public sealed partial class MainWindow
{
    // The press on a group list, for a drag of a split's row that starts from it.
    private (Pointer Pointer, ListView List, double Y)? _groupPress;

    private sealed class GroupRowDragState
    {
        public required ListView List;
        public required ListViewItem Item;
        public required BrowserTab Row;
        public required TabFolder Group;
        public required double StartY;
        // The group's rows below its header, as blocks (a row, or a nested group's header with its rows), in order.
        public required List<(List<object> Rows, double Height)> Blocks;
        public required int From;
        public required double Top, RowTop, RowHeight;
        public double Offset;
        public int To;
    }

    private GroupRowDragState? _groupRowDrag;

    // How far past an edge the row goes for a long pull: Apple's rubber-band constant, over about a row's height.
    private const double RubberConstant = 0.55;

    private static double Rubber(double over, double dimension) =>
        over * dimension * RubberConstant / (dimension + RubberConstant * Math.Abs(over));

    internal void GroupList_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is ListView { ItemsPanelRoot: { } panel } list) _groupPress = (e.Pointer, list, e.GetCurrentPoint(panel).Position.Y);
    }

    /// <summary>A split's row in a group, picked up (instead of the system's drag): true if it's begun.</summary>
    private bool BeginGroupRowDrag(ListView list, BrowserTab row)
    {
        if (row is not { IsSplitRow: true, Split.Group: { } group } || _groupPress is not { } press || press.List != list) return false;
        if (list.ContainerFromItem(row) is not ListViewItem item || list.ItemsPanelRoot is not { } panel) return false;
        var rows = RowsOfList(list).ToList();
        var header = rows.IndexOf(group);
        var at = rows.IndexOf(row);
        if (header < 0 || at <= header) return false;
        var end = header + 1;
        while (end < rows.Count && DepthOf(rows[end]) > group.Depth) end++;
        if (list.ContainerFromItem(rows[header + 1]) is not FrameworkElement first || list.ContainerFromItem(rows[end - 1]) is not FrameworkElement last) return false;

        // Each row's room: down to the next row's top (the last, its own height and margins).
        double RoomOf(int i) => i + 1 < rows.Count && list.ContainerFromItem(rows[i + 1]) is FrameworkElement next && list.ContainerFromItem(rows[i]) is FrameworkElement here
            ? next.ActualOffset.Y - here.ActualOffset.Y
            : list.ContainerFromItem(rows[i]) is FrameworkElement only ? only.ActualHeight + only.Margin.Top + only.Margin.Bottom : 0;
        var blocks = new List<(List<object> Rows, double Height)>();
        var from = -1;
        for (var i = header + 1; i < end;)
        {
            var block = new List<object> { rows[i] };
            var height = RoomOf(i);
            var j = i + 1;
            while (j < end && DepthOf(rows[j]) > DepthOf(rows[i])) { block.Add(rows[j]); height += RoomOf(j); j++; }
            if (rows[i] == row) from = blocks.Count;
            blocks.Add((block, height));
            i = j;
        }
        if (from < 0 || !item.CapturePointer(press.Pointer)) return false;

        _groupRowDrag = new GroupRowDragState
        {
            List = list,
            Item = item,
            Row = row,
            Group = group,
            // Held where it was grabbed: from the first move it's under the pointer as it was then.
            StartY = press.Y,
            Blocks = blocks,
            From = from,
            To = from,
            Top = first.ActualOffset.Y,
            RowTop = item.ActualOffset.Y,
            RowHeight = blocks[from].Height,
        };
        item.PointerMoved += GroupRowDrag_PointerMoved;
        item.PointerReleased += GroupRowDrag_PointerReleased;
        item.PointerCaptureLost += GroupRowDrag_PointerCaptureLost;
        Canvas.SetZIndex(item, 1);
        // The group's card drawn whole meanwhile (DragCards.cs), so the row moves over it rather than taking its piece along.
        ShowDragCards(list, RestingCardSpans(list));
        return true;
    }

    private void GroupRowDrag_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_groupRowDrag is not { } drag || drag.List.ItemsPanelRoot is not { } panel) return;
        var y = e.GetCurrentPoint(panel).Position.Y;
        // Where it can go: from the group's first row's top to its last row's bottom.
        var total = drag.Blocks.Sum(b => b.Height);
        var min = drag.Top - drag.RowTop;
        var max = drag.Top + total - drag.RowHeight - drag.RowTop;
        var pulled = y - drag.StartY;
        drag.Offset = pulled < min ? min + Rubber(pulled - min, drag.RowHeight)
            : pulled > max ? max + Rubber(pulled - max, drag.RowHeight)
            : pulled;
        drag.Item.Translation = new Vector3(0, (float)drag.Offset, 0);

        // The place it's over: among the others, where its middle is.
        var middle = drag.RowTop + drag.Offset + drag.RowHeight / 2;
        var others = drag.Blocks.Where((_, i) => i != drag.From).ToList();
        var to = others.Count;
        var top = drag.Top;
        for (var i = 0; i < others.Count; i++)
        {
            if (middle < top + others[i].Height / 2) { to = i; break; }
            top += others[i].Height;
        }
        if (to == drag.To) return;
        drag.To = to;
        // The others make room for it there, sliding.
        var order = others.ToList();
        order.Insert(to, drag.Blocks[drag.From]);
        var at = drag.Top;
        foreach (var block in order)
        {
            var original = drag.Top + drag.Blocks.TakeWhile(b => b.Rows != block.Rows).Sum(b => b.Height);
            if (block.Rows != drag.Blocks[drag.From].Rows)
                foreach (var row in block.Rows)
                    if (drag.List.ContainerFromItem(row) is UIElement container)
                        Spring.To(container, "Translation", new Vector3(0, (float)(at - original), 0), SlideSeconds, SlideBounce);
            at += block.Height;
        }
    }

    private void GroupRowDrag_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        (sender as UIElement)?.ReleasePointerCapture(e.Pointer);
        EndGroupRowDrag(drop: true);
        e.Handled = true;
    }

    private void GroupRowDrag_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => EndGroupRowDrag(drop: true);

    /// <summary>Let go: its pages move to the place it's over among the group's contents, and the row glides there.</summary>
    private void EndGroupRowDrag(bool drop)
    {
        if (_groupRowDrag is not { } drag) return;
        _groupRowDrag = null;
        drag.Item.PointerMoved -= GroupRowDrag_PointerMoved;
        drag.Item.PointerReleased -= GroupRowDrag_PointerReleased;
        drag.Item.PointerCaptureLost -= GroupRowDrag_PointerCaptureLost;
        Canvas.SetZIndex(drag.Item, 0);
        var shownAt = drag.RowTop + drag.Offset;

        if (drop && drag.To != drag.From && drag.Row.Split is { } split)
        {
            var pages = split.Pages.Where(p => p.Folder == drag.Group && drag.Group.Children.Contains(p))
                .OrderBy(p => drag.Group.Children.IndexOf(p)).Cast<object>().ToList();
            var others = drag.Blocks.Where((_, i) => i != drag.From).ToList();
            // Before the first thing of the block it's dropped above (a split's row: its first page), or at the end.
            var before = drag.To < others.Count ? others[drag.To].Rows[0] : null;
            var target = before switch
            {
                BrowserTab { IsSplitRow: true, Split: { } other } => other.Pages.Where(p => drag.Group.Children.Contains(p))
                    .OrderBy(p => drag.Group.Children.IndexOf(p)).Cast<object>().FirstOrDefault(),
                null => null,
                _ => before,
            };
            foreach (var page in pages) drag.Group.Children.Remove(page);
            var index = target is not null && drag.Group.Children.IndexOf(target) is >= 0 and var i ? i : drag.Group.Children.Count;
            drag.Group.Children.InsertRange(index, pages);
            SaveSessionSoon();
        }

        // The rows as they're shown are where they'll be laid out: no slide of the list's own (RefreshFolderRowsForMotion).
        foreach (var block in drag.Blocks)
            foreach (var row in block.Rows)
                if (drag.List.ContainerFromItem(row) is UIElement container) Spring.Jump(container, "Translation", Vector3.Zero);
        RefreshFolderRowsForMotion();
        drag.List.UpdateLayout();
        EndDragCards();
        // The row glides from where it was let go to its place (a rubber-banded pull springs back).
        if (drag.List.ContainerFromItem(drag.Row) is FrameworkElement landed)
        {
            var from = shownAt - landed.ActualOffset.Y;
            if (Math.Abs(from) >= 0.5) Spring.To(landed, "Translation", Vector3.Zero, SlideSeconds, 0, from: new Vector3(0, (float)from, 0));
        }
        _groupPress = null;
    }
}
