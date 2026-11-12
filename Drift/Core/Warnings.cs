using System.Collections.Generic;
using System.Linq;

namespace Drift;

/// <summary>A question before something that can't be undone: its title, what would go, and the button that does it.</summary>
public sealed record Warning(string Title, string Body, string Action);

/// <summary>
/// What Drift asks before the things that can't be taken back (Confirm.cs). Things that can be
/// undone (a bookmark deleted, the tab list cleared) aren't asked about; they offer Undo instead.
/// </summary>
public static class Warnings
{
    /// <summary>Closing a window, if anything would be lost with it; null if nothing would.</summary>
    public static Warning? CloseWindow(bool isPrivate, int downloads, int privateWindows, int droppedTabs, int unsavedPages)
    {
        var lines = new List<string>();
        if (downloads > 0) lines.Add($"{Count(downloads, "download")} will stop.");
        if (privateWindows == 1) lines.Add("Your private window will close.");
        else if (privateWindows > 1) lines.Add($"Your {privateWindows} private windows will close.");
        if (droppedTabs > 0) lines.Add($"{Count(droppedTabs, "tab")} won't be kept.");
        if (unsavedPages > 0)
            lines.Add($"{Count(unsavedPages, "page")} {(unsavedPages == 1 ? "has" : "have")} changes that may not be saved.");
        if (lines.Count == 0) return null;
        return new(isPrivate ? "Close this private window?" : "Close Drift?", string.Join("\n", lines), "Close");
    }

    /// <summary>Closing pages whose sites say they have unsaved changes; null if none do.</summary>
    public static Warning? LeavePages(IReadOnlyList<string> sites) => sites.Count switch
    {
        0 => null,
        1 => new($"Leave {sites[0]}?", "Changes you made may not be saved.", "Leave"),
        _ => new($"Leave {sites.Count} pages?", "Changes you made may not be saved.", "Leave"),
    };

    /// <summary>Clearing what's ticked, from a time range ("Last hour", "All time"); null if nothing is ticked.</summary>
    public static Warning? ClearData(IReadOnlyList<string> kinds, string range)
    {
        if (kinds.Count == 0) return null;
        var names = kinds.Select((k, i) => i == 0 ? k : k.ToLowerInvariant()).ToList();
        // A comma before the last "and": the kinds have "and" in their own names.
        var list = names.Count switch
        {
            1 => names[0],
            2 => $"{names[0]} and {names[1]}",
            _ => string.Join(", ", names.Take(names.Count - 1)) + ", and " + names[^1],
        };
        var when = range.ToLowerInvariant();
        if (when.StartsWith("last ")) when = "the " + when;
        return new("Clear browsing data?", $"{list}, from {when}.", "Clear");
    }

    /// <summary>Resetting every shortcut; null if none were changed.</summary>
    public static Warning? ResetShortcuts(int changed) => changed switch
    {
        <= 0 => null,
        1 => new("Reset all shortcuts?", "The one you changed goes back to how it was.", "Reset"),
        _ => new("Reset all shortcuts?", $"The {changed} you changed go back to how they were.", "Reset"),
    };

    public static Warning RemoveExtension(string name) => new($"Remove “{name}”?", "Its settings go with it.", "Remove");

    private static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";
}
