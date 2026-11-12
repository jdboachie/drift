namespace Drift;

/// <summary>
/// How many Essentials sit side by side: as many as fit across the sidebar without a tile going
/// narrower than it is tall, up to six, so widening the sidebar adds a column at a time (and
/// narrowing takes one away). Never more across than there are tiles; one or two are two across.
/// </summary>
public static class EssentialsGrid
{
    /// <summary>A tile is 46 tall with 4 around it: its cell is square at this width.</summary>
    public const double MinCell = 54;

    /// <summary>The most a row can hold.</summary>
    public const int MostColumns = 6;

    /// <summary>The most across that fit the sidebar's width inside its padding.</summary>
    public static int MostAcross(double width) =>
        System.Math.Clamp((int)System.Math.Floor(width / MinCell), 2, MostColumns);

    public static int Columns(int count, double width) =>
        System.Math.Max(2, System.Math.Min(count, MostAcross(width)));
}
