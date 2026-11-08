using System;
using System.Collections.Generic;
using System.Linq;

namespace Drift;

/// <summary>
/// Whether what's typed in the command bar names one of its commands ("dev" → Developer Tools,
/// "pin to read" → Pin to Reading List), and how well. No UI here, so it can be tested.
/// </summary>
public static class CommandMatch
{
    /// <summary>
    /// −1 unless every word typed starts a word of the name or its keywords (two letters at least
    /// in all). Higher when it starts the name itself, and the more of the name is typed.
    /// </summary>
    public static double Score(string query, string name, IReadOnlyCollection<string>? keywords = null)
    {
        var tokens = Words(query);
        if (tokens.Count == 0 || tokens.Sum(t => t.Length) < 2) return -1;
        var nameWords = Words(name);
        var words = nameWords.Concat(keywords?.SelectMany(Words) ?? []).ToList();
        foreach (var token in tokens)
            if (!words.Any(w => w.StartsWith(token, StringComparison.Ordinal))) return -1;

        double score = 10;
        if (IsStrong(tokens, nameWords)) score += 10;
        var typed = tokens.Sum(t => t.Length);
        var named = Math.Max(1, nameWords.Sum(w => w.Length));
        score += 5.0 * Math.Min(1, (double)typed / named);
        return score;
    }

    /// <summary>
    /// What's typed starts the name: its first word starts the name's first word, and the rest match
    /// ("pin to" for Pin to …, "new doc" for New Google Doc).
    /// </summary>
    public static bool IsStrong(string query, string name, IReadOnlyCollection<string>? keywords = null) =>
        Score(query, name, keywords) >= 0 && IsStrong(Words(query), Words(name));

    private static bool IsStrong(List<string> tokens, List<string> nameWords) =>
        tokens.Count > 0 && nameWords.Count > 0 && nameWords[0].StartsWith(tokens[0], StringComparison.Ordinal);

    private static List<string> Words(string text)
    {
        var words = new List<string>();
        var word = new System.Text.StringBuilder();
        foreach (var c in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) word.Append(c);
            else if (word.Length > 0) { words.Add(word.ToString()); word.Clear(); }
        }
        if (word.Length > 0) words.Add(word.ToString());
        return words;
    }
}
