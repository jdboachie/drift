using System;

namespace Drift;

/// <summary>What opening a link in Peek does with the Peek there may already be.</summary>
public enum PeekStart
{
    /// <summary>No Peek: a new one grows out of the link.</summary>
    New,
    /// <summary>This tab's Peek is showing: the link loads in it.</summary>
    Navigate,
    /// <summary>Another tab's Peek, out of sight with that tab: it goes, and this tab gets its own.</summary>
    Replace,
}

/// <summary>
/// Alt+click on a link opens it in Peek, from any tab (the page script reports the link; this
/// says whether it's one Peek can show). No UI here, so it can be tested.
/// </summary>
public static class PeekLink
{
    /// <summary>The page to peek at, or null for a link that isn't a web page.</summary>
    public static Uri? Parse(string href) =>
        Uri.TryCreate(href, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri
            : null;

    /// <param name="peekOpen">There's a Peek, showing or left behind with its tab.</param>
    /// <param name="sameOwner">It belongs to the tab the link is opening from.</param>
    public static PeekStart Start(bool peekOpen, bool sameOwner) =>
        !peekOpen ? PeekStart.New : sameOwner ? PeekStart.Navigate : PeekStart.Replace;
}
