namespace Drift;

/// <summary>A shortcut as it's shown beside what it does (New Tab's "Ctrl T"), Arc-style: the keys side by side.</summary>
public static class KeyHint
{
    /// <param name="shortcut">The shortcut's name as Settings writes it, "Ctrl+Shift+T".</param>
    public static string From(string? shortcut) => string.IsNullOrEmpty(shortcut) ? "" : shortcut.Replace('+', ' ');
}
