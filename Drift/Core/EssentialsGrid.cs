namespace Drift;

/// <summary>
/// How many Essentials sit side by side, for how many there are: up to four across, the rows kept as
/// even as they can be (five and six three across, seven and eight four, nine three by three); one or
/// two, two across.
/// </summary>
public static class EssentialsGrid
{
    public const int MaxColumns = 4;

    public static int Columns(int count)
    {
        if (count <= 2) return 2;
        var rows = (count + MaxColumns - 1) / MaxColumns;
        return (count + rows - 1) / rows;
    }
}
