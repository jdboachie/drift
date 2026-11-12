using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;

namespace Drift;

/// <summary>
/// Searching a site from the command bar, as in Edge: type "youtube.com" (or "yout"), and a
/// "Search YouTube" row comes up; Enter on it puts YouTube in a chip at the start of the field, and
/// what you type then is searched on YouTube. Backspace in the empty field, or Esc, goes back to
/// searching as usual. The sites are a built-in few (SiteSearch.cs) and any you visit that say how
/// to search them (an OpenSearch description, as browsers have always learned them).
/// </summary>
public sealed partial class MainWindow
{
    // The site being searched, while it's in the chip.
    private SiteSearch? _siteSearch;

    /// <summary>
    /// How much you visit a site (and its subdomains), from history: so that of several sites that
    /// fit what's typed, yours comes first. Worked out once per call, when first asked.
    /// </summary>
    private static Func<string, double> SiteVisits()
    {
        Dictionary<string, double>? visits = null;
        return host =>
        {
            if (visits is null)
            {
                visits = new();
                foreach (var entry in History.All)
                    if (Uri.TryCreate(entry.Url, UriKind.Absolute, out var u))
                    {
                        var h = u.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? u.Host[4..] : u.Host;
                        visits[h] = visits.GetValueOrDefault(h) + History.Frecency(entry);
                    }
            }
            return visits.Where(v => v.Key == host || v.Key.EndsWith("." + host, StringComparison.Ordinal)).Sum(v => v.Value);
        };
    }

    private static CommandItem SiteSearchRow(SiteSearch site) => new()
    {
        Kind = CommandKind.SiteSearch,
        Title = $"Search {site.Name}",
        Site = site.Host,
        SiteSearch = site,
    };

    /// <summary>"Search YouTube" for what's typed, if it names a site, ready to go after the first row.</summary>
    private CommandItem? SiteSearchItem(string query)
    {
        if (_siteSearch is not null) return null;
        return SiteSearches.Match(query, SiteSearchStore.All, SiteVisits()) is { } site ? SiteSearchRow(site) : null;
    }

    /// <summary>After "@": the sites you can search whose name starts with what follows it.</summary>
    private List<CommandItem> AtSiteItems(string typed) =>
        SiteSearches.Filter(typed, SiteSearchStore.All, SiteVisits()).Take(CommandRowsMax).Select(SiteSearchRow).ToList();

    /// <param name="query">Already typed for it ("@youtube cats" brings "cats").</param>
    private void EnterSiteSearch(SiteSearch site, string query = "")
    {
        _siteSearch = site;
        _completion = null;
        SiteChipText.Text = site.Name;
        SiteChip.Visibility = Visibility.Visible;
        CommandBox.PlaceholderText = $"Search {site.Name}";
        SetCommandText(query);
        _typed = query;
        CommandBox.Select(query.Length, 0);
        ShowSiteIcon(site);
        UpdateCommandResults();
        CommandBox.Focus(FocusState.Keyboard);
    }

    /// <summary>Back to searching as usual.</summary>
    private void LeaveSiteSearch()
    {
        if (_siteSearch is null) return;
        _siteSearch = null;
        SiteChip.Visibility = Visibility.Collapsed;
        CommandBox.PlaceholderText = "Search or Enter URL...";
        CommandFavicon.Visibility = Visibility.Collapsed;
        CommandGlyph.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// The site's icon, as kept from your visits: under the address its search page is on
    /// ("www.youtube.com", "en.wikipedia.org"), else the site itself, with or without "www.".
    /// </summary>
    internal static async Task<Microsoft.UI.Xaml.Media.ImageSource?> SiteSearchIconAsync(SiteSearch site)
    {
        var hosts = new List<string>();
        if (Uri.TryCreate(SiteSearches.Parameter().Replace(site.Template, "x"), UriKind.Absolute, out var page)) hosts.Add(page.Host);
        hosts.Add(site.Host);
        hosts.Add("www." + site.Host);
        foreach (var host in hosts.Distinct(StringComparer.OrdinalIgnoreCase))
            if (await History.IconAsync(host) is { } icon) return icon;
        return null;
    }

    private async void ShowSiteIcon(SiteSearch site)
    {
        if (await SiteSearchIconAsync(site) is { } icon && _siteSearch == site)
        {
            CommandFavicon.Source = icon;
            CommandFavicon.Visibility = Visibility.Visible;
            CommandGlyph.Visibility = Visibility.Collapsed;
        }
    }
}

/// <summary>
/// The sites you can search: the built-in ones and those learned as you browse, kept in
/// %LOCALAPPDATA%\Drift\site-search.json. A page that offers an OpenSearch description
/// (&lt;link rel="search"&gt;) has it read once a session per site; only a search on that same
/// site is kept.
/// </summary>
internal static class SiteSearchStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Drift", "site-search.json");

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static readonly HashSet<string> Checked = new(StringComparer.OrdinalIgnoreCase);
    private static List<SiteSearch>? _learned;
    private static List<SiteSearch>? _all;

    private const string FindDescription =
        "(() => { const l = document.querySelector('link[rel~=\"search\"][type=\"application/opensearchdescription+xml\"][href]'); return l ? l.href : null; })()";

    public static IReadOnlyList<SiteSearch> All => _all ??= SiteSearches.Combine(SiteSearches.BuiltIn, Learned);

    private static List<SiteSearch> Learned => _learned ??= Load();

    /// <summary>After a page loads: if it says how to search its site, and that's new, learn it.</summary>
    public static async void Notice(CoreWebView2 core)
    {
        try
        {
            if (!Uri.TryCreate(core.Source, UriKind.Absolute, out var page) || page.Scheme != "https") return;
            var host = page.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? page.Host[4..] : page.Host;
            if (All.Any(s => s.Host == host) || !Checked.Add(host)) return;

            var json = await core.ExecuteScriptAsync(FindDescription);
            if (JsonSerializer.Deserialize<string?>(json) is not { } href
                || !Uri.TryCreate(href, UriKind.Absolute, out var description)
                || description.Scheme is not ("https" or "http")) return;

            using var response = await Client.GetAsync(description, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 64 * 1024) return;
            var xml = await response.Content.ReadAsStringAsync();
            if (xml.Length > 64 * 1024 || SiteSearches.FromOpenSearch(xml, page) is not { } site) return;
            if (All.Any(s => s.Host == site.Host)) return;

            Learned.Add(site);
            _all = null;
            Save();
        }
        catch
        {
            // Not this time: the page went, the description didn't load, or it wasn't one.
        }
    }

    private sealed record Stored(string Name, string Host, string Template);

    private static List<SiteSearch> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return (JsonSerializer.Deserialize<List<Stored>>(File.ReadAllText(FilePath)) ?? [])
                    .Where(s => !string.IsNullOrEmpty(s.Host) && s.Template?.Contains("{searchTerms}") == true)
                    .Select(s => new SiteSearch(s.Name, s.Host, s.Template)).ToList();
        }
        catch { }
        return [];
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Learned.Select(s => new Stored(s.Name, s.Host, s.Template)),
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
