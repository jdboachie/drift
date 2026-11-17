using System;
using System.Collections.Generic;
using System.Linq;

namespace Drift;

/// <summary>
/// What the icon picker offers, and its search. The symbols are Ionicons (Arc's too), in
/// PickerIcons.Symbols.cs; the emoji, with the words they're found by, are gemoji's, in
/// Assets/emoji.txt. Both are made by build_icons.py (see Assets/README.md).
/// </summary>
public static partial class PickerIcons
{
    /// <summary>Assets/emoji.txt's lines: group, emoji and its words, between tabs.</summary>
    public static List<(string Group, string Icon, string Words)> ParseEmoji(string text)
    {
        var emoji = new List<(string, string, string)>();
        foreach (var line in text.Split('\n'))
        {
            var parts = line.TrimEnd('\r').Split('\t');
            if (parts.Length == 3 && parts[1].Length > 0) emoji.Add((parts[0], parts[1], parts[2]));
        }
        return emoji;
    }

    /// <summary>Every word typed starts one of its words ("cof" finds coffee); typing nothing finds everything.</summary>
    public static bool Matches(string words, string query)
    {
        var typed = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (typed.Length == 0) return true;
        var own = words.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return typed.All(t => own.Any(w => w.StartsWith(t, StringComparison.OrdinalIgnoreCase)));
    }
}
