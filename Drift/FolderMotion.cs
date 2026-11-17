using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Drift.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Drift;

/// <summary>
/// A folder opening and closing as Dia's groups do. Closing, its card's rounded bottom edge comes up
/// over its tabs, which stay where they are and are cut off from below, and everything under the folder
/// comes up with it (its parent's card too, which closes up behind it); opening, the edge goes down and
/// uncovers them. The chevron turns at the same time (its RotationTransition, MainWindow.xaml).
/// Laid out once, as the folder is when open: the motion is drawing only. Each of the folder's rows has
/// what's on it cut off at the edge (XAML's own clips: a clip on a row's composition visual is XAML's,
/// and setting one took Drift down) and its pieces of the cards shortened to match; everything below the
/// folder is moved up by the room the edge hasn't reached (Translation). Changing the rows' room each
/// frame instead didn't keep up: the list's panel laid them out a frame or more behind.
/// </summary>
public sealed partial class MainWindow
{
    // Dia's: about 130ms closing, 100ms opening, slowing as it gets there.
    private const double FolderCloseSeconds = 0.14;
    private const double FolderOpenSeconds = 0.12;

    /// <summary>A card piece in a row, as it is when the folder's open.</summary>
    private sealed record MotionPiece(Border Card, CardPiece Piece, Thickness Margin, CornerRadius Corners, Thickness Edges, bool Shown);

    /// <summary>A row of the folder's (the header too), where it is from the header's top when the folder's open.</summary>
    private sealed record MotionRow(object Row, ListViewItem Item, double Top, double Height, List<MotionPiece> Pieces, List<UIElement> Content);

    private sealed class FolderMotion
    {
        public required int Level;
        public required MotionRow Header;
        public required List<MotionRow> Rows;
        public required List<UIElement> Below;
        public required bool Opening;
        // The folder's room's end and its card's bottom, from the header's top, open and shut.
        public required double OpenRoom, ShutRoom, OpenCard, ShutCard;
        // For each card it's in: how far its bottom is above the folder's room's end, open.
        public required Dictionary<int, double> ParentGap;
        // The parents whose cards end with the folder's last row (they round off as it closes).
        public required HashSet<int> ParentsEnding;
        public required double Seconds;
        public readonly Stopwatch Clock = Stopwatch.StartNew();
    }

    private FolderMotion? _folderMotion;

    // While the motion changes the rows itself: the sidebar's own slides (Slides.cs) stay out of it.
    private bool _folderMotionRefresh;

    private void RefreshFolderRowsForMotion()
    {
        _folderMotionRefresh = true;
        try { RefreshFolderRows(); }
        finally { _folderMotionRefresh = false; }
    }

    /// <summary>Opens or closes the folder with its edge moving; false if it can't (it's then up to the caller).</summary>
    private bool AnimateFolder(TabFolder folder)
    {
        FinishFolderMotion();
        if (!_uiSettings.AnimationsEnabled || folder.IsRenaming || FolderList.ContainerFromItem(folder) is not ListViewItem) return false;
        var opening = !folder.IsExpanded;
        // Closing with the tab in front in it: that tab stays under the header, so it just closes. And the
        // other way: closed but still showing tabs under it, it just opens (starting from the header alone,
        // the tabs showing went and the rows below jumped up, then all came back down).
        if (!opening && _active is IFolderTab front && folder.AllTabs.Contains(front)) return false;
        var at0 = FolderRows.IndexOf(folder);
        if (opening && at0 >= 0 && at0 + 1 < FolderRows.Count && DepthOf(FolderRows[at0 + 1]) > folder.Depth) return false;

        // The chevron turns now. Opening, the rows are laid out at once (and cut back to the header before
        // they're drawn); closing, they stay until the edge has come up over them.
        _folderTree.Toggle(folder, _active);
        double shutRoomBelow;
        if (opening)
        {
            shutRoomBelow = FolderList.ContainerFromItem(folder) is ListViewItem shut ? shut.Margin.Bottom : RowGap;
            RefreshFolderRowsForMotion();
            FolderList.UpdateLayout();
        }
        else
        {
            // Where it'll be once shut: the room under its header then.
            var next = _folderTree.Rows(_active);
            var cards = FolderCard.Layout(next.Select(CardInput).ToList());
            var index = next.ToList().IndexOf(folder);
            shutRoomBelow = RowGap + (index >= 0 ? cards[index].Ends * CardRoom : 0);
        }
        if (FolderList.ContainerFromItem(folder) is not ListViewItem header)
        {
            if (!opening) RefreshFolderRows();
            return true;
        }

        var at = FolderRows.IndexOf(folder);
        var end = at + 1;
        while (at >= 0 && end < FolderRows.Count && DepthOf(FolderRows[end]) > folder.Depth) end++;
        var rows = new List<MotionRow>();
        for (var i = at + 1; i < end; i++)
            if (FolderList.ContainerFromItem(FolderRows[i]) is ListViewItem item)
                rows.Add(Measure(FolderRows[i], item, item.TransformToVisual(header).TransformPoint(default).Y));
        if (rows.Count == 0)
        {
            if (!opening) RefreshFolderRows();
            return true;
        }
        // Under the folder: the folder list's rows after it, and the rest of the sidebar's list below it.
        var below = new List<UIElement>();
        for (var i = end; i < FolderRows.Count; i++)
            if (FolderList.ContainerFromItem(FolderRows[i]) is UIElement item) below.Add(item);
        below.AddRange(SidebarScrollContent.Children.Where(c => Grid.GetRow((FrameworkElement)c) > Grid.GetRow(FolderList)));

        var last = rows[^1];
        var openRoom = last.Top + last.Height + last.Item.Margin.Bottom;
        double BottomOf(MotionRow row, MotionPiece piece) => row.Top + row.Height - piece.Margin.Bottom;
        var own = last.Pieces.FirstOrDefault(p => p.Piece.Level == folder.Depth);
        var parentGap = new Dictionary<int, double>();
        var ending = new HashSet<int>();
        foreach (var piece in last.Pieces.Where(p => p.Piece.Level < folder.Depth))
        {
            parentGap[piece.Piece.Level] = openRoom - BottomOf(last, piece);
            if (piece.Piece.Part == CardPart.Bottom) ending.Add(piece.Piece.Level);
        }
        _folderMotion = new FolderMotion
        {
            Level = folder.Depth,
            Header = Measure(folder, header, 0),
            Rows = rows,
            Below = below,
            Opening = opening,
            OpenRoom = openRoom,
            ShutRoom = header.ActualHeight + shutRoomBelow,
            OpenCard = own is null ? openRoom - RowGap : BottomOf(last, own),
            ShutCard = header.ActualHeight,
            ParentGap = parentGap,
            ParentsEnding = ending,
            Seconds = opening ? FolderOpenSeconds : FolderCloseSeconds,
        };
        // Anything still sliding from an earlier change stops where it belongs: the motion moves it now.
        foreach (var element in below.Concat(rows.Select(r => (UIElement)r.Item))) Spring.Jump(element, "Translation", Vector3.Zero);
        // Opening, cut back to the header before the first frame shows the rows.
        ApplyFolderMotion(_folderMotion, opening ? 0 : 1);
        CompositionTarget.Rendering += FolderMotion_Rendering;
        return true;
    }

    private static MotionRow Measure(object row, ListViewItem item, double top)
    {
        var pieces = new List<MotionPiece>();
        var content = new List<UIElement>();
        if (VisualTreeHelper.GetChildrenCount(item) > 0 && VisualTreeHelper.GetChild(item, 0) is Grid root)
            foreach (var child in root.Children)
            {
                if (child is Border { Tag: CardTag tag } card) pieces.Add(new MotionPiece(card, tag.Piece, card.Margin, card.CornerRadius, card.BorderThickness, card.Visibility == Visibility.Visible));
                else content.Add(child);
            }
        return new MotionRow(row, item, top, item.ActualHeight, pieces, content);
    }

    private void FolderMotion_Rendering(object? sender, object e)
    {
        if (_folderMotion is not { } motion) return;
        var t = Math.Min(1, motion.Clock.Elapsed.TotalSeconds / motion.Seconds);
        // Fast at first, slowing as it gets there.
        var eased = 1 - Math.Pow(1 - t, 3);
        ApplyFolderMotion(motion, motion.Opening ? eased : 1 - eased);
        if (t >= 1) FinishFolderMotion();
    }

    /// <summary>The folder <paramref name="open"/> of the way open (0 shut, 1 open): its rows cut at its card's edge, what's below moved up to it.</summary>
    private static void ApplyFolderMotion(FolderMotion motion, double open)
    {
        var room = motion.ShutRoom + (motion.OpenRoom - motion.ShutRoom) * open;
        var edge = motion.ShutCard + (motion.OpenCard - motion.ShutCard) * open;
        var lift = new Vector3(0, (float)(room - motion.OpenRoom), 0);
        foreach (var element in motion.Below) element.Translation = lift;
        foreach (var row in motion.Rows.Prepend(motion.Header)) CutRow(motion, row, room, edge);
    }

    /// <summary>
    /// What's on the row is cut off at the folder's card's edge, and its card pieces end there too: the
    /// folder's own rounding off if the edge is across it, its parents' going on to where the folder's
    /// room ends (and rounding off there if they end with it).
    /// </summary>
    private static void CutRow(FolderMotion motion, MotionRow row, double room, double edge)
    {
        var cut = edge - row.Top;
        // Wide and tall enough above that only the bottom is cut: the card reaches past the row on every side.
        const double Reach = 200;
        foreach (var element in row.Content)
            element.Clip = new RectangleGeometry { Rect = new Rect(-Reach, -Reach, row.Item.ActualWidth + 2 * Reach, Math.Max(0, cut) + Reach) };

        foreach (var piece in row.Pieces)
        {
            var top = piece.Margin.Top;
            var openBottom = row.Height - piece.Margin.Bottom;
            var level = piece.Piece.Level;
            double bottom;
            bool rounds;
            if (level >= motion.Level)
            {
                bottom = Math.Min(openBottom, cut);
                var roundedOpen = piece.Piece.Part is CardPart.Bottom or CardPart.Only or CardPart.Hover;
                rounds = level == motion.Level ? bottom < openBottom || roundedOpen : roundedOpen && bottom >= openBottom;
            }
            else
            {
                bottom = Math.Min(openBottom, room - motion.ParentGap.GetValueOrDefault(level) - row.Top);
                rounds = piece.Piece.Part is CardPart.Bottom || (motion.ParentsEnding.Contains(level) && bottom < openBottom);
            }
            piece.Card.Visibility = piece.Shown && bottom > top + 0.5 ? Visibility.Visible : Visibility.Collapsed;
            piece.Card.Margin = new Thickness(piece.Margin.Left, top, piece.Margin.Right, row.Height - bottom);
            var radius = rounds ? CardRadius : 0;
            piece.Card.CornerRadius = new CornerRadius(piece.Corners.TopLeft, piece.Corners.TopRight, radius, radius);
            piece.Card.BorderThickness = new Thickness(piece.Edges.Left, piece.Edges.Top, piece.Edges.Right, rounds ? 1 : 0);
        }
    }

    /// <summary>The folder at once where it was going: its rows as they are, and gone if it closed.</summary>
    private void FinishFolderMotion()
    {
        if (_folderMotion is not { } motion) return;
        _folderMotion = null;
        CompositionTarget.Rendering -= FolderMotion_Rendering;
        foreach (var element in motion.Below) element.Translation = Vector3.Zero;
        foreach (var row in motion.Rows.Prepend(motion.Header))
        {
            foreach (var element in row.Content) element.Clip = null;
            // Its own pieces of the cards back (recycled, it may be another row's by now: the list knows whose).
            IndentFolderRow(row.Item, FolderList.ItemFromContainer(row.Item) ?? row.Row);
        }
        if (!motion.Opening) RefreshFolderRowsForMotion();
    }
}
