using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Drift.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Drift;

/// <summary>
/// Live folders, as in Arc: a "Pull Requests" folder that keeps itself to your open GitHub pull
/// requests (the ones you made, are assigned, are asked to review, or are mentioned in).
/// <list type="bullet">
/// <item>Made for you the first time you make a pull request (a repo's comparison page, then the
/// pull request), once; or from the menu's New pull requests folder.</item>
/// <item>Asked of GitHub's search in a hidden view signed in as your tabs are (Integrations.cs): every
/// minute while Drift's in front, every five behind, and soon after you do something on github.com
/// (Core/LiveSchedule.cs).</item>
/// <item>New ones go at the top, with a dot until opened; under a closed folder's header they show
/// anyway. Finished ones go, unless they're in front.</item>
/// <item>Its tabs close as pinned tabs do (unloaded, then closed). One you close, or drag out, stays
/// out while it's open on GitHub. Nothing can be put in it.</item>
/// </list>
/// </summary>
public sealed partial class MainWindow
{
    private DispatcherQueueTimer? _liveTimer;
    private readonly LiveSchedule _liveSchedule = new();
    private bool _liveFetching;
    // You asked for this one (Refresh, or the folder just made): its spinner shows while it's asked.
    // The asks every minute or so happen quietly.
    private bool _liveAskedFor;
    // Drift's window is the one in front: asked more often (LiveSchedule).
    private bool _liveInFront = true;

    /// <summary>The pull requests folder has been made for you once (session.json), so it isn't again.</summary>
    private bool _pullsFolderMade;

    // While a refresh takes away finished pull requests: you didn't close them, so they aren't kept out.
    private bool _liveRemoving;

    // In every Profile: one pull requests folder is enough, wherever it is, and it keeps filling while you're elsewhere.
    private IEnumerable<TabFolder> LiveFolders => Profiles.SelectMany(s => s.Tree.AllFolders).Where(f => f.IsLive);

    private void StartLiveFolders()
    {
        if (_private || _liveTimer is not null) return;
        _liveTimer = DispatcherQueue.CreateTimer();
        _liveTimer.Interval = TimeSpan.FromSeconds(10);
        _liveTimer.Tick += (_, _) => Safely(LiveTick);
        _liveTimer.Start();
        Activated += (_, e) =>
        {
            _liveInFront = e.WindowActivationState != WindowActivationState.Deactivated;
            if (!_liveInFront) return;
            _liveSchedule.CameToFront(DateTimeOffset.Now);
            Safely(LiveTick);
        };
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
        if (!_liveFetching && _liveSchedule.Due(DateTimeOffset.Now, _liveInFront)) _ = FetchPullsAsync();
    }

    /// <summary>A tab went from one page to another: a pull request made, or something done on GitHub.</summary>
    private void NoteLivePage(string? from, string? to)
    {
        if (_private || !Uri.TryCreate(to, UriKind.Absolute, out var now)
            || !string.Equals(now.Host, GitHubPulls.Host, StringComparison.OrdinalIgnoreCase)) return;
        Uri.TryCreate(from, UriKind.Absolute, out var before);
        if (GitHubPulls.MadePullRequest(before, now))
        {
            if (!_pullsFolderMade && !LiveFolders.Any()) NewPullsFolder();
            // Asked again as GitHub's search catches up with it.
            _liveSchedule.Acted(DateTimeOffset.Now);
            return;
        }
        // A review, a merge, signing in: asked again soon.
        _liveSchedule.Visited(DateTimeOffset.Now);
    }

    private TabFolder NewPullsFolder()
    {
        var folder = _folderTree.NewFolder("Pull Requests");
        folder.Live = new LiveSource();
        _pullsFolderMade = true;
        RefreshFolderRows();
        SaveSessionSoon();
        _liveAskedFor = true;
        _liveSchedule.Now();
        StartLiveFolders();
        LiveTick();
        return folder;
    }

    internal void NewPullsFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!LiveFolders.Any()) NewPullsFolder();
    }

    private void LiveFolderRefresh_Click(object sender, RoutedEventArgs e)
    {
        // Already being asked (quietly): that ask is the one you're waiting on, so it shows.
        if (_liveFetching) foreach (var folder in LiveFolders) folder.IsFetching = true;
        else _liveAskedFor = true;
        _liveSchedule.Now();
        LiveTick();
    }

    private void LiveFolderOpenSource_Click(object sender, RoutedEventArgs e) => OpenInNewTab(GitHubPulls.Pulls);

    // ---- Its menu: when it was checked, and what it shows ----

    private static readonly (string Query, string Label)[] LiveSearchLabels =
    [
        ("author:@me", "Made by me"),
        ("assignee:@me", "Assigned to me"),
        ("review-requested:@me", "Waiting for my review"),
        ("mentions:@me", "Mentioning me"),
    ];

    /// <summary>A live folder's menu: when it was last checked, and what it shows (Show ›, Repositories ›).</summary>
    private List<MenuFlyoutItemBase> LiveMenuItems(TabFolder folder, LiveSource live)
    {
        var items = new List<MenuFlyoutItemBase>
        {
            new MenuFlyoutItem { Text = LiveFolder.Checked(live.Checked, DateTimeOffset.Now), IsEnabled = false },
        };

        var show = new List<MenuFlyoutItemBase>();
        var on = LiveFolder.Searches(live);
        foreach (var (query, label) in LiveSearchLabels)
        {
            var isOn = on.Contains(query);
            // One search stays on: a folder that asks for nothing would just empty.
            var item = new ToggleMenuFlyoutItem { Text = label, IsChecked = isOn, IsEnabled = !isOn || on.Count > 1 };
            item.Click += (_, _) => ChangeLive(folder, () =>
            {
                if (isOn) live.Off.Add(query);
                else live.Off.Remove(query);
            });
            show.Add(item);
        }
        show.Add(new MenuFlyoutSeparator());
        var drafts = new ToggleMenuFlyoutItem { Text = "Include drafts", IsChecked = live.Drafts };
        drafts.Click += (_, _) => ChangeLive(folder, () => live.Drafts = !live.Drafts);
        show.Add(drafts);
        items.Add(Submenu("Show", show));

        // The ones in the last answer, and any hidden (which aren't in it, being hidden).
        var repos = new List<MenuFlyoutItemBase>();
        var all = live.Repos.Concat(live.HiddenRepos).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var repo in all)
        {
            var shown = !live.HiddenRepos.Contains(repo);
            var item = new ToggleMenuFlyoutItem { Text = repo, IsChecked = shown };
            item.Click += (_, _) => ChangeLive(folder, () =>
            {
                if (shown) live.HiddenRepos.Add(repo);
                else live.HiddenRepos.Remove(repo);
            });
            repos.Add(item);
        }
        items.Add(Submenu("Repositories", repos));
        return items;
    }

    /// <summary>What a live folder shows changed: kept, and it's asked again now.</summary>
    private void ChangeLive(TabFolder folder, Action change)
    {
        change();
        SaveSessionSoon();
        _liveSchedule.Now();
        LiveTick();
    }

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

    // However long GitHub takes, an ask ends within these: a hidden page's script can wait forever
    // (its timers are throttled or frozen while hidden), and while one ask is out no other is made, not
    // even by Refresh, so a stuck one stopped the folder for good.
    private static readonly TimeSpan LiveOpenLimit = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan LiveAskLimit = TimeSpan.FromSeconds(60);

    private async Task FetchPullsAsync()
    {
        _liveFetching = true;
        var why = new List<string>();
        // A spinner on each live folder's header meanwhile, if you asked for it.
        if (_liveAskedFor) foreach (var folder in LiveFolders) folder.IsFetching = true;
        _liveAskedFor = false;
        try
        {
            var core = await Deadline.Within<CoreWebView2?>(HiddenSiteAsync(GitHubPulls.Base), LiveOpenLimit, fallback: null);
            if (core is null)
            {
                LiveLog("no answer: GitHub's page didn't open in time");
                CloseHiddenSite(GitHubPulls.Host);
                _liveSchedule.Fetched(DateTimeOffset.Now, answered: false, doubted: false);
                return;
            }
            var ask = SearchPullsAsync(core, why);
            var result = await Deadline.Within(ask, LiveAskLimit, fallback: null);
            if (!ask.IsCompleted)
            {
                // Given up on: the page's thrown away, so the next ask starts afresh.
                LiveLog($"no answer in {LiveAskLimit.TotalSeconds:0} s ({string.Join("; ", why)})");
                CloseHiddenSite(GitHubPulls.Host);
            }
            if (result is not { } found)
            {
                if (ask.IsCompleted) LiveLog("no answer: " + string.Join("; ", why));
                _liveSchedule.Fetched(DateTimeOffset.Now, answered: false, doubted: false);
                return;
            }
            var doubted = false;
            foreach (var folder in LiveFolders.ToList())
            {
                folder.NeedsSignIn = !found.SignedIn;
                if (!found.SignedIn) continue;
                var live = folder.Live!;
                live.Checked = DateTimeOffset.Now;
                live.Repos = LiveFolder.Repos(found.Pulls);
                if (LiveFolder.Believe(live, found.Pulls, alreadyEmpty: folder.Children.Count == 0)) ApplyPulls(folder, LiveFolder.Shown(live, found.Pulls));
                else doubted = true;
            }
            // Its note (signed out, or nothing in it) may have changed.
            RefreshFolderRows();
            LiveLog(found.SignedIn
                ? $"answered: {found.Pulls.Count} pull requests{(doubted ? ", none: asked again before it's believed" : "")}"
                : "answered: signed out of GitHub");
            // An answer with nothing in it is asked again soon before it's believed.
            _liveSchedule.Fetched(DateTimeOffset.Now, answered: true, doubted);
        }
        catch (Exception error)
        {
            LiveLog($"failed: {error.GetType().Name} ({string.Join("; ", why)})");
            App.LogCrash(error);
            _liveSchedule.Fetched(DateTimeOffset.Now, answered: false, doubted: false);
            CloseHiddenSite(GitHubPulls.Host);
        }
        finally
        {
            _liveFetching = false;
            foreach (var folder in _folderTree.AllFolders) folder.IsFetching = false;
        }
    }

    /// <summary>
    /// Every search's pull requests, together; whether GitHub knew you; null if any search failed,
    /// so a folder is never emptied by a search that didn't answer.
    /// </summary>
    /// <param name="why">What happened, a line for each page asked, for the log (LiveLog).</param>
    private async Task<(bool SignedIn, List<PullRequest> Pulls)?> SearchPullsAsync(CoreWebView2 core, List<string> why)
    {
        var lists = new List<List<PullRequest>>();
        // The searches its menu has on (one live folder at most: NewPullsFolder).
        var searches = LiveFolders.FirstOrDefault()?.Live is { } live ? LiveFolder.Searches(live) : GitHubPulls.Queries;
        foreach (var who in searches)
        {
            var list = new List<PullRequest>();
            for (var page = 1; page <= GitHubPulls.MaxPages; page++)
            {
                var (json, status) = await SearchPageAsync(core, GitHubPulls.SearchPath(who, page));
                if (json is null)
                {
                    why.Add($"{who} page {page}: {status}");
                    return null;
                }
                if (GitHubPulls.Parse(json) is not { } search)
                {
                    why.Add($"{who} page {page}: not a search ({json.Length} characters)");
                    return null;
                }
                if (!search.SignedIn) return (false, []);
                list.AddRange(search.Items);
                if (page >= search.Pages) break;
            }
            lists.Add(list);
        }
        return (true, GitHubPulls.Merge(lists));
    }

    /// <summary>A search page's JSON, or null and what happened instead (an HTTP status, an error).</summary>
    private static async Task<(string? Json, string Status)> SearchPageAsync(CoreWebView2 core, string path)
    {
        var script = $$"""
            (async () => {
              const stop = new AbortController();
              setTimeout(() => stop.abort(), 15000);
              try {
                const r = await fetch({{JsonSerializer.Serialize(path)}}, { headers: { Accept: 'application/json' }, credentials: 'include', signal: stop.signal });
                return { status: r.status, text: r.ok ? await r.text() : null };
              } catch (e) { return { error: String((e && e.name) || e) }; }
            })()
            """;
        var reply = await EvaluateAsync(core, script, gesture: false);
        if (reply is not { ValueKind: JsonValueKind.Object } answer) return (null, "no reply from the page");
        if (answer.TryGetProperty("error", out var error)) return (null, "error " + error.GetString());
        var status = answer.TryGetProperty("status", out var s) && s.TryGetInt32(out var code) ? code : 0;
        return answer.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String
            ? (text.GetString(), $"HTTP {status}")
            : (null, $"HTTP {status}");
    }

    /// <summary>
    /// How each ask went, in live.log beside the session (what, how many, an HTTP status; never what the
    /// pull requests are), to tell why a folder isn't changing. Kept to its last couple of hundred lines.
    /// </summary>
    private static void LiveLog(string line)
    {
        try
        {
            var folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Drift");
            var path = System.IO.Path.Combine(folder, "live.log");
            System.IO.File.AppendAllText(path, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
            if (new System.IO.FileInfo(path).Length > 64 * 1024)
                System.IO.File.WriteAllLines(path, System.IO.File.ReadAllLines(path).TakeLast(200));
        }
        catch
        {
            // Nowhere to write it.
        }
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
