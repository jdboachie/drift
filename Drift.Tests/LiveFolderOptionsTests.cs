using Drift;
using Drift.Models;
using Xunit;

namespace Drift.Tests;

public class LiveFolderOptionsTests
{
    private static PullRequest Pr(int n, string repo = "drift", bool draft = false) =>
        new("jd", repo, n, "PR", draft, DateTimeOffset.UnixEpoch.AddDays(n));

    [Fact]
    public void Every_search_runs_until_one_is_turned_off()
    {
        var live = new LiveSource();
        Assert.Equal(GitHubPulls.Queries, LiveFolder.Searches(live));
        live.Off.Add("mentions:@me");
        Assert.DoesNotContain("mentions:@me", LiveFolder.Searches(live));
        Assert.Equal(3, LiveFolder.Searches(live).Count);
    }

    [Fact]
    public void Drafts_show_unless_turned_off()
    {
        var live = new LiveSource();
        List<PullRequest> found = [Pr(1), Pr(2, draft: true)];
        Assert.Equal(2, LiveFolder.Shown(live, found).Count);
        live.Drafts = false;
        Assert.Equal([1], LiveFolder.Shown(live, found).Select(p => p.Number));
    }

    [Fact]
    public void A_hidden_repository_s_pull_requests_dont_show()
    {
        var live = new LiveSource();
        live.HiddenRepos.Add("JD/Other");
        Assert.Equal([1], LiveFolder.Shown(live, [Pr(1), Pr(2, repo: "other")]).Select(p => p.Number));
    }

    [Fact]
    public void The_repositories_to_pick_from_are_the_ones_with_pull_requests_each_once_in_order()
    {
        Assert.Equal(["jd/api", "jd/drift"], LiveFolder.Repos([Pr(1), Pr(2, repo: "api"), Pr(3)]));
    }

    [Fact]
    public void When_it_was_last_checked_in_plain_words()
    {
        var now = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("Not checked yet", LiveFolder.Checked(null, now));
        Assert.Equal("Checked just now", LiveFolder.Checked(now.AddSeconds(-40), now));
        Assert.Equal("Checked 1 min ago", LiveFolder.Checked(now.AddSeconds(-70), now));
        Assert.Equal("Checked 12 min ago", LiveFolder.Checked(now.AddMinutes(-12), now));
        Assert.Equal("Checked 2 h ago", LiveFolder.Checked(now.AddMinutes(-130), now));
    }
}
