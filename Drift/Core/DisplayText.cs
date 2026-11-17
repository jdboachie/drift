namespace Drift;

/// <summary>
/// Text for one line of the interface, cut to a length a line could ever show. Some addresses run to
/// tens of thousands of characters (a sign-in page carrying a whole document in its address), and
/// Windows' text layout fails outright on a run that long, taking Drift down with it.
/// </summary>
public static class DisplayText
{
    /// <summary>Far more than the widest window shows on one line.</summary>
    public const int MaxLength = 1000;

    public static string? Clip(string? text)
    {
        if (text is null || text.Length <= MaxLength) return text;
        var end = MaxLength;
        // Not between the two halves of an emoji or other character outside the basic range.
        if (char.IsHighSurrogate(text[end - 1])) end--;
        return text[..end];
    }
}
