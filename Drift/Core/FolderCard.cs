using System.Collections.Generic;

namespace Drift;

/// <summary>
/// Which piece of a folder's card a row draws: the card's top, a slice of its middle, its bottom, or all
/// of it; or, for a closed folder's header, all of it but only while it's hovered.
/// </summary>
public enum CardPart { None, Top, Middle, Bottom, Only, Hover }

/// <summary>
/// One piece of a card in a row: whose card (by the depth of its folder), which piece, and, for a card's
/// bottom, how many cards end at this row from it inward (the room under the row grows by one step each).
/// </summary>
public readonly record struct CardPiece(int Level, CardPart Part, int Rank = 0);

/// <summary>
/// How a row of the folder list sits among the cards: the pieces it draws, how many cards it's inside
/// (each takes it in a little from both sides), and how many cards end under it (each adds room below).
/// </summary>
public sealed record CardRow(IReadOnlyList<CardPiece> Pieces, int Inside, int Ends);

/// <summary>A whole card: whose (its folder), how deep, and where its top and bottom edges are.</summary>
public readonly record struct CardSpan(object Folder, int Level, double Top, double Bottom);

/// <summary>
/// Folders are cards, as Dia's groups are: a folder's header and everything in it on one rounded
/// surface, and a folder inside another is a card of its own on its parent's, so the nesting shows.
/// A closed folder still showing the tab in front under it is a card with that tab; on its own, its
/// header has the card only while it's hovered. The folder list is a flat list of rows, so each row
/// draws its pieces of the cards it's in.
/// </summary>
public static class FolderCard
{
    /// <param name="rows">Each row's depth, whether it's a folder, and if so whether it's open.</param>
    public static IReadOnlyList<CardRow> Layout(IReadOnlyList<(int Depth, bool IsFolder, bool Open)> rows)
    {
        var pieces = new List<CardPiece>[rows.Count];
        var inside = new int[rows.Count];
        var ends = new int[rows.Count];
        for (var i = 0; i < rows.Count; i++) pieces[i] = new List<CardPiece>();

        // Outer cards first, so a row's pieces go from its outermost card in.
        foreach (var level in Levels(rows))
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i] is not { IsFolder: true } folder || folder.Depth != level) continue;
                // The card runs on to the next row no deeper than its folder.
                var end = i + 1;
                while (end < rows.Count && rows[end].Depth > level) end++;
                if (end - i == 1)
                {
                    pieces[i].Add(new CardPiece(level, folder.Open ? CardPart.Only : CardPart.Hover));
                    continue;
                }
                pieces[i].Add(new CardPiece(level, CardPart.Top));
                for (var k = i + 1; k < end; k++) inside[k]++;
                for (var k = i + 1; k < end - 1; k++) pieces[k].Add(new CardPiece(level, CardPart.Middle));
                pieces[end - 1].Add(new CardPiece(level, CardPart.Bottom));
                ends[end - 1]++;
            }

        var result = new CardRow[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            // A bottom's rank: the cards ending here from it inward (it's outside all of them).
            var list = pieces[i];
            for (var p = 0; p < list.Count; p++)
                if (list[p].Part == CardPart.Bottom)
                {
                    var rank = 0;
                    for (var q = p; q < list.Count; q++) if (list[q].Part == CardPart.Bottom) rank++;
                    list[p] = list[p] with { Rank = rank };
                }
            result[i] = new CardRow(list, inside[i], ends[i]);
        }
        return result;
    }

    /// <summary>
    /// Mid-drag, the rows as they're shown and the cards as they'll be once it's dropped (RowDrag.cs):
    /// the dragged rows at their slot among the others, a level as deep as they'd land (a folder's
    /// rows keep theirs under it), or left out without a slot; a row arriving from another list is a
    /// null key. So a card grows and shrinks with its rows, and never goes with a dragged one.
    /// </summary>
    /// <param name="rows">Every row of the list, in order: its key, depth, whether it's a folder, and if so whether it's open.</param>
    /// <param name="slot">Where the dragged rows go among the others (RowPreview.Landing), or null.</param>
    /// <param name="landingDepth">The depth the dragged row (or the folder of a dragged folder's rows) would have there.</param>
    public static List<(object? Key, int Depth, CardRow Cards)> Preview(
        IReadOnlyList<(object Key, int Depth, bool IsFolder, bool Open)> rows, IReadOnlySet<object> dragged,
        int? slot, int landingDepth, bool arriving)
    {
        var lifted = new List<(object Key, int Depth, bool IsFolder, bool Open)>();
        foreach (var row in rows) if (dragged.Contains(row.Key)) lifted.Add(row);
        var by = lifted.Count > 0 ? landingDepth - lifted[0].Depth : 0;

        var shown = new List<(object? Key, int Depth, bool IsFolder, bool Open)>();
        var index = 0;
        void PlaceLifted()
        {
            foreach (var row in lifted) shown.Add(row with { Depth = row.Depth + by });
            if (arriving) shown.Add((null, landingDepth, false, false));
        }
        foreach (var row in rows)
        {
            if (dragged.Contains(row.Key)) continue;
            if (index++ == slot) PlaceLifted();
            shown.Add(row);
        }
        if (slot is { } at && at >= index) PlaceLifted();

        var cards = Layout(shown.ConvertAll(r => (r.Depth, r.IsFolder, r.Open)));
        var result = new List<(object? Key, int Depth, CardRow Cards)>(shown.Count);
        for (var i = 0; i < shown.Count; i++) result.Add((shown[i].Key, shown[i].Depth, cards[i]));
        return result;
    }

    /// <summary>
    /// The cards whole, from where the rows are (mid-drag, where they're shown): from the top of its
    /// folder's header to the bottom of its last row, with the room a card's end has under it. Outer
    /// cards first. Hover pieces (a closed folder's, only while hovered) aren't cards here.
    /// </summary>
    /// <param name="rows">Each row in order: its key (null for one arriving from another list), its top, its
    /// height (without the room around it), and its pieces.</param>
    public static List<CardSpan> Spans(IReadOnlyList<(object? Key, double Top, double Height, CardRow Cards)> rows, double room)
    {
        var spans = new List<CardSpan>();
        var open = new Dictionary<int, (object Folder, double Top)>();
        foreach (var (key, top, height, cards) in rows)
            foreach (var piece in cards.Pieces)
            {
                if (piece.Part is CardPart.Top or CardPart.Only && key is not null) open[piece.Level] = (key, top);
                if (piece.Part is CardPart.Bottom or CardPart.Only && open.Remove(piece.Level, out var start))
                    spans.Add(new CardSpan(start.Folder, piece.Level, start.Top,
                        top + height + (piece.Part == CardPart.Bottom ? piece.Rank * room : 0)));
            }
        spans.Sort((a, b) => a.Level != b.Level ? a.Level.CompareTo(b.Level) : a.Top.CompareTo(b.Top));
        return spans;
    }

    private static SortedSet<int> Levels(IReadOnlyList<(int Depth, bool IsFolder, bool Open)> rows)
    {
        var levels = new SortedSet<int>();
        foreach (var row in rows) if (row.IsFolder) levels.Add(row.Depth);
        return levels;
    }
}
