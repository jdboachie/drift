namespace Drift;

/// <summary>
/// Text too wide for its room loses its middle, as iOS shows a long path: both ends stay ("C:\Users\…\Downloads"),
/// the end getting the odd character, since it usually says which folder it is. As much as fits, by the width
/// <c>measure</c> gives (the text's own font, in the window).
/// </summary>
public static class MiddleTruncate
{
    private const string Ellipsis = "…";

    public static string Fit(string text, System.Func<string, double> measure, double room)
    {
        if (measure(text) <= room) return text;
        // The most characters kept that still fit (fewer kept, never wider).
        int fits = 0, low = 1, high = text.Length - 1;
        while (low <= high)
        {
            var kept = (low + high) / 2;
            if (measure(Keep(text, kept)) <= room) { fits = kept; low = kept + 1; }
            else high = kept - 1;
        }
        return Keep(text, fits);
    }

    private static string Keep(string text, int kept)
    {
        var head = kept / 2;
        return text[..head] + Ellipsis + text[^(kept - head)..];
    }
}
