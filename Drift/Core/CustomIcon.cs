namespace Drift;

/// <summary>
/// An icon you chose for a tab or folder: an emoji, or a symbol. Symbols are Drift's own (Ionicons, in
/// Assets/DriftIcons.ttf), or from Segoe Fluent Icons for ones chosen before Drift had its own.
/// </summary>
public static class CustomIcon
{
    /// <summary>A symbol: icon fonts' characters are in Unicode's private use areas, where no emoji is.</summary>
    public static bool IsSymbol(string icon) =>
        (icon.Length == 1 && icon[0] is >= '' and <= '') || IsDriftSymbol(icon);

    /// <summary>One of Drift's own symbols, drawn in its font: past the Segoe ones, in the supplementary private use area.</summary>
    public static bool IsDriftSymbol(string icon) =>
        icon.Length == 2 && char.IsSurrogatePair(icon, 0) && char.ConvertToUtf32(icon, 0) is >= 0xF0000 and <= 0xFFFFD;
}
