using System;

namespace Drift;

/// <summary>
/// The site settings panel's ad blocking switch. uBlock Origin Lite keeps a filtering level per
/// site (0 none, 1 basic, 2 optimal, 3 complete), and sites at its default level aren't listed;
/// this says which site the switch is for and which level each position means.
/// </summary>
public static class AdBlockSite
{
    // uBlock Origin Lite's own default.
    private const int Optimal = 2;

    /// <summary>
    /// The site the switch applies to: the page's host without "www.", so the site's other
    /// subdomains (where its sign-in or embeds may run) are covered too. Null for pages that
    /// aren't websites.
    /// </summary>
    public static string? HostFor(Uri? page)
    {
        if (page is null || (page.Scheme != Uri.UriSchemeHttp && page.Scheme != Uri.UriSchemeHttps) || page.Host.Length == 0) return null;
        return page.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? page.Host[4..] : page.Host;
    }

    public static bool IsOn(int level) => level > 0;

    /// <param name="defaultLevel">The blocker's level for sites not listed.</param>
    public static int LevelFor(bool on, int defaultLevel) => !on ? 0 : defaultLevel > 0 ? defaultLevel : Optimal;
}
