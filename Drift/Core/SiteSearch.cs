using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Drift;

/// <summary>
/// A site you can search from the command bar, as in Edge: type "youtube.com" (or "yout"), pick
/// "Search YouTube", and what you type next is searched there.
/// </summary>
/// <param name="Host">The site, without "www.": what's typed is matched against it.</param>
/// <param name="Template">Its search page, with {searchTerms} where the query goes (OpenSearch's form).</param>
public sealed record SiteSearch(string Name, string Host, string Template)
{
    public Uri UrlFor(string query)
    {
        query = query.Trim();
        if (query.Length == 0) return new Uri($"https://{Host}/");
        var url = Template.Replace("{searchTerms}", Uri.EscapeDataString(query));
        // OpenSearch's other parameters: the optional ones left out, the rest given plain values.
        url = SiteSearches.Parameter().Replace(url, m => m.Groups[2].Success ? "" : m.Groups[1].Value switch
        {
            "startPage" or "startIndex" => "1",
            "count" => "20",
            "inputEncoding" or "outputEncoding" => "UTF-8",
            "language" => "*",
            _ => "",
        });
        return new Uri(url);
    }
}

public static partial class SiteSearches
{
    public static readonly IReadOnlyList<SiteSearch> BuiltIn =
    [
        new("YouTube", "youtube.com", "https://www.youtube.com/results?search_query={searchTerms}"),
        new("Google", "google.com", "https://www.google.com/search?q={searchTerms}"),
        new("Google Maps", "maps.google.com", "https://www.google.com/maps/search/{searchTerms}"),
        new("Wikipedia", "wikipedia.org", "https://en.wikipedia.org/w/index.php?search={searchTerms}"),
        new("GitHub", "github.com", "https://github.com/search?q={searchTerms}"),
        new("Reddit", "reddit.com", "https://www.reddit.com/search/?q={searchTerms}"),
        new("Amazon", "amazon.com", "https://www.amazon.com/s?k={searchTerms}"),
        new("X", "x.com", "https://x.com/search?q={searchTerms}"),
        new("Stack Overflow", "stackoverflow.com", "https://stackoverflow.com/search?q={searchTerms}"),
        new("MDN", "developer.mozilla.org", "https://developer.mozilla.org/search?q={searchTerms}"),
        new("npm", "npmjs.com", "https://www.npmjs.com/search?q={searchTerms}"),
        new("Bing", "bing.com", "https://www.bing.com/search?q={searchTerms}"),
        new("DuckDuckGo", "duckduckgo.com", "https://duckduckgo.com/?q={searchTerms}"),
        new("Spotify", "open.spotify.com", "https://open.spotify.com/search/{searchTerms}"),
        new("Apple Music", "music.apple.com", "https://music.apple.com/us/search?term={searchTerms}"),
        new("LinkedIn", "linkedin.com", "https://www.linkedin.com/search/results/all/?keywords={searchTerms}"),
    ];

    /// <summary>
    /// The site what's typed names, if any: its address or the start of it ("yout", "youtube.com",
    /// "https://www.youtube.com/"), or the start of its name. Two letters at least, and nothing
    /// that's already a search or a page on it. Of several, the one you visit most, then the shortest.
    /// </summary>
    public static SiteSearch? Match(string typed, IEnumerable<SiteSearch> sites, Func<string, double> visits)
    {
        var t = typed.Trim().ToLowerInvariant();
        foreach (var scheme in new[] { "https://", "http://" })
            if (t.StartsWith(scheme, StringComparison.Ordinal)) t = t[scheme.Length..];
        if (t.StartsWith("www.", StringComparison.Ordinal)) t = t[4..];
        t = t.TrimEnd('/');
        if (t.Length < 2 || t.Any(c => char.IsWhiteSpace(c) || c is '/' or '?' or '#')) return null;

        return sites
            .Where(s => s.Host.StartsWith(t, StringComparison.Ordinal)
                || s.Name.Replace(" ", "").StartsWith(t, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => visits(s.Host))
            .ThenBy(s => s.Host.Length)
            .FirstOrDefault();
    }

    /// <summary>
    /// "@" and what follows it: the sites whose name or address starts with it (all of them for
    /// nothing), the ones you visit most first, then by name.
    /// </summary>
    public static List<SiteSearch> Filter(string typed, IEnumerable<SiteSearch> sites, Func<string, double> visits)
    {
        var t = typed.Trim().ToLowerInvariant();
        if (t.StartsWith("www.", StringComparison.Ordinal)) t = t[4..];
        return sites
            .Where(s => t.Length == 0 || s.Host.StartsWith(t, StringComparison.Ordinal)
                || s.Name.Replace(" ", "").StartsWith(t.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => visits(s.Host))
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// "@youtube cats": a site named after the "@", then a space, starts searching it for the rest.
    /// Null until there's that space, or if no site goes by that name.
    /// </summary>
    public static (SiteSearch Site, string Query)? ParseAt(string typed, IEnumerable<SiteSearch> sites, Func<string, double> visits)
    {
        if (!typed.StartsWith('@')) return null;
        var space = typed.IndexOf(' ');
        if (space <= 1) return null;
        var site = Filter(typed[1..space], sites, visits).FirstOrDefault();
        return site is null ? null : (site, typed[(space + 1)..].TrimStart());
    }

    /// <summary>The built-in sites, and those learned from your browsing that they don't already have.</summary>
    public static List<SiteSearch> Combine(IEnumerable<SiteSearch> builtIn, IEnumerable<SiteSearch> learned)
    {
        var all = builtIn.ToList();
        foreach (var site in learned)
            if (!all.Any(s => s.Host == site.Host)) all.Add(site);
        return all;
    }

    /// <summary>
    /// A site's OpenSearch description (what a page offers with &lt;link rel="search"&gt;, and how
    /// browsers learn a site's search): its name and its search page. Null unless it has a web page
    /// search with {searchTerms} in it, on the same site as the page that offered it.
    /// </summary>
    public static SiteSearch? FromOpenSearch(string xml, Uri page)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch { return null; }
        var root = doc.Root;
        if (root?.Name.LocalName != "OpenSearchDescription") return null;
        var name = root.Elements().FirstOrDefault(e => e.Name.LocalName == "ShortName")?.Value.Trim();
        var template = root.Elements()
            .Where(e => e.Name.LocalName == "Url"
                && (string?)e.Attribute("type") is "text/html"
                && ((string?)e.Attribute("method") ?? "get").Equals("get", StringComparison.OrdinalIgnoreCase))
            .Select(e => (string?)e.Attribute("template"))
            .FirstOrDefault(t => t is not null && t.Contains("{searchTerms}"));
        if (string.IsNullOrEmpty(name) || template is null) return null;
        if (!Uri.TryCreate(Parameter().Replace(template, "x"), UriKind.Absolute, out var target)
            || target.Scheme is not ("https" or "http")) return null;

        var pageSite = WithoutWww(page.Host);
        var searchSite = WithoutWww(target.Host);
        string? host =
            searchSite == pageSite ? pageSite
            : searchSite.EndsWith("." + pageSite, StringComparison.Ordinal) ? pageSite
            : pageSite.EndsWith("." + searchSite, StringComparison.Ordinal) ? searchSite
            : null;
        return host is null ? null : new SiteSearch(name.Length > 40 ? name[..40] : name, host, template);
    }

    private static string WithoutWww(string host)
    {
        host = host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    // {name} or {name?}, and {prefix:name}.
    [GeneratedRegex(@"\{(?:[\w]+:)?([\w]+)(\?)?\}")]
    internal static partial Regex Parameter();
}
