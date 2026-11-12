using Drift;
using Xunit;

namespace Drift.Tests;

public class GitHubPullsTests
{
    // GitHub's search page asked for JSON (trimmed from a real reply).
    private const string Search = """
        {"meta":{"title":"Pull request search results"},"payload":{"blackbirdSearchRoute":{"header_redesign_enabled":false,"results":[
        {"author_name":"hemant","id":"5276463986","repo":{"repository":{"id":142480903,"name":"microsoft-ui-xaml","owner_id":6154722,"owner_login":"microsoft"}},"labels":[],"num_comments":0,"number":12643,"state":"draft","hl_title":"Remove deleted objects&#39; entries from <em>ResourceGraph</em> maps","created":"2026-11-12T16:54:25.000+05:30","reviewable_state":"draft","merged":false},
        {"author_name":"hitesh","id":"5274740888","repo":{"repository":{"id":142480903,"name":"microsoft-ui-xaml","owner_id":6154722,"owner_login":"microsoft"}},"labels":[],"num_comments":1,"number":12642,"state":"open","hl_title":"TableView: API review follow-ups","created":"2026-11-13T09:00:00.000+00:00","reviewable_state":"ready","merged":false},
        {"author_name":"ali","id":"1","repo":{"repository":{"id":1,"name":"drift","owner_login":"jd"}},"number":7,"state":"closed","hl_title":"Old","created":"2026-11-01T00:00:00Z","reviewable_state":"ready","merged":true}
        ],"type":"pullrequests","page":1,"page_count":3,"result_count":25,"logged_in":true}}}
        """;

    [Fact]
    public void Reads_each_open_pull_request()
    {
        var search = GitHubPulls.Parse(Search)!;
        Assert.True(search.SignedIn);
        Assert.Equal(3, search.Pages);
        Assert.Equal(2, search.Items.Count);
        var draft = search.Items[0];
        Assert.Equal("microsoft", draft.Owner);
        Assert.Equal("microsoft-ui-xaml", draft.Repo);
        Assert.Equal(12643, draft.Number);
        Assert.Equal("Remove deleted objects' entries from ResourceGraph maps", draft.Title);
        Assert.True(draft.Draft);
        Assert.False(search.Items[1].Draft);
        Assert.Equal(new Uri("https://github.com/microsoft/microsoft-ui-xaml/pull/12643"), draft.Url);
        Assert.Equal("microsoft/microsoft-ui-xaml#12643", draft.Key);
    }

    [Fact]
    public void Signed_out_is_said_so()
    {
        var search = GitHubPulls.Parse(Search.Replace("\"logged_in\":true", "\"logged_in\":false"))!;
        Assert.False(search.SignedIn);
    }

    [Fact]
    public void Not_the_search_is_nothing()
    {
        Assert.Null(GitHubPulls.Parse("<!DOCTYPE html><html><body>Sign in</body></html>"));
        Assert.Null(GitHubPulls.Parse("""{"payload":{"something":"else"}}"""));
    }

    [Fact]
    public void Merged_lists_have_each_once_newest_first()
    {
        var search = GitHubPulls.Parse(Search)!;
        var merged = GitHubPulls.Merge([search.Items, search.Items.Take(1)]);
        Assert.Equal([12642, 12643], merged.Select(p => p.Number));
    }

    [Fact]
    public void Searches_open_pull_requests_youre_part_of()
    {
        Assert.Equal(4, GitHubPulls.Queries.Count);
        var path = GitHubPulls.SearchPath(GitHubPulls.Queries[2], page: 1);
        Assert.StartsWith("/search?q=", path);
        Assert.EndsWith("&type=pullrequests", path);
        var query = Uri.UnescapeDataString(path["/search?q=".Length..path.IndexOf('&')]);
        Assert.Contains("is:pr", query);
        Assert.Contains("is:open", query);
        Assert.Contains("review-requested:@me", query);
        Assert.EndsWith("&p=2", GitHubPulls.SearchPath(GitHubPulls.Queries[0], page: 2));
    }

    [Theory]
    [InlineData("https://github.com/Microsoft/WinUI/pull/12", "microsoft/winui#12")]
    [InlineData("https://github.com/jd/drift/pull/3/files", "jd/drift#3")]
    [InlineData("https://github.com/jd/drift/pull/3#issuecomment-1", "jd/drift#3")]
    [InlineData("https://github.com/jd/drift/pulls", null)]
    [InlineData("https://github.com/jd/drift/issues/3", null)]
    [InlineData("https://gitlab.com/jd/drift/pull/3", null)]
    public void A_pull_requests_page_has_its_key(string url, string? key) =>
        Assert.Equal(key, GitHubPulls.KeyOf(new Uri(url)));

    [Fact]
    public void Keys_ignore_case()
    {
        var search = GitHubPulls.Parse(Search)!;
        Assert.Equal(search.Items[0].Key, GitHubPulls.KeyOf(new Uri("https://github.com/Microsoft/Microsoft-UI-Xaml/pull/12643")));
    }

    [Theory]
    [InlineData("https://github.com/jd/drift/compare/main...feature", "https://github.com/jd/drift/pull/4", true)]
    [InlineData("https://github.com/jd/drift/compare/main...other:drift:feature?expand=1", "https://github.com/jd/drift/pull/4", true)]
    [InlineData("https://github.com/jd/drift/pull/3", "https://github.com/jd/drift/pull/4", false)]
    [InlineData("https://github.com/jd/drift/compare/main...feature", "https://github.com/jd/other/pull/4", false)]
    [InlineData("https://github.com/jd/drift/compare/main...feature", "https://github.com/jd/drift/compare/main...x", false)]
    public void Going_from_a_comparison_to_its_pull_request_made_one(string from, string to, bool made) =>
        Assert.Equal(made, GitHubPulls.MadePullRequest(new Uri(from), new Uri(to)));

    [Theory]
    [InlineData("Add live folders by jdboachie · Pull Request #12 · jd/drift · GitHub", "Add live folders")]
    [InlineData("Fix · the thing by someone · Pull Request #3 · org/repo", "Fix · the thing")]
    [InlineData("jd/drift: An Arc-style browser", "jd/drift: An Arc-style browser")]
    public void A_pull_requests_page_title_is_shortened_to_its_own(string page, string title) =>
        Assert.Equal(title, GitHubPulls.ShortTitle(page));
}
