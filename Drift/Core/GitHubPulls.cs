using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Drift;

/// <summary>An open pull request, as GitHub's search has it.</summary>
public sealed record PullRequest(string Owner, string Repo, int Number, string Title, bool Draft, DateTimeOffset Created)
{
    /// <summary>"owner/repo#12", lower case: what ties a live folder's tab to it.</summary>
    public string Key => GitHubPulls.Key(Owner, Repo, Number);

    public Uri Url => new($"https://{GitHubPulls.Host}/{Owner}/{Repo}/pull/{Number}");
}

/// <summary>A page of GitHub's search: whether it was asked signed in, how many pages there are, and what's on this one.</summary>
public sealed record PullSearch(bool SignedIn, int Pages, List<PullRequest> Items);

/// <summary>
/// The pull requests a GitHub live folder holds (LiveFolders.cs), as in Arc: the open ones you made,
/// are assigned, are asked to review, or are mentioned in. They come from github.com's own search
/// page, which answers in JSON when asked for it, signed in as your tabs are.
/// </summary>
public static partial class GitHubPulls
{
    public const string Host = "github.com";

    /// <summary>Somewhere tiny on GitHub, where its search can be asked as a signed-in page would.</summary>
    public static Uri Base => new($"https://{Host}/robots.txt");

    public static Uri SignIn => new($"https://{Host}/login");

    /// <summary>Your pull requests on GitHub itself.</summary>
    public static Uri Pulls => new($"https://{Host}/pulls");

    /// <summary>Who a search is for: one search each, as GitHub's search has no "or" for these.</summary>
    public static IReadOnlyList<string> Queries { get; } =
        ["author:@me", "assignee:@me", "review-requested:@me", "mentions:@me"];

    /// <summary>At most this many pages of each search (ten a page).</summary>
    public const int MaxPages = 3;

    public static string SearchPath(string who, int page) =>
        "/search?q=" + Uri.EscapeDataString($"is:pr is:open archived:false {who} sort:updated-desc")
        + (page > 1 ? $"&type=pullrequests&p={page}" : "&type=pullrequests");

    public static string Key(string owner, string repo, int number) =>
        $"{owner}/{repo}#{number}".ToLowerInvariant();

    /// <summary>The search's pull requests, open ones only; null if it isn't the search (a sign-in page, an error).</summary>
    public static PullSearch? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object) return null;
            // The page's data sits under its route's name.
            var route = payload.EnumerateObject().Select(p => p.Value)
                .FirstOrDefault(v => v.ValueKind == JsonValueKind.Object && v.TryGetProperty("results", out var r) && r.ValueKind == JsonValueKind.Array);
            if (route.ValueKind != JsonValueKind.Object) return null;
            var signedIn = route.TryGetProperty("logged_in", out var l) && l.ValueKind == JsonValueKind.True;
            var pages = route.TryGetProperty("page_count", out var c) && c.TryGetInt32(out var n) ? n : 1;
            var items = new List<PullRequest>();
            foreach (var r in route.GetProperty("results").EnumerateArray())
            {
                if (Read(r) is { } pr) items.Add(pr);
            }
            return new PullSearch(signedIn, pages, items);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static PullRequest? Read(JsonElement r)
    {
        if (r.ValueKind != JsonValueKind.Object) return null;
        var state = Text(r, "state");
        if (Bool(r, "merged") || state is not ("open" or "draft")) return null;
        if (!r.TryGetProperty("number", out var number) || !number.TryGetInt32(out var n)) return null;
        if (!r.TryGetProperty("repo", out var repo) || !repo.TryGetProperty("repository", out var repository)) return null;
        if (Text(repository, "owner_login") is not { } owner || Text(repository, "name") is not { } name) return null;
        var title = WebUtility.HtmlDecode(Tags().Replace(Text(r, "hl_title") ?? "", "")).Trim();
        var created = DateTimeOffset.TryParse(Text(r, "created"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t)
            ? t : DateTimeOffset.MinValue;
        return new PullRequest(owner, name, n, title.Length > 0 ? title : $"#{n}",
            state == "draft" || Text(r, "reviewable_state") == "draft", created);
    }

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>The searches' pull requests together, each once, newest first.</summary>
    public static List<PullRequest> Merge(IEnumerable<IEnumerable<PullRequest>> lists) =>
        lists.SelectMany(l => l).GroupBy(p => p.Key).Select(g => g.First())
            .OrderByDescending(p => p.Created).ToList();

    /// <summary>The key of the pull request a page is on (its conversation, files, commits…); null for any other page.</summary>
    public static string? KeyOf(Uri? uri) =>
        Parts(uri) is [var owner, var repo, "pull", var number, ..] && int.TryParse(number, out var n) && n > 0
            ? Key(owner, repo, n) : null;

    /// <summary>Going from a repo's comparison page to a pull request in that repo: one was just made there.</summary>
    public static bool MadePullRequest(Uri? from, Uri? to) =>
        Parts(from) is [var owner, var repo, "compare", ..] && KeyOf(to) is { } key
        && key.StartsWith($"{owner}/{repo}#", StringComparison.OrdinalIgnoreCase);

    private static string[]? Parts(Uri? uri) =>
        uri is not null && string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase)
            ? uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray()
            : null;

    /// <summary>"Fix it by jd · Pull Request #3 · jd/drift · GitHub" is "Fix it"; other titles are left as they are.</summary>
    public static string ShortTitle(string title) =>
        PageTitle().Match(title) is { Success: true } m ? m.Groups["title"].Value : title;

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"^(?<title>.+) by \S+ · Pull Request #\d+ · \S+/\S+( · GitHub)?$")]
    private static partial Regex PageTitle();
}
