using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Drift.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;

namespace Drift;

/// <summary>
/// Live folders, as in Arc: a "Pull Requests" folder that keeps itself to your open GitHub pull
/// requests (the ones you made, are assigned, are asked to review, or are mentioned in).
/// <list type="bullet">
/// <item>Made for you the first time you make a pull request (a repo's comparison page, then the
/// pull request), once; or from the menu's New pull requests folder.</item>
/// <item>Asked of GitHub's search every five minutes, in a hidden view signed in as your tabs are
/// (Integrations.cs), and soon after you do something on github.com.</item>
/// <item>New ones go at the top, with a dot until opened; under a closed folder's header they show
/// anyway. Finished ones go, unless they're in front.</item>
/// <item>Its tabs close as pinned tabs do (unloaded, then closed). One you close, or drag out, stays
/// out while it's open on GitHub. Nothing can be put in it.</item>
/// </list>
/// </summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan LiveRefresh = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LiveRetry = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan LiveSoonest = TimeSpan.FromSeconds(60);

    private DispatcherQueueTimer? _liveTimer;
    private DateTimeOffset _liveNextFetch;
    private DateTimeOffset _liveFetched;
    private bool _liveFetching;

    /// <summary>The pull requests folder has been made for you once (session.json), so it isn't again.</summary>
    private bool _pullsFolderMade;

    // While a refresh takes away finished pull requests: you didn't close them, so they aren't kept out.
    private bool _liveRemoving;

    private IEnumerable<TabFolder> LiveFolders => _folderTree.AllFolders.Where(f => f.IsLive);

    private void StartLiveFolders()
    {
        if (_private || _liveTimer is not null) return;
        _liveTimer = DispatcherQueue.CreateTimer();
        _liveTimer.Interval = TimeSpan.FromSeconds(10);
        _liveTimer.Tick += (_, _) => Safely(LiveTick);
        _liveTimer.Start();
        LiveTick();
    }

    private void StopLiveFolders() => _liveTimer?.Stop();

    private void LiveTick()
    {
        if (!LiveFolders.Any())
        {
            CloseHiddenSite(GitHubPulls.Host);
            return;
        }
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized }) return;
        if (DateTimeOffset.Now >= _liveNextFetch && !_liveFetching) _ = FetchPullsAsync();
    }

    /// <summary>A tab went from one page to another: a pull request made, or something done on GitHub.</summary>
    private void NoteLivePage(string? from, string? to)
    {
        if (_private || !Uri.TryCreate(to, UriKind.Absolute, out var now)
            || !string.Equals(now.Host, GitHubPulls.Host, StringComparison.OrdinalIgnoreCase)) return;
        Uri.TryCreate(from, UriKind.Absolute, out var before);
        if (!_pullsFolderMade && !LiveFolders.Any() && GitHubPulls.MadePullRequest(before, now))
        {
            NewPullsFolder();
            return;
        }
        // A review, a merge, signing in: asked again soon.
        _liveNextFetch = Min(_liveNextFetch, _liveFetched + LiveSoonest);
    }

    private TabFolder NewPullsFolder()
    {
        var folder = _folderTree.NewFolder("Pull Requests");
        folder.Live = new LiveSource();
        _pullsFolderMade = true;
        RefreshFolderRows();
        SaveSessionSoon();
        _liveNextFetch = DateTimeOffset.MinValue;
        StartLiveFolders();
        LiveTick();
        return folder;
    }

    private void NewPullsFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!LiveFolders.Any()) NewPullsFolder();
    }

    private void LiveFolderRefresh_Click(object sender, RoutedEventArgs e)
    {
        _liveNextFetch = DateTimeOffset.MinValue;
        LiveTick();
    }

    private void LiveFolderOpenSource_Click(object sender, RoutedEventArgs e) => OpenInNewTab(GitHubPulls.Pulls);

    /// <summary>A pull request's page title, in its live folder: just the pull request's.</summary>
    private static string LiveTitle(BrowserTab tab, string title) =>
        tab.Folder is { IsLive: true } ? GitHubPulls.ShortTitle(title) : title;

    /// <summary>A tab you took out of a live folder (closed, dragged elsewhere) stays out while it's open on GitHub.</summary>
    private void DismissLive(BrowserTab tab)
    {
        if (!_liveRemoving && tab.Folder?.Live is { } live && GitHubPulls.KeyOf(tab.HomeUri) is { } key)
            live.Dismissed.Add(key);
    }

    // ---- Fetching ----

    private async Task FetchPullsAsync()
    {
        _liveFetching = true;
        try
        {
            var core = await HiddenSiteAsync(GitHubPulls.Base);
            var result = await SearchPullsAsync(core);
            if (result is not { } found)
            {
                _liveNextFetch = DateTimeOffset.Now + LiveRetry;
                return;
            }
            var doubted = false;
            foreach (var folder in LiveFolders.ToList())
            {
                folder.NeedsSignIn = !found.SignedIn;
                if (!found.SignedIn) continue;
                if (LiveFolder.Believe(folder.Live!, found.Pulls)) ApplyPulls(folder, found.Pulls);
                else doubted = true;
            }
            // Its note (signed out, or nothing in it) may have changed.
            RefreshFolderRows();
            // An answer with nothing in it is asked again soon before it's believed.
            _liveNextFetch = DateTimeOffset.Now + (doubted ? LiveSoonest : LiveRefresh);
        }
        catch (Exception error)
        {
            App.LogCrash(error);
            _liveNextFetch = DateTimeOffset.Now + LiveRetry;
            CloseHiddenSite(GitHubPulls.Host);
        }
        finally
        {
            _liveFetching = false;
            _liveFetched = DateTimeOffset.Now;
        }
    }

    /// <summary>
    /// Every search's pull requests, together; whether GitHub knew you; null if any search failed,
    /// so a folder is never emptied by a search that didn't answer.
    /// </summary>
    private async Task<(bool SignedIn, List<PullRequest> Pulls)?> SearchPullsAsync(CoreWebView2 core)
    {
        var lists = new List<List<PullRequest>>();
        foreach (var who in GitHubPulls.Queries)
        {
            var list = new List<PullRequest>();
            for (var page = 1; page <= GitHubPulls.MaxPages; page++)
            {
                var json = await SearchPageAsync(core, GitHubPulls.SearchPath(who, page));
                if (json is null || GitHubPulls.Parse(json) is not { } search) return null;
                if (!search.SignedIn) return (false, []);
                list.AddRange(search.Items);
                if (page >= search.Pages) break;
            }
            lists.Add(list);
        }
        return (true, GitHubPulls.Merge(lists));
    }

    /// <summary>A search page's JSON, or null if GitHub doesn't answer.</summary>
    private static async Task<string?> SearchPageAsync(CoreWebView2 core, string path)
    {
        var script = $$"""
            (async () => {
              const stop = new AbortController();
              setTimeout(() => stop.abort(), 15000);
              try {
                const r = await fetch({{JsonSerializer.Serialize(path)}}, { headers: { Accept: 'application/json' }, credentials: 'include', signal: stop.signal });
                return r.ok ? await r.text() : null;
              } catch { return null; }
            })()
            """;
        var reply = await EvaluateAsync(core, script, gesture: false);
        return reply is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;
    }

    // ---- Keeping a folder to the list ----

    private void ApplyPulls(TabFolder folder, List<PullRequest> found)
    {
        if (folder.Live is not { } live) return;
        var tabs = folder.Children.OfType<BrowserTab>().ToList();
        static string? KeyOf(BrowserTab t) => GitHubPulls.KeyOf(t.HomeUri);
        bool Busy(BrowserTab t) => t == _active || t.Split is not null || t == _peekOwner;
        var plan = LiveFolder.Reconcile(tabs.Select(KeyOf).ToList(), found, live.Dismissed,
            key => tabs.Any(t => KeyOf(t) == key && Busy(t)));
        live.Dismissed = plan.Dismissed;

        var remove = new HashSet<string>(plan.Remove, StringComparer.OrdinalIgnoreCase);
        _liveRemoving = true;
        try
        {
            foreach (var tab in tabs.Where(t => KeyOf(t) is { } k && remove.Contains(k))) RemoveTab(tab, remember: false);
        }
        finally
        {
            _liveRemoving = false;
        }
        foreach (var tab in tabs)
            if (tab.IsUnloaded && KeyOf(tab) is { } k && plan.Titles.TryGetValue(k, out var title)) tab.Title = title;

        // The first fill isn't news.
        var quiet = !live.Filled;
        live.Filled = true;
        // Each to the top, last first, so they end up in the list's order.
        for (var i = plan.Add.Count - 1; i >= 0; i--) _ = AddPullAsync(folder, plan.Add[i], isNew: !quiet);
        if (plan.Add.Count > 0 || remove.Count > 0)
        {
            RefreshFolderRows();
            SaveSessionSoon();
        }
    }

    private async Task AddPullAsync(TabFolder folder, PullRequest pull, bool isNew)
    {
        var tab = await CreateTabAsync(pull.Url, TabPlace.Folder, activate: false, deferLoad: true,
            title: pull.Title, index: 0, folder: folder);
        if (!isNew || tab.Folder != folder) return;
        tab.IsNew = true;
        // A closed folder shows it under its header anyway, as Arc peeks it out.
        if (!folder.IsExpanded)
        {
            folder.Lingering.Add(tab);
            RefreshFolderRows();
        }
    }
}
