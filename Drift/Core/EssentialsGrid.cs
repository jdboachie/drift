namespace Drift;

/// <summary>
/// How many Essentials sit side by side: as many as fit across the sidebar without a tile going
/// narrower than it is tall, up to six, so widening the sidebar adds a column at a time (and
/// narrowing takes one away). Never more across than there are tiles; one or two are two across.
/// Every row fills the sidebar's width: a full row's tiles share it evenly, and so do a short last
/// row's, which come out wider (five across three: three, then two of half the width each).
/// </summary>
public static class EssentialsGrid
{
    /// <summary>A tile is 48 tall with 5 around it: its cell is square at this width.</summary>
    public const double MinCell = 58;

    /// <summary>A row: a 48-tall tile and 5 above and below it.</summary>
    public const double RowHeight = 58;

    /// <summary>The most a row can hold.</summary>
    public const int MostColumns = 6;

    /// <summary>Where a tile's cell is, its 5 around included: its left, its top and its width.</summary>
    public readonly record struct Cell(double X, double Y, double Width);

    /// <summary>The most across that fit the sidebar's width inside its padding.</summary>
    public static int MostAcross(double width) =>
        System.Math.Clamp((int)System.Math.Floor(width / MinCell), 2, MostColumns);

    public static int Columns(int count, double width) =>
        System.Math.Max(2, System.Math.Min(count, MostAcross(width)));

    public static int Rows(int count, int columns) => (count + columns - 1) / columns;

    /// <summary>How many tiles share row <paramref name="row"/>: the columns, or fewer on the last.</summary>
    private static int InRow(int row, int count, int columns) =>
        System.Math.Clamp(count - row * columns, 1, columns);

    /// <summary>The cell of the tile at <paramref name="slot"/>, of <paramref name="count"/>, across <paramref name="width"/>.</summary>
    public static Cell CellOf(int slot, int count, int columns, double width)
    {
        var row = slot / columns;
        var across = width / InRow(row, count, columns);
        return new Cell(slot % columns * across, row * RowHeight, across);
    }

    /// <summary>The slot whose cell is under a point (or nearest it, from outside the grid).</summary>
    public static int SlotAt(double x, double y, int count, int columns, double width)
    {
        if (count <= 0) return 0;
        var row = System.Math.Clamp((int)System.Math.Floor(y / RowHeight), 0, Rows(count, columns) - 1);
        var inRow = InRow(row, count, columns);
        var column = System.Math.Clamp((int)System.Math.Floor(x / (width / inRow)), 0, inRow - 1);
        return row * columns + column;
    }
}
