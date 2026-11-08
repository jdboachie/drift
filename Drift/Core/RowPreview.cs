using System.Collections.Generic;

namespace Drift;

/// <summary>A row of a sidebar list: its key (the item), its height, and whether it's a folder header (something to drop onto).</summary>
public sealed record RowLayout(object Key, double Height, bool IsHeader = false);

/// <param name="Slot">Where it lands among the rows not being dragged.</param>
/// <param name="Gap">The same place in the list's own indexing (the dragged rows still in it).</param>
/// <param name="Onto">A folder header the pointer is in the middle of.</param>
public readonly record struct Landing(int Slot, int Gap, object? Onto);

/// <summary>
/// A drag over a sidebar list, as the rows slide to show it: where a drop would land, and where
/// every row sits meanwhile. Worked out on the rows as they'd be with the dragged ones lifted out,
/// so the rows moving under the pointer don't change the answer. No UI here, so it can be tested.
/// </summary>
public static class RowPreview
{
    /// <param name="y">The pointer, from the top of the list.</param>
    public static Landing Landing(IReadOnlyList<RowLayout> rows, IReadOnlySet<object> dragged, double y)
    {
        var slot = 0;
        var gap = rows.Count;
        object? onto = null;
        var top = 0.0;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (dragged.Contains(row.Key)) continue;
            // The middle half of a header is onto it; its edges are the gaps either side.
            if (row.IsHeader && y > top + row.Height / 4 && y < top + row.Height * 3 / 4) onto = row.Key;
            if (top + row.Height / 2 < y) slot++;
            else if (gap == rows.Count) gap = i;
            top += row.Height;
        }
        return new Landing(slot, gap, onto);
    }

    /// <param name="slot">Where the dragged rows go among the others, or null for nowhere here (lifted out).</param>
    /// <param name="arriving">For a row from another list, the room to make for it.</param>
    public static Dictionary<object, double> Tops(IReadOnlyList<RowLayout> rows, IReadOnlySet<object> dragged,
        int? slot, double arriving = 0)
    {
        var lifted = new List<RowLayout>();
        foreach (var row in rows)
        {
            if (!dragged.Contains(row.Key)) continue;
            lifted.Add(row);
        }

        var tops = new Dictionary<object, double>();
        var top = 0.0;
        var index = 0;
        void PlaceLifted()
        {
            foreach (var row in lifted)
            {
                tops[row.Key] = top;
                top += row.Height;
            }
            top += arriving;
        }
        foreach (var row in rows)
        {
            if (dragged.Contains(row.Key)) continue;
            if (index++ == slot) PlaceLifted();
            tops[row.Key] = top;
            top += row.Height;
        }
        if (slot is { } at && at >= index) PlaceLifted();
        return tops;
    }
}
