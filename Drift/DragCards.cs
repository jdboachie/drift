using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Drift.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Drift;

/// <summary>
/// Mid-drag, the folders' cards are drawn whole, on a layer behind the folder list (FolderCardLayer),
/// rather than as the rows' pieces (Folders.cs). The rows glide to where they're shown (RowDrag.cs), and
/// a card made of their pieces glided with them: the dragged row's piece went through the middle of it,
/// the others' the other way. Here each card is a top, a middle and a bottom that glide on the same
/// spring as the rows, so its edges keep to its header and its last row and it stays one surface: still
/// while a tab moves within its folder, its bottom edge going up or down as one leaves or joins. The
/// rows' pieces are hidden meanwhile, and shown again once the rows are back in their places.
/// </summary>
public sealed partial class MainWindow
{
    // The middle's height at a scale of 1: it's scaled to the card's height, which only stretches its fill
    // (its edges are its sides).
    private const double DragCardBody = 1000;

    private sealed record DragCard(Border Top, Border Body, Border Bottom);

    private readonly Dictionary<object, DragCard> _dragCards = new(ReferenceEqualityComparer.Instance);
    private bool _dragCardsShown;
    private DispatcherQueueTimer? _dragCardsHandBack;

    /// <summary>The cards whole, where the rows are shown mid-drag (or are laid out), gliding there with them.</summary>
    private void ShowDragCards(List<CardSpan> spans)
    {
        _dragCardsHandBack?.Stop();
        if (!_dragCardsShown)
        {
            // From where the rows' pieces draw them now, which then hide.
            _dragCardsShown = true;
            PlaceDragCards(RestingCardSpans(), animate: false);
            ShowRowCardPieces(false);
        }
        PlaceDragCards(spans, animate: true);
    }

    /// <summary>
    /// The drag's over: the rows' pieces draw the cards again. Cancelled, the rows glide back first, and
    /// the cards with them; dropped, the rows are laid out where they were shown, so it's at once.
    /// </summary>
    private void HandBackDragCards(bool animate)
    {
        if (!_dragCardsShown) return;
        if (!animate)
        {
            EndDragCards();
            return;
        }
        PlaceDragCards(RestingCardSpans(), animate: true);
        if (_dragCardsHandBack is null)
        {
            _dragCardsHandBack = DispatcherQueue.CreateTimer();
            _dragCardsHandBack.IsRepeating = false;
            _dragCardsHandBack.Tick += (_, _) => EndDragCards();
        }
        _dragCardsHandBack.Interval = TimeSpan.FromSeconds(SlideSeconds + 0.15);
        _dragCardsHandBack.Stop();
        _dragCardsHandBack.Start();
    }

    private void EndDragCards()
    {
        _dragCardsHandBack?.Stop();
        _dragCardsShown = false;
        FolderCardLayer.Children.Clear();
        _dragCards.Clear();
        ShowRowCardPieces(true);
    }

    private void ShowRowCardPieces(bool shown)
    {
        if (FolderList.ItemsPanelRoot is not { } panel) return;
        foreach (var row in panel.Children)
            foreach (var part in RowParts(row))
                if (part is Border { Tag: CardTag } piece) piece.Opacity = shown ? 1 : 0;

        static IEnumerable<UIElement> RowParts(UIElement row) =>
            row is ListViewItem item && Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(item) > 0
                && Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(item, 0) is Grid root
                ? root.Children
                : [];
    }

    /// <summary>The cards as the rows are laid out, from their pieces' layout (FolderCard).</summary>
    private List<CardSpan> RestingCardSpans()
    {
        var rows = new List<(object? Key, double Top, double Height, CardRow Cards)>();
        foreach (var row in FolderRows)
            if (FolderList.ContainerFromItem(row) is FrameworkElement container && _folderCardRows.TryGetValue(row, out var cards))
                rows.Add((row, container.ActualOffset.Y, container.ActualHeight, cards));
        return FolderCard.Spans(rows, CardRoom);
    }

    /// <param name="headerWas">Where a folder's header was before the rows changed (Slides.cs), for a card
    /// that's new: it grows out of its header from there, as a folder opening does.</param>
    private void PlaceDragCards(List<CardSpan> spans, bool animate, Func<object, double?>? headerWas = null)
    {
        if (FolderList.ItemsPanelRoot is not { } panel) return;
        var origin = panel.TransformToVisual(FolderCardLayer).TransformPoint(new Point(0, 0));
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var span in spans)
        {
            seen.Add(span.Folder);
            var fresh = !_dragCards.TryGetValue(span.Folder, out var card);
            if (fresh) _dragCards[span.Folder] = card = NewDragCard();
            // A card's sides are its folder's header's (Folders.cs).
            var left = origin.X + span.Level * (BrowserTab.IndentStep + CardInset);
            var width = Math.Max(0, panel.ActualWidth - span.Level * (BrowserTab.IndentStep + CardInset) - span.Level * CardInset);
            var top = origin.Y + span.Top;
            var bottom = origin.Y + span.Bottom;
            foreach (var part in new[] { card!.Top, card.Body, card.Bottom })
            {
                part.Width = width;
                // Each card over the one it's in.
                Canvas.SetZIndex(part, span.Level);
            }
            // A card that's new (a folder that wasn't one: it opened, or has the dragged tab under it now)
            // grows out of its header, from where that was.
            if (fresh && animate && FolderList.ContainerFromItem(span.Folder) is FrameworkElement header)
            {
                var from = origin.Y + (headerWas?.Invoke(span.Folder) ?? span.Top);
                Move(card.Top, "Translation", new Vector3((float)left, (float)from, 0), glide: false);
                Move(card.Body, "Translation", new Vector3((float)left, (float)(from + CardRadius), 0), glide: false);
                Move(card.Body, "Scale", new Vector3(1, (float)(Math.Max(0, header.ActualHeight - 2 * CardRadius) / DragCardBody), 1), glide: false);
                Move(card.Bottom, "Translation", new Vector3((float)left, (float)(from + header.ActualHeight - CardRadius), 0), glide: false);
                fresh = false;
            }
            var glide = animate && !fresh;
            Move(card.Top, "Translation", new Vector3((float)left, (float)top, 0), glide);
            Move(card.Body, "Translation", new Vector3((float)left, (float)(top + CardRadius), 0), glide);
            Move(card.Body, "Scale", new Vector3(1, (float)(Math.Max(0, bottom - top - 2 * CardRadius) / DragCardBody), 1), glide);
            Move(card.Bottom, "Translation", new Vector3((float)left, (float)(bottom - CardRadius), 0), glide);
        }
        // Folders that aren't cards any more (a closed one that was showing the dragged tab).
        foreach (var gone in _dragCards.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            var card = _dragCards[gone];
            FolderCardLayer.Children.Remove(card.Top);
            FolderCardLayer.Children.Remove(card.Body);
            FolderCardLayer.Children.Remove(card.Bottom);
            _dragCards.Remove(gone);
        }

        static void Move(UIElement part, string property, Vector3 to, bool glide)
        {
            if (glide) Spring.To(part, property, to, SlideSeconds, SlideBounce);
            else Spring.Jump(part, property, to);
        }
    }

    private DragCard NewDragCard()
    {
        var style = (Style)Application.Current.Resources["FolderCardStyle"];
        Border Part(double height, CornerRadius corners, Thickness edges) => new()
        {
            Style = style,
            Height = height,
            CornerRadius = corners,
            BorderThickness = edges,
            IsHitTestVisible = false,
        };
        var card = new DragCard(
            Part(CardRadius, new CornerRadius(CardRadius, CardRadius, 0, 0), new Thickness(1, 1, 1, 0)),
            Part(DragCardBody, new CornerRadius(0), new Thickness(1, 0, 1, 0)),
            Part(CardRadius, new CornerRadius(0, 0, CardRadius, CardRadius), new Thickness(1, 0, 1, 1)));
        FolderCardLayer.Children.Add(card.Top);
        FolderCardLayer.Children.Add(card.Body);
        FolderCardLayer.Children.Add(card.Bottom);
        return card;
    }

    /// <summary>A row shown mid-drag at another depth (where the dragged one would land) sits in from the side as it will there.</summary>
    private static void IndentShownRow(ListViewItem item, int depth, CardRow cards)
    {
        var left = depth * BrowserTab.IndentStep + cards.Inside * CardInset;
        var right = cards.Inside * CardInset;
        if (item.Margin.Left != left || item.Margin.Right != right)
            item.Margin = new Thickness(left, item.Margin.Top, right, item.Margin.Bottom);
    }
}
