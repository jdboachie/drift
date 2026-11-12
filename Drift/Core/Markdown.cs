using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Drift;

public enum MdKind { Paragraph, Heading, Bullet, Numbered, Quote, Code, Rule, Table }

public enum MdAlign { Left, Center, Right }

[Flags]
public enum MdStyle { None = 0, Bold = 1, Italic = 2, Code = 4 }

/// <summary>A stretch of text in one style; with Link, a link to there.</summary>
public sealed record MdSpan(string Text, MdStyle Style = MdStyle.None, string? Link = null);

/// <param name="Level">A heading's level (1–6), or a list item's depth (0 at the margin).</param>
/// <param name="Number">A numbered item's number.</param>
/// <param name="Code">A code block's text, exactly as written.</param>
/// <param name="Rows">A table's rows, its header first: each a cell per column, each cell its spans.</param>
/// <param name="Align">A table's columns' alignment.</param>
public sealed record MdBlock(MdKind Kind, IReadOnlyList<MdSpan> Spans, int Level = 0, int Number = 0,
    string? Language = null, string? Code = null,
    IReadOnlyList<IReadOnlyList<IReadOnlyList<MdSpan>>>? Rows = null, IReadOnlyList<MdAlign>? Align = null);

/// <summary>
/// The Markdown an AI answers in, read into blocks for the command bar to draw: paragraphs (their
/// line breaks kept, as chat answers mean them), headings, lists, quotes, code blocks and rules,
/// with bold, italic, code and links inside. Tables stay as their lines. Made to be read again and
/// again as an answer streams in, so whatever isn't finished yet (an open code block, a lone
/// "**") shows as well as it can rather than waiting.
/// </summary>
public static partial class Markdown
{
    public static List<MdBlock> Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var blocks = new List<MdBlock>();
        var paragraph = new List<string>();
        var quote = new List<string>();

        void Flush()
        {
            if (paragraph.Count > 0) blocks.Add(new MdBlock(MdKind.Paragraph, Inline(string.Join("\n", paragraph))));
            if (quote.Count > 0) blocks.Add(new MdBlock(MdKind.Quote, Inline(string.Join("\n", quote))));
            paragraph.Clear();
            quote.Clear();
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
            {
                Flush();
                var fence = trimmed[..3];
                var language = trimmed[3..].Trim();
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].Trim().StartsWith(fence); i++) code.Add(lines[i]);
                blocks.Add(new MdBlock(MdKind.Code, Array.Empty<MdSpan>(), Language: language.Length > 0 ? language : null,
                    Code: string.Join("\n", code)));
                continue;
            }
            if (trimmed.Length == 0)
            {
                Flush();
                continue;
            }
            // A table: a header row, the line of dashes under it, then rows, while lines have pipes.
            if (trimmed.Contains('|') && i + 1 < lines.Length && TableRule().Match(lines[i + 1].Trim()) is { Success: true }
                && Cells(trimmed) is { Count: > 0 } header && Cells(lines[i + 1].Trim()).Count == header.Count)
            {
                Flush();
                var align = Cells(lines[i + 1].Trim()).Select(c =>
                    c.StartsWith(':') && c.EndsWith(':') ? MdAlign.Center : c.EndsWith(':') ? MdAlign.Right : MdAlign.Left).ToList();
                var rows = new List<IReadOnlyList<IReadOnlyList<MdSpan>>> { header.Select(c => (IReadOnlyList<MdSpan>)Inline(c)).ToList() };
                for (i += 2; i < lines.Length && lines[i].Trim() is { Length: > 0 } row && row.Contains('|'); i++)
                {
                    var cells = Cells(row);
                    rows.Add(Enumerable.Range(0, header.Count)
                        .Select(c => (IReadOnlyList<MdSpan>)(c < cells.Count ? Inline(cells[c]) : new List<MdSpan>())).ToList());
                }
                i--;
                blocks.Add(new MdBlock(MdKind.Table, Array.Empty<MdSpan>(), Rows: rows, Align: align));
                continue;
            }
            if (Heading().Match(line) is { Success: true } heading)
            {
                Flush();
                blocks.Add(new MdBlock(MdKind.Heading, Inline(heading.Groups[2].Value.TrimEnd('#', ' ')), Level: heading.Groups[1].Length));
                continue;
            }
            if (Rule().IsMatch(trimmed))
            {
                Flush();
                blocks.Add(new MdBlock(MdKind.Rule, Array.Empty<MdSpan>()));
                continue;
            }
            if (Bullet().Match(line) is { Success: true } bullet)
            {
                Flush();
                blocks.Add(new MdBlock(MdKind.Bullet, Inline(bullet.Groups[2].Value), Level: Depth(bullet.Groups[1].Value)));
                continue;
            }
            if (Numbered().Match(line) is { Success: true } numbered)
            {
                Flush();
                blocks.Add(new MdBlock(MdKind.Numbered, Inline(numbered.Groups[3].Value), Level: Depth(numbered.Groups[1].Value),
                    Number: int.TryParse(numbered.Groups[2].Value, out var n) ? n : 1));
                continue;
            }
            if (trimmed.StartsWith('>'))
            {
                if (paragraph.Count > 0) Flush();
                quote.Add(trimmed[1..].TrimStart());
                continue;
            }
            if (quote.Count > 0) Flush();
            paragraph.Add(trimmed);
        }
        Flush();
        return blocks;
    }

    /// <summary>
    /// A table row's cells, without the pipes at either end. A pipe inside code, or escaped as
    /// "\|", is part of its cell (the escape's backslash goes).
    /// </summary>
    private static List<string> Cells(string row)
    {
        if (row.StartsWith('|')) row = row[1..];
        if (row.EndsWith('|') && !row.EndsWith("\\|")) row = row[..^1];
        var cells = new List<string>();
        var cell = new StringBuilder();
        var inCode = false;
        for (var i = 0; i < row.Length; i++)
        {
            var c = row[i];
            if (c == '\\' && i + 1 < row.Length && row[i + 1] == '|')
            {
                cell.Append('|');
                i++;
            }
            else if (c == '`')
            {
                inCode = !inCode;
                cell.Append(c);
            }
            else if (c == '|' && !inCode)
            {
                cells.Add(cell.ToString().Trim());
                cell.Clear();
            }
            else cell.Append(c);
        }
        cells.Add(cell.ToString().Trim());
        return cells;
    }

    // Two spaces (or a tab) a level, as models indent nested lists.
    private static int Depth(string indent) => indent.Replace("\t", "  ").Length / 2;

    // ───────────────────────── Inline ─────────────────────────

    public static List<MdSpan> Inline(string text)
    {
        var spans = new List<MdSpan>();
        Inline(text, MdStyle.None, spans);
        return Merge(spans);
    }

    private static void Inline(string text, MdStyle style, List<MdSpan> spans)
    {
        var plain = new StringBuilder();
        void Add(MdSpan span)
        {
            if (plain.Length > 0) spans.Add(new MdSpan(plain.ToString(), style));
            plain.Clear();
            spans.Add(span);
        }

        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length && IsPunctuation(text[i + 1]))
            {
                plain.Append(text[i + 1]);
                i += 2;
                continue;
            }
            if (c == '`')
            {
                var ticks = Run(text, i, '`');
                var close = text.IndexOf(new string('`', ticks), i + ticks, StringComparison.Ordinal);
                if (close > i)
                {
                    var code = text[(i + ticks)..close];
                    if (ticks > 1) code = code.Trim();
                    Add(new MdSpan(code, style | MdStyle.Code));
                    i = close + ticks;
                    continue;
                }
            }
            if (c == '[' && LinkAt(text, i) is { } link)
            {
                var (label, url, end) = link;
                if (plain.Length > 0) spans.Add(new MdSpan(plain.ToString(), style));
                plain.Clear();
                var inner = new List<MdSpan>();
                Inline(label, style, inner);
                foreach (var span in inner) spans.Add(span with { Link = span.Link ?? url });
                i = end;
                continue;
            }
            if (c is 'h' && string.CompareOrdinal(text, i, "http", 0, 4) == 0
                && BareUrl().Match(text, i) is { Success: true } bare && bare.Index == i
                && (i == 0 || !char.IsLetterOrDigit(text[i - 1])))
            {
                var address = bare.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', '\'', '"');
                Add(new MdSpan(address, style, address));
                i += address.Length;
                continue;
            }
            if (c is '*' or '_' && Emphasis(text, i) is { } emphasis)
            {
                var (inner2, markers, after) = emphasis;
                var add = markers >= 2 ? MdStyle.Bold : MdStyle.Italic;
                if (markers == 3) add = MdStyle.Bold | MdStyle.Italic;
                if (plain.Length > 0) spans.Add(new MdSpan(plain.ToString(), style));
                plain.Clear();
                Inline(inner2, style | add, spans);
                i = after;
                continue;
            }
            plain.Append(c);
            i++;
        }
        if (plain.Length > 0) spans.Add(new MdSpan(plain.ToString(), style));
    }

    /// <summary>"[label](url)" at i: the label, the address and where it ends.</summary>
    private static (string Label, string Url, int End)? LinkAt(string text, int i)
    {
        var depth = 0;
        for (var j = i; j < text.Length; j++)
        {
            if (text[j] == '[') depth++;
            else if (text[j] == ']' && --depth == 0)
            {
                if (j + 1 >= text.Length || text[j + 1] != '(') return null;
                var close = text.IndexOf(')', j + 2);
                if (close < 0) return null;
                var url = text[(j + 2)..close].Trim();
                // A title after the address: [x](url "title").
                var space = url.IndexOf(' ');
                if (space > 0) url = url[..space];
                return url.Length == 0 ? null : (text[(i + 1)..j], url, close + 1);
            }
        }
        return null;
    }

    /// <summary>
    /// Emphasis opening at i, if it closes: what's inside, how many markers (1 italic, 2 bold,
    /// 3 both) and where it ends. Underscores only count at the edges of words, so snake_case
    /// stays as it is.
    /// </summary>
    private static (string Inner, int Markers, int After)? Emphasis(string text, int i)
    {
        var marker = text[i];
        var count = Math.Min(3, Run(text, i, marker));
        var start = i + count;
        if (start >= text.Length || char.IsWhiteSpace(text[start])) return null;
        if (marker == '_' && i > 0 && char.IsLetterOrDigit(text[i - 1])) return null;

        var closer = new string(marker, count);
        for (var j = start + 1; j <= text.Length - count; j++)
        {
            if (text[j - 1] == '\\') continue;
            if (string.CompareOrdinal(text, j, closer, 0, count) != 0) continue;
            if (char.IsWhiteSpace(text[j - 1])) continue;
            var after = j + count;
            // "**bold** " closes, but the first two of "***" in "**a***b" don't for a single.
            if (count == 1 && after < text.Length && text[after] == marker) { j++; continue; }
            if (marker == '_' && after < text.Length && char.IsLetterOrDigit(text[after])) continue;
            return (text[start..j], count, after);
        }
        return null;
    }

    private static int Run(string text, int i, char c)
    {
        var n = 0;
        while (i + n < text.Length && text[i + n] == c) n++;
        return n;
    }

    private static bool IsPunctuation(char c) => char.IsPunctuation(c) || char.IsSymbol(c);

    /// <summary>Neighbouring spans alike, as one.</summary>
    private static List<MdSpan> Merge(List<MdSpan> spans)
    {
        var merged = new List<MdSpan>();
        foreach (var span in spans)
        {
            if (span.Text.Length == 0) continue;
            if (merged.Count > 0 && merged[^1] is var last && last.Style == span.Style && last.Link == span.Link)
                merged[^1] = last with { Text = last.Text + span.Text };
            else merged.Add(span);
        }
        return merged;
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.*)$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^(\*\s*\*\s*\*[\s*]*|-\s*-\s*-[\s-]*|_\s*_\s*_[\s_]*)$")]
    private static partial Regex Rule();

    [GeneratedRegex(@"^(\s*)[-*+]\s+(.*)$")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"^(\s*)(\d{1,9})[.)]\s+(.*)$")]
    private static partial Regex Numbered();

    [GeneratedRegex(@"^\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)*\|?$")]
    private static partial Regex TableRule();

    [GeneratedRegex(@"https?://[^\s<>()\[\]]+")]
    private static partial Regex BareUrl();
}
