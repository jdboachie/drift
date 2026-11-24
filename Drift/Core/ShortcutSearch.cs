namespace Drift;

/// <summary>
/// Settings › Shortcuts' search: a shortcut shows when every word typed starts a word of its name, its group or its
/// keys ("reo", "library", "ctrl shift t" or "ctrl+shift+t"), in any case.
/// </summary>
public static class ShortcutSearch
{
    private static readonly char[] Between = [' ', '+', '\t', ',', '-'];

    public static bool Matches(string query, params string[] texts)
    {
        var typed = query.Split(Between, System.StringSplitOptions.RemoveEmptyEntries);
        if (typed.Length == 0) return true;
        var words = string.Join(' ', texts).Split(Between, System.StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in typed)
        {
            var found = false;
            foreach (var candidate in words)
                if (candidate.StartsWith(word, System.StringComparison.OrdinalIgnoreCase)) { found = true; break; }
            if (!found) return false;
        }
        return true;
    }
}
