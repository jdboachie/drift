using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Drift.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.System;
using Windows.UI;

namespace Drift;

/// <summary>
/// The command bar, modelled on Arc's: one centred field for new tabs (Ctrl+T) and for this tab's
/// address (Ctrl+L, or a click on the address pill), with suggestions underneath. Empty, it offers
/// your recent tabs. Typing, it offers in order: the best match (a site you've been to, completed
/// inline as you type, or the URL you typed), the search for what you typed, Google's suggestions,
/// then open tabs and history that match. Open tabs say "Switch to Tab" and go to the tab rather than
/// opening it again. What's typed can also name a command, as in Arc (CommandActions.cs): "dev" for
/// Developer Tools, "pin to read" to pin the tab to Reading List; one whose name you're typing comes
/// before the search. Up/Down move, Enter goes, Esc or a click outside closes.
/// </summary>
public sealed partial class MainWindow
{
    private enum CommandKind { Clipboard, Tab, History, Url, Search, Action, SiteSearch, Bookmark }

    private sealed class CommandItem
    {
        public required CommandKind Kind { get; init; }
        public required string Title { get; init; }
        /// <summary>Shown after the title, dimmed: " — https://…".</summary>
        public string? Detail { get; init; }
        public Uri? Uri { get; init; }
        public BrowserTab? Tab { get; init; }
        /// <summary>Whose icon to show, for history and URLs (host and port).</summary>
        public string? Site { get; init; }
        /// <summary>A suggestion from Google for an earlier query, shown dimmed until this one's arrive.</summary>
        public bool Stale { get; init; }
        /// <summary>A command (CommandActions.cs), for Kind Action.</summary>
        public CommandAction? Action { get; init; }
        /// <summary>The site to search, for Kind SiteSearch (SiteSearchBar.cs).</summary>
        public SiteSearch? SiteSearch { get; init; }
        public string Key => $"{Kind}|{Uri}|{Tab?.GetHashCode()}|{Title}|{Action?.FullName}";
    }

    /// <summary>What one row is drawn with, so the highlight can recolour it.</summary>
    /// <param name="Hint">"Switch to Tab", shown on the highlighted row only, before its Enter key.</param>
    /// <param name="Chip">"Visit from clipboard", or the folder a command is for, always shown.</param>
    /// <param name="Keys">A command's shortcut, at the end.</param>
    /// <param name="Enter">"Enter", at the very end of the highlighted row: what Enter will pick.</param>
    private sealed record CommandRow(Grid Row, TextBlock Text, Run? Detail, FontIcon? Glyph, StackPanel? Hint, Border? Chip, TextBlock? Keys, Border Enter);

    private const int CommandRowsMax = 8;
    private const double CommandRowHeight = 40;
    private const double CommandCardMaxWidth = 720;

    // Google's suggestion service, as Chrome asks it: the "chrome" client also returns sites
    // ("NAVIGATION") alongside searches. Only what's typed into the command bar is sent.
    private static readonly HttpClient SuggestClient = new() { Timeout = TimeSpan.FromSeconds(3) };
    private readonly Dictionary<string, List<(string Text, string? Description, bool IsSite)>> _suggestions = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _suggestCancel;
    // The query whose suggestions arrived last: shown, dimmed, while newer ones load.
    private string? _lastSuggestQuery;

    private bool _commandBarOpen;
    // Enter opens a new tab (Ctrl+T), rather than navigating this one (Ctrl+L).
    private bool _commandNewTab;
    // A new tab opened from a folder's menu goes into that folder.
    private TabFolder? _commandFolder;
    // Ctrl+L: the URL it opened with. While the field still says exactly that, there's nothing to suggest.
    private string? _commandPrefill;
    // What's been typed, without the inline completion after it.
    private string _typed = "";
    // The text the app itself last put in the field, so its TextChanged isn't taken for typing.
    private string? _programmaticText;
    private CommandItem? _completion;
    private List<CommandItem> _commandItems = new();
    private readonly List<CommandRow> _commandRows = new();
    private int _highlight = -1;
    // Moved with the arrow keys or the pointer: kept on the same item when suggestions arrive.
    private bool _highlightMoved;
    // A link on the clipboard when the bar opened, offered first while the field is empty (like Zen).
    private CommandItem? _clipboardItem;
    // Most recently shown first, for the empty command bar.
    private readonly List<BrowserTab> _recentTabs = new();

    /// <param name="newTab">True for a new tab; false to edit the current tab's address.</param>
    /// <param name="animate">
    /// From a click: a quick fade and scale. From the keyboard it just appears, since it's opened
    /// many times a day and any animation there reads as lag.
    /// </param>
    private void OpenCommandBar(bool newTab, bool animate)
    {
        if (_active is null) newTab = true;
        var wasOpen = _commandBarOpen;
        if (wasOpen && newTab == _commandNewTab)
        {
            CommandBox.SelectAll();
            return;
        }

        if (!wasOpen) _clipboardItem = null;
        LeaveAskMode(restoreQuestion: false);
        LeaveSiteSearch();
        _commandFolder = null;
        _commandSplitWith = null;
        CommandBox.PlaceholderText = "Search or Enter URL...";
        _commandBarOpen = true;
        _commandNewTab = newTab;
        _commandPrefill = newTab ? null : _active?.Url;
        _completion = null;
        _highlightMoved = false;
        SetCommandText(_commandPrefill ?? "");
        _typed = CommandBox.Text;

        CommandLayer.Visibility = Visibility.Visible;
        PlaceCommandCard();
        UpdateDragRegions();
        UpdateCommandResults();
        CommandBox.Focus(FocusState.Keyboard);
        CommandBox.SelectAll();
        if (!wasOpen) ReadClipboard();
        if (!wasOpen) WarmAskAi();

        // With Windows' animation effects off, the spring jumps and only the fade is left.
        if (!wasOpen && animate)
        {
            // About the middle of the field, so what you're about to type into barely moves.
            CommandCard.UpdateLayout();
            CommandCard.CenterPoint = new Vector3((float)CommandCard.ActualWidth / 2, 26, 0);
            CommandEnterAnimation.Begin();
            Spring.To(CommandCard, "Scale", Vector3.One, duration: 0.2, bounce: 0, from: new Vector3(0.97f, 0.97f, 1));
        }
    }

    /// <param name="focusPage">Give the keyboard back to the page, unless what's next takes it anyway.</param>
    private void CloseCommandBar(bool focusPage = true)
    {
        if (!_commandBarOpen) return;
        _commandBarOpen = false;
        LeaveAskMode(restoreQuestion: false);
        LeaveSiteSearch();
        _suggestCancel?.Cancel();
        _suggestions.Clear();
        _lastSuggestQuery = null;
        CommandEnterAnimation.Stop();
        Spring.Jump(CommandCard, "Scale", Vector3.One);
        CommandLayer.Visibility = Visibility.Collapsed;
        UpdateDragRegions();
        if (focusPage) _active?.View.Focus(FocusState.Programmatic);
    }

    /// <summary>Centred on the window, a little above the middle, as wide as fits up to 720.</summary>
    private void PlaceCommandCard()
    {
        CommandCard.Width = Math.Max(280, Math.Min(CommandCardMaxWidth, Root.ActualWidth - 48));
        CommandCard.Margin = new Thickness(0, Math.Max(24, Math.Round(Root.ActualHeight * 0.22)), 0, 0);
    }

    private void SetCommandText(string text)
    {
        _programmaticText = text;
        CommandBox.Text = text;
    }

    // ───────────────────────── Typing ─────────────────────────

    private void CommandBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_commandBarOpen) return;
        var text = CommandBox.Text;
        if (_programmaticText is not null && text == _programmaticText)
        {
            _programmaticText = null;
            return;
        }
        _programmaticText = null;
        // Asking a follow-up: nothing to suggest or complete.
        if (_askMode)
        {
            _typed = text;
            return;
        }
        // Searching a site: what's typed is the search, nothing to complete.
        if (_siteSearch is not null)
        {
            _typed = text;
            UpdateCommandResults();
            return;
        }

        // Typing at the end (not deleting, not editing the middle) is what gets completed inline,
        // like Chrome: backspacing over a completion removes it rather than bringing it back.
        var grew = text.Length > _typed.Length
            && text.StartsWith(_typed, StringComparison.OrdinalIgnoreCase)
            && CommandBox.SelectionStart == text.Length;
        _typed = text;
        _completion = null;
        _highlightMoved = false;

        // "@youtube " (with the space): straight into searching YouTube, with whatever follows.
        if (text.StartsWith('@'))
        {
            if (SiteSearches.ParseAt(text, SiteSearchStore.All, SiteVisits()) is var (atSite, atQuery))
            {
                EnterSiteSearch(atSite, atQuery);
                return;
            }
            // Picking a site by name: nothing to complete.
            UpdateCommandResults();
            return;
        }

        if (grew && FindCompletion(text) is { } found)
        {
            var (item, completed) = found;
            var rest = completed[text.Length..];
            SetCommandText(text + rest);
            CommandBox.Select(text.Length, rest.Length);
            _completion = item;
        }
        UpdateCommandResults();
    }

    private void CommandBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_askMode && AskKeyDown(e.Key))
        {
            e.Handled = true;
            return;
        }
        if (_siteSearch is not null && (e.Key == VirtualKey.Escape
            || (e.Key == VirtualKey.Back && CommandBox.Text.Length == 0)))
        {
            // Out of the site's search, back to the usual one.
            e.Handled = true;
            LeaveSiteSearch();
            UpdateCommandResults();
            return;
        }
        switch (e.Key)
        {
            case VirtualKey.Down:
                MoveHighlight(+1);
                e.Handled = true;
                break;
            case VirtualKey.Up:
                MoveHighlight(-1);
                e.Handled = true;
                break;
            case VirtualKey.Enter:
                e.Handled = true;
                ActivateCommand();
                break;
            case VirtualKey.Escape:
                e.Handled = true;
                CloseCommandBar();
                break;
            case VirtualKey.Tab:
                // Sends what's typed to AI (AskAiBar.cs); with that off, takes the completion, like
                // Right. Never moves focus to whatever is under the bar.
                e.Handled = true;
                if (_siteSearch is not null) { }
                // Picking a site after "@": Tab takes the highlighted one, as Enter does.
                else if (CommandQuery.StartsWith('@'))
                {
                    if (_highlight >= 0 && _highlight < _commandItems.Count && _commandItems[_highlight].SiteSearch is { } picked)
                        EnterSiteSearch(picked);
                }
                else if (Settings.Current.AskAiWithTab) AskAi();
                else if (CommandBox.SelectionLength > 0) CommandBox.Select(CommandBox.Text.Length, 0);
                break;
        }
    }

    private void CommandLayer_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Only presses outside the card; the card's own reach here too, on their way up.
        if (!ReferenceEquals(e.OriginalSource, CommandLayer)) return;
        e.Handled = true;
        CloseCommandBar();
    }

    private void AddressPill_Click(object sender, RoutedEventArgs e) => OpenCommandBar(newTab: false, animate: true);

    // ───────────────────────── Results ─────────────────────────

    /// <summary>The query the suggestions are for: what's typed, without the inline completion.</summary>
    private string CommandQuery => (_completion is not null ? _typed : CommandBox.Text).Trim();

    private bool CommandPristine => _commandPrefill is not null && CommandBox.Text == _commandPrefill;

    private void UpdateCommandResults()
    {
        var query = CommandQuery;
        List<CommandItem> items;
        int highlight;
        if (_siteSearch is not null)
        {
            // Enter searches the site; there's nothing else to pick.
            items = new();
            highlight = -1;
        }
        else if (CommandPristine)
        {
            // Ctrl+L on a page: just its address, ready to copy or retype.
            items = new();
            highlight = -1;
        }
        else if (query.StartsWith('@'))
        {
            // "@": the sites you can search, narrowed by what follows.
            items = AtSiteItems(query[1..]);
            highlight = items.Count > 0 ? 0 : -1;
        }
        else if (query.Length == 0)
        {
            // Starting over: the last suggestions have nothing to do with what comes next.
            _lastSuggestQuery = null;
            items = RecentItems();
            highlight = items.Count > 0 ? 0 : -1;
        }
        else
        {
            items = QueryItems(query);
            highlight = 0;
            if (!_suggestions.ContainsKey(query) && !query.Contains("://")) FetchSuggestions(query);
        }
        ShowCommandItems(items, highlight);
        UpdateAskHint();
    }

    /// <summary>Empty: a link you've copied, the tabs you were last on, then the pages you visit most.</summary>
    private List<CommandItem> RecentItems()
    {
        var clipboard = _clipboardItem is null ? new List<CommandItem>() : new List<CommandItem> { _clipboardItem };
        var items = _recentTabs.Where(t => t != _active)
            .Concat(AllTabs.Where(t => t != _active && !_recentTabs.Contains(t)))
            .Take(5)
            .Select(TabItem)
            .ToList();
        var open = OpenUrls();
        items.AddRange(History.All
            .Where(h => !open.Contains(Normalize(h.Url)))
            .OrderByDescending(History.Frecency)
            .Take(Math.Max(0, 6 - items.Count))
            .Select(HistoryItem));
        return clipboard.Concat(items).ToList();
    }

    /// <summary>
    /// If the clipboard holds a single web address (copied link or URL text), offers it. Read once per
    /// opening, on this machine only; the text itself goes nowhere unless you pick the row.
    /// </summary>
    private async void ReadClipboard()
    {
        string? text = null;
        try
        {
            var content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.WebLink))
                text = (await content.GetWebLinkAsync())?.ToString();
            else if (content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
                text = await content.GetTextAsync();
        }
        catch
        {
            // Another app has the clipboard open, or it holds something unreadable.
            return;
        }

        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 2048 || text.Any(char.IsWhiteSpace)) return;
        if (!TryParseAddress(text, out var uri) || uri.Scheme is not ("http" or "https")) return;
        if (!_commandBarOpen) return;

        _clipboardItem = new CommandItem { Kind = CommandKind.Clipboard, Title = text, Uri = uri, Site = uri.Authority };
        // Only while nothing's been typed: it belongs to the empty list.
        if (!CommandPristine && CommandQuery.Length == 0 && !_highlightMoved) UpdateCommandResults();
    }

    private List<CommandItem> QueryItems(string query)
    {
        var tokens = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var open = OpenUrls();

        var matches = new List<(CommandItem Item, double Score)>();
        foreach (var tab in AllTabs)
        {
            var url = tab.Url ?? "";
            var score = MatchScore(tokens, tab.Title, url);
            // An open tab is the likeliest thing you're looking for.
            if (score >= 0) matches.Add((TabItem(tab), score + 8));
        }
        // Bookmarks you're typing the name of come first of all (Bookmarks.cs); an open one is its tab.
        var bookmarked = new HashSet<string>();
        foreach (var bookmark in BookmarkStore.Tree.All().Where(n => !n.IsFolder))
        {
            var score = MatchScore(tokens, bookmark.Title, bookmark.Url!);
            if (score < 0) continue;
            bookmarked.Add(Normalize(bookmark.Url!));
            if (open.Contains(Normalize(bookmark.Url!))) continue;
            matches.Add((BookmarkItem(bookmark), score + 20));
        }
        foreach (var entry in History.All)
        {
            if (open.Contains(Normalize(entry.Url)) || bookmarked.Contains(Normalize(entry.Url))) continue;
            var score = MatchScore(tokens, entry.Title, entry.Url);
            if (score >= 0) matches.Add((HistoryItem(entry), score + History.Frecency(entry)));
        }
        matches.Sort((a, b) => b.Score.CompareTo(a.Score));

        var items = new List<CommandItem>();
        // First, what Enter does: the completed site, else the address typed, else the search.
        if (_completion is not null) items.Add(_completion);
        else if (TryParseAddress(query, out var typedUri))
            items.Add(new CommandItem { Kind = CommandKind.Url, Title = query, Uri = typedUri, Site = typedUri.Authority });
        // "Search YouTube", when what's typed names a site you can search: right after where Enter goes.
        if (SiteSearchItem(query) is { } siteSearch) items.Add(siteSearch);
        // Commands whose names you're typing come before the search ("pin to read"); others after it.
        var actions = MatchingActions(query);
        items.AddRange(actions.Where(a => a.Strong).Select(a => ActionItem(a.Action)));
        items.Add(SearchItem(query));
        items.AddRange(actions.Where(a => !a.Strong).Select(a => ActionItem(a.Action)));

        var (suggestions, stale) = SuggestionsFor(query);
        var suggestionSlots = matches.Count > 0 ? 2 : 5;
        foreach (var (text, description, isSite) in suggestions)
        {
            if (suggestionSlots == 0) break;
            if (isSite)
            {
                if (!Uri.TryCreate(text, UriKind.Absolute, out var site)) continue;
                if (open.Contains(Normalize(text)) || items.Any(i => i.Uri is { } u && Normalize(u.ToString()) == Normalize(text))) continue;
                items.Add(new CommandItem
                {
                    Kind = CommandKind.Url,
                    Title = string.IsNullOrEmpty(description) ? text : description,
                    Detail = string.IsNullOrEmpty(description) ? null : text,
                    Uri = site,
                    Site = site.Authority,
                    Stale = stale,
                });
            }
            else
            {
                if (string.Equals(text, query, StringComparison.OrdinalIgnoreCase)) continue;
                items.Add(new CommandItem { Kind = CommandKind.Search, Title = text, Uri = SearchUri(text), Stale = stale });
            }
            suggestionSlots--;
        }

        foreach (var (item, _) in matches)
        {
            if (items.Count >= CommandRowsMax) break;
            if (items.Any(i => i.Key == item.Key || (i.Tab is not null && i.Tab == item.Tab))) continue;
            items.Add(item);
        }
        return items.Take(CommandRowsMax).ToList();
    }

    /// <summary>
    /// −1 when some word isn't in the title or URL. Otherwise higher for a site that starts with the
    /// first word, and for words that start words in the title.
    /// </summary>
    private static double MatchScore(string[] tokens, string title, string url)
    {
        var lowerTitle = title.ToLowerInvariant();
        var lowerUrl = url.ToLowerInvariant();
        double score = 0;
        foreach (var token in tokens)
        {
            var inTitle = lowerTitle.IndexOf(token, StringComparison.Ordinal);
            var inUrl = lowerUrl.IndexOf(token, StringComparison.Ordinal);
            if (inTitle < 0 && inUrl < 0) return -1;
            if (inTitle == 0 || (inTitle > 0 && !char.IsLetterOrDigit(lowerTitle[inTitle - 1]))) score += 3;
        }
        if (SiteOf(url) is { } site && site.StartsWith(tokens[0], StringComparison.Ordinal)) score += 20;
        return score;
    }

    /// <summary>
    /// The site to complete what's typed to: the host you've used most (open tabs count extra)
    /// that starts with it, like "you" → youtube.com. Its open tab if there is one, else its front page.
    /// </summary>
    private (CommandItem Item, string Completed)? FindCompletion(string typed)
    {
        if (typed.Length == 0 || typed.Contains(' ') || typed.Contains('/')) return null;
        var lower = typed.ToLowerInvariant();

        var scores = new Dictionary<string, (double Score, string Root)>(StringComparer.OrdinalIgnoreCase);
        void Consider(string url, double weight)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return;
            var site = SiteOf(url)!;
            if (site.Length <= lower.Length || !site.StartsWith(lower, StringComparison.Ordinal)) return;
            var root = $"{uri.Scheme}://{uri.Authority}/";
            scores[site] = scores.TryGetValue(site, out var s) ? (s.Score + weight, s.Root) : (weight, root);
        }
        foreach (var entry in History.All) Consider(entry.Url, History.Frecency(entry));
        foreach (var tab in AllTabs) Consider(tab.Url ?? "", 5);
        if (scores.Count == 0) return null;

        var best = scores.OrderByDescending(p => p.Value.Score).ThenBy(p => p.Key.Length).First();
        var completed = typed + best.Key[typed.Length..];
        var tabThere = _recentTabs.Concat(AllTabs)
            .FirstOrDefault(t => string.Equals(SiteOf(t.Url), best.Key, StringComparison.OrdinalIgnoreCase));
        if (tabThere is not null) return (TabItem(tabThere), completed);

        var rootUri = new Uri(best.Value.Root);
        var rootTitle = History.All.FirstOrDefault(h => Normalize(h.Url) == Normalize(best.Value.Root))?.Title;
        return (new CommandItem
        {
            Kind = CommandKind.Url,
            Title = string.IsNullOrWhiteSpace(rootTitle) ? best.Key : rootTitle,
            Detail = string.IsNullOrWhiteSpace(rootTitle) ? null : best.Value.Root,
            Uri = rootUri,
            Site = rootUri.Authority,
        }, completed);
    }

    private CommandItem TabItem(BrowserTab tab)
    {
        var url = tab.Url;
        return new CommandItem
        {
            Kind = CommandKind.Tab,
            Title = tab.Title,
            Detail = string.IsNullOrEmpty(url) || url == "about:blank" ? null : url,
            Tab = tab,
        };
    }

    private static CommandItem BookmarkItem(BookmarkNode bookmark)
    {
        Uri.TryCreate(bookmark.Url, UriKind.Absolute, out var uri);
        return new CommandItem
        {
            Kind = CommandKind.Bookmark,
            Title = bookmark.Title,
            Detail = bookmark.Url,
            Uri = uri,
            Site = uri?.Authority,
        };
    }

    private static CommandItem HistoryItem(HistoryEntry entry)
    {
        Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri);
        var hasTitle = !string.IsNullOrWhiteSpace(entry.Title);
        return new CommandItem
        {
            Kind = CommandKind.History,
            Title = hasTitle ? entry.Title : entry.Url,
            Detail = hasTitle ? entry.Url : null,
            Uri = uri,
            Site = uri?.Authority,
        };
    }

    private static CommandItem ActionItem(CommandAction action) =>
        new() { Kind = CommandKind.Action, Title = action.Name, Action = action };

    private static CommandItem SearchItem(string query) =>
        new() { Kind = CommandKind.Search, Title = query, Uri = SearchUri(query) };

    private HashSet<string> OpenUrls() =>
        AllTabs
            .Select(t => t.Url)
            .Where(u => !string.IsNullOrEmpty(u))
            .Select(u => Normalize(u!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string Normalize(string url) => url.TrimEnd('/');

    /// <summary>The host and port without "www.", as typed: "youtube.com", "localhost:4200".</summary>
    private static string? SiteOf(string? url)
    {
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host)) return null;
        var site = uri.Authority.ToLowerInvariant();
        return site.StartsWith("www.") ? site[4..] : site;
    }

    // ───────────────────────── Google suggestions ─────────────────────────

    /// <summary>
    /// The suggestions for exactly this query once they've arrived. Until then, the last ones that did
    /// arrive, all of them, marked stale: they stay where they were, dimmed, and the new ones replace
    /// them in place, rather than the list shrinking and growing again on every key.
    /// </summary>
    private (List<(string Text, string? Description, bool IsSite)> List, bool Stale) SuggestionsFor(string query)
    {
        if (_suggestions.TryGetValue(query, out var exact)) return (exact, false);
        if (_lastSuggestQuery is not null && _suggestions.TryGetValue(_lastSuggestQuery, out var last)) return (last, true);
        return (new(), false);
    }

    private async void FetchSuggestions(string query)
    {
        _suggestCancel?.Cancel();
        var cancel = _suggestCancel = new CancellationTokenSource();
        try
        {
            var url = "https://suggestqueries.google.com/complete/search?client=chrome&ie=utf-8&oe=utf-8&q="
                + Uri.EscapeDataString(query);
            var json = await SuggestClient.GetStringAsync(url, cancel.Token);
            if (cancel.IsCancellationRequested || !_commandBarOpen) return;
            _suggestions[query] = ParseSuggestions(json);
            _lastSuggestQuery = query;
            if (string.Equals(CommandQuery, query, StringComparison.OrdinalIgnoreCase) && !CommandPristine)
                ShowCommandItems(QueryItems(query), 0);
        }
        catch
        {
            // Offline or slow: local results are already showing.
        }
    }

    /// <summary>[query, [texts], [descriptions], [], {"google:suggesttype": ["QUERY" | "NAVIGATION", …]}]</summary>
    private static List<(string Text, string? Description, bool IsSite)> ParseSuggestions(string json)
    {
        var list = new List<(string, string?, bool)>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 2) return list;
        var texts = root[1];
        var descriptions = root.GetArrayLength() > 2 ? root[2] : default;
        JsonElement types = default;
        if (root.GetArrayLength() > 4 && root[4].ValueKind == JsonValueKind.Object)
            root[4].TryGetProperty("google:suggesttype", out types);
        for (var i = 0; i < texts.GetArrayLength(); i++)
        {
            var text = texts[i].GetString();
            if (string.IsNullOrEmpty(text)) continue;
            var description = descriptions.ValueKind == JsonValueKind.Array && i < descriptions.GetArrayLength()
                ? descriptions[i].GetString() : null;
            var isSite = types.ValueKind == JsonValueKind.Array && i < types.GetArrayLength()
                && types[i].GetString() == "NAVIGATION";
            list.Add((text, description, isSite));
        }
        return list;
    }

    // ───────────────────────── Rows ─────────────────────────

    private void ShowCommandItems(List<CommandItem> items, int defaultHighlight)
    {
        // As many rows as fit below the field in this window.
        var room = Root.ActualHeight - CommandCard.Margin.Top - 52 - 1 - 12 - 16;
        var fit = Math.Max(3, (int)(room / CommandRowHeight));
        if (items.Count > fit) items = items.Take(fit).ToList();

        var keep = _highlightMoved && _highlight >= 0 && _highlight < _commandItems.Count ? _commandItems[_highlight].Key : null;
        _commandItems = items;
        _commandRows.Clear();
        CommandResults.Children.Clear();
        for (var i = 0; i < items.Count; i++) CommandResults.Children.Add(BuildRow(items[i], i));

        var kept = keep is null ? -1 : items.FindIndex(i => i.Key == keep);
        _highlight = kept >= 0 ? kept : items.Count > 0 ? defaultHighlight : -1;
        if (kept < 0) _highlightMoved = false;

        var any = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        CommandResults.Visibility = any;
        CommandDivider.Visibility = any;
        ApplyHighlight();
        UpdateCommandIcon();
    }

    private UIElement BuildRow(CommandItem item, int index)
    {
        var row = new Grid
        {
            Height = CommandRowHeight,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 0, 12, 0),
            ColumnSpacing = 12,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Searches get a magnifier, commands their own icon; everything else its site's icon, with a
        // globe until it loads.
        FontIcon? glyph = new FontIcon
        {
            Glyph = item.Action?.Glyph ?? item.Kind switch { CommandKind.Search or CommandKind.SiteSearch => "\uE721", CommandKind.Clipboard => "\uE77F", _ => "\uE774" },
            FontSize = 14,
        };
        row.Children.Add(glyph);
        if (item.Kind is not (CommandKind.Search or CommandKind.Clipboard or CommandKind.Action))
        {
            var image = new Image { Width = 16, Height = 16 };
            row.Children.Add(image);
            if (item.Tab?.Favicon is { } tabIcon) ShowIcon(image, glyph, tabIcon);
            else if (item.SiteSearch is { } searched) LoadSiteSearchIcon(image, glyph, searched);
            else if (item.Site is { } site) LoadRowIcon(image, glyph, site);
        }

        var text = new TextBlock
        {
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        };
        text.Inlines.Add(new Run { Text = item.Title });
        Run? detail = null;
        if (!string.IsNullOrEmpty(item.Detail))
        {
            detail = new Run { Text = " — " + item.Detail };
            text.Inlines.Add(detail);
        }
        StackPanel? hint = null;
        Border? chip = null;
        TextBlock? keys = null;
        if (item.Action is { } action)
        {
            // "Pin to [Reading List / Work]": the name, then what it's for in a chip, its own folder in bold.
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            text.TextTrimming = TextTrimming.None;
            line.Children.Add(text);
            if (action.Target is { } target)
            {
                var label = new TextBlock { FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                var cut = target.LastIndexOf(" / ", StringComparison.Ordinal);
                if (cut >= 0) label.Inlines.Add(new Run { Text = target[..(cut + 3)] });
                label.Inlines.Add(new Run { Text = cut >= 0 ? target[(cut + 3)..] : target, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                chip = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 3, 8, 4),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = label,
                };
                line.Children.Add(chip);
            }
            Grid.SetColumn(line, 1);
            row.Children.Add(line);
            if (action.ShortcutId is { } id)
            {
                keys = new TextBlock { Text = ShortcutText(id), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(keys, 2);
                row.Children.Add(keys);
            }
        }
        else
        {
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
        }

        if (item.Kind == CommandKind.Bookmark)
        {
            // A star at the end: it's one of your bookmarks.
            keys = new TextBlock
            {
                Text = "",
                FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(keys, 2);
            row.Children.Add(keys);
        }
        if (item.Kind == CommandKind.Clipboard)
        {
            chip = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 3, 8, 4),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = "Visit from clipboard", FontSize = 12 },
            };
            Grid.SetColumn(chip, 2);
            row.Children.Add(chip);
        }
        else if (item.Kind == CommandKind.Tab)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Visibility = Visibility.Collapsed };
            // Then the Enter key, which is what switches.
            panel.Children.Add(new TextBlock { Text = "Switch to Tab", FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(panel, 2);
            row.Children.Add(panel);
            hint = panel;
        }

        // Moving over a row highlights it, as the arrow keys do. PointerMoved rather than
        // PointerEntered, so rows appearing under a still pointer don't steal the highlight.
        row.PointerMoved += (_, _) =>
        {
            if (_highlight == index) return;
            _highlight = index;
            _highlightMoved = true;
            ApplyHighlight();
        };
        row.Tapped += (_, e) =>
        {
            e.Handled = true;
            _highlight = index;
            ActivateCommand();
        };
        NameRow(row, item);

        // The end of the row: whatever's there already, then the Enter key, shown while highlighted.
        var end = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        foreach (var part in row.Children.OfType<FrameworkElement>().Where(c => Grid.GetColumn(c) == 2).ToList())
        {
            row.Children.Remove(part);
            end.Children.Add(part);
        }
        var enter = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 1, 6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Child = new TextBlock { Text = "Enter", FontSize = 12 },
        };
        end.Children.Add(enter);
        Grid.SetColumn(end, 2);
        row.Children.Add(end);

        if (item.Stale) row.Opacity = 0.5;
        _commandRows.Add(new CommandRow(row, text, detail, glyph, hint, chip, keys, enter));
        return row;
    }

    private static void NameRow(FrameworkElement row, CommandItem item) =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(row,
            item.Kind == CommandKind.Tab ? $"Switch to tab: {item.Title}"
            : item.Kind == CommandKind.Clipboard ? $"Visit from clipboard: {item.Title}"
            : item.Kind == CommandKind.Search ? $"Search: {item.Title}"
            : item.Kind == CommandKind.SiteSearch ? item.Title
            : item.Action is { } action ? action.FullName
            : item.Title);

    private static void ShowIcon(Image image, FontIcon glyph, ImageSource icon)
    {
        image.Source = icon;
        glyph.Visibility = Visibility.Collapsed;
    }

    private static async void LoadSiteSearchIcon(Image image, FontIcon glyph, SiteSearch site)
    {
        if (await SiteSearchIconAsync(site) is { } icon) ShowIcon(image, glyph, icon);
    }

    private static async void LoadRowIcon(Image image, FontIcon glyph, string site)
    {
        if (await History.IconAsync(site) is { } icon) ShowIcon(image, glyph, icon);
    }

    /// <summary>
    /// The highlighted row in the Windows accent colour, its text in the colour made to sit on it;
    /// other rows plain. Both follow the system's accent and light/dark mode.
    /// </summary>
    private void ApplyHighlight()
    {
        var accent = ThemeBrush("AccentFillColorDefaultBrush");
        var onAccent = ThemeBrush("TextOnAccentFillColorPrimaryBrush");
        var onAccentDim = onAccent is SolidColorBrush { Color: var c }
            ? new SolidColorBrush(Color.FromArgb(0xB3, c.R, c.G, c.B)) : onAccent;
        var primary = ThemeBrush("TextFillColorPrimaryBrush");
        var secondary = ThemeBrush("DriftSecondaryTextBrush");
        var icon = ThemeBrush("DriftIconBrush");
        var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var chipFill = ThemeBrush("DriftPillBrush");
        var chipText = ThemeBrush("TextFillColorSecondaryBrush");
        var chipOnAccent = onAccent is SolidColorBrush { Color: var a }
            ? new SolidColorBrush(Color.FromArgb(0x33, a.R, a.G, a.B)) : chipFill;

        for (var i = 0; i < _commandRows.Count; i++)
        {
            var (row, text, detail, glyph, hint, chip, keys, enter) = _commandRows[i];
            var on = i == _highlight;
            row.Background = on ? accent : clear;
            text.Foreground = on ? onAccent : primary;
            if (detail is not null) detail.Foreground = on ? onAccentDim : secondary;
            if (glyph is not null) glyph.Foreground = on ? onAccent : icon;
            if (hint is not null)
            {
                hint.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                foreach (var part in hint.Children)
                {
                    if (part is TextBlock t) t.Foreground = onAccentDim;
                    else if (part is FontIcon f) f.Foreground = onAccentDim;
                }
            }
            if (chip is not null)
            {
                chip.Background = on ? chipOnAccent : chipFill;
                ((TextBlock)chip.Child).Foreground = on ? onAccent : chipText;
            }
            if (keys is not null) keys.Foreground = on ? onAccentDim : secondary;
            enter.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            enter.Background = chipOnAccent;
            ((TextBlock)enter.Child).Foreground = onAccent;
        }
    }

    private static Brush ThemeBrush(string key) => (Brush)Application.Current.Resources[key];

    /// <summary>The field shows the completed site's icon, like Arc; otherwise the magnifier.</summary>
    private async void UpdateCommandIcon()
    {
        // Searching a site: its icon stays.
        if (_siteSearch is not null) return;
        ImageSource? icon = null;
        var item = _completion;
        if (item is not null)
            icon = item.Tab?.Favicon ?? (item.Site is { } site ? await History.IconAsync(site) : null);
        // Checked again after the wait: the completion may have changed meanwhile.
        if (icon is not null && _completion == item)
        {
            CommandFavicon.Source = icon;
            CommandFavicon.Visibility = Visibility.Visible;
            CommandGlyph.Visibility = Visibility.Collapsed;
        }
        else
        {
            CommandFavicon.Visibility = Visibility.Collapsed;
            CommandGlyph.Visibility = Visibility.Visible;
        }
    }

    private void MoveHighlight(int delta)
    {
        if (_commandItems.Count == 0) return;
        _highlight = _highlight < 0
            ? (delta > 0 ? 0 : _commandItems.Count - 1)
            : Math.Clamp(_highlight + delta, 0, _commandItems.Count - 1);
        _highlightMoved = true;
        ApplyHighlight();
    }

    // ───────────────────────── Going ─────────────────────────

    /// <summary>Enter, or a click on a row: the highlighted row, else whatever's in the field.</summary>
    private async void ActivateCommand()
    {
        CommandItem? item = _highlight >= 0 && _highlight < _commandItems.Count ? _commandItems[_highlight] : null;
        var splitWith = _commandSplitWith;
        if (item?.Action is { } action)
        {
            CloseCommandBar();
            action.Run();
            return;
        }
        if (item?.Kind == CommandKind.SiteSearch && item.SiteSearch is { } site)
        {
            EnterSiteSearch(site);
            return;
        }
        if (item?.Kind == CommandKind.Tab && item.Tab is { } tab)
        {
            CloseCommandBar(focusPage: false);
            if (splitWith is not null) SplitBeside(splitWith, tab);
            else Activate(tab);
            tab.View.Focus(FocusState.Programmatic);
            return;
        }

        var input = CommandBox.Text.Trim();
        var uri = _siteSearch is { } searching ? searching.UrlFor(input)
            : item?.Uri ?? (input.Length > 0 ? ToUri(input) : null);
        var newTab = _commandNewTab || _active is null;
        // Still there? It may have been deleted while the bar was open.
        var folder = _commandFolder is { } f && _folderTree.AllFolders.Contains(f) ? f : null;
        CloseCommandBar(focusPage: uri is null);
        if (uri is null) return;

        if (splitWith is not null)
        {
            // "Add Split View": the new page on the right of the tab it was for.
            var opened = await CreateTabAsync(uri, TabPlace.Regular, activate: false);
            SplitBeside(splitWith, opened);
            opened.View.Focus(FocusState.Programmatic);
        }
        else if (newTab)
        {
            var opened = folder is not null
                ? await CreateTabAsync(uri, TabPlace.Folder, activate: true, folder: folder)
                : await CreateTabAsync(uri, TabPlace.Regular, activate: true);
            opened.View.Focus(FocusState.Programmatic);
        }
        else
        {
            Navigate(_active!, uri);
            _active!.View.Focus(FocusState.Programmatic);
        }
    }

    /// <summary>Called whenever a tab comes to the front, for the empty command bar's recent list.</summary>
    private void NoteRecentTab(BrowserTab tab)
    {
        _recentTabs.Remove(tab);
        _recentTabs.Insert(0, tab);
    }
}
