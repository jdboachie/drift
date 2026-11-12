using Drift;
using Drift.Models;
using Xunit;

namespace Drift.Tests;

public class LiveFolderTests
{
    private static PullRequest Pr(int n, string title = "PR") =>
        new("jd", "drift", n, title, Draft: false, Created: DateTimeOffset.UnixEpoch.AddDays(n));

    private static string Key(int n) => $"jd/drift#{n}";

    [Fact]
    public void New_pull_requests_are_added_in_order()
    {
        var plan = LiveFolder.Reconcile([Key(1)], [Pr(3), Pr(2), Pr(1)], new HashSet<string>(), _ => false);
        Assert.Equal([3, 2], plan.Add.Select(p => p.Number));
        Assert.Empty(plan.Remove);
    }

    [Fact]
    public void Finished_ones_go_unless_kept()
    {
        var plan = LiveFolder.Reconcile([Key(1), Key(2), Key(3)], [Pr(2)], new HashSet<string>(), key => key == Key(3));
        Assert.Equal([Key(1)], plan.Remove);
    }

    [Fact]
    public void Tabs_that_arent_pull_requests_are_left_alone()
    {
        var plan = LiveFolder.Reconcile([null, Key(1)], [Pr(1)], new HashSet<string>(), _ => false);
        Assert.Empty(plan.Remove);
        Assert.Empty(plan.Add);
    }

    [Fact]
    public void Closed_ones_stay_closed_while_theyre_open_on_GitHub()
    {
        var dismissed = new HashSet<string> { Key(2), Key(5) };
        var plan = LiveFolder.Reconcile([], [Pr(2), Pr(1)], dismissed, _ => false);
        Assert.Equal([1], plan.Add.Select(p => p.Number));
        // 5 is finished: if it opens again, it comes back.
        Assert.Equal([Key(2)], plan.Dismissed);
    }

    [Fact]
    public void An_empty_answer_is_believed_the_second_time_running()
    {
        var live = new LiveSource();
        Assert.True(LiveFolder.Believe(live, [Pr(1)]));
        Assert.False(LiveFolder.Believe(live, []));
        Assert.True(LiveFolder.Believe(live, []));
        Assert.True(LiveFolder.Believe(live, []));
        Assert.True(LiveFolder.Believe(live, [Pr(1)]));
        Assert.False(LiveFolder.Believe(live, []));
    }

    [Fact]
    public void Titles_come_from_GitHub()
    {
        var plan = LiveFolder.Reconcile([Key(1)], [Pr(1, "Renamed")], new HashSet<string>(), _ => false);
        Assert.Equal("Renamed", plan.Titles[Key(1)]);
    }
}

public class LiveFolderTreeTests
{
    private sealed class Tab : IFolderTab
    {
        public TabFolder? Folder { get; set; }
        public int Depth { get; set; }
    }

    [Fact]
    public void Nothing_is_dropped_into_a_live_folder()
    {
        var tree = new FolderTree();
        var live = tree.NewFolder("Pull Requests");
        live.Live = new LiveSource();
        var plain = tree.NewFolder("Work");
        var tab = new Tab();
        tree.Add(tab, live);
        var rows = tree.Rows(null);

        Assert.Null(tree.DropAt(rows, 0, live, null));
        Assert.Null(tree.DropAt(rows, 1, null, null));   // just under its header
        Assert.Null(tree.DropAt(rows, 2, null, null));   // after its tab
        Assert.False(tree.CanNest(plain, live));
        Assert.True(tree.CanNest(live, plain));
    }

    [Fact]
    public void A_subfolder_asked_of_a_live_folder_goes_beside_it()
    {
        var tree = new FolderTree();
        var live = tree.NewFolder("Pull Requests");
        live.Live = new LiveSource();
        var sub = tree.NewFolder("New folder", live);
        Assert.Null(sub.Parent);
        Assert.DoesNotContain(sub, live.Children);
    }

    [Fact]
    public void A_live_folder_and_whats_been_closed_in_it_are_saved()
    {
        var tree = new FolderTree();
        var live = tree.NewFolder("Pull Requests");
        live.Live = new LiveSource { Filled = true };
        live.Live.Dismissed.Add("jd/drift#2");
        tree.NewFolder("Work");
        var unfilled = tree.NewFolder("Pull Requests 2");
        unfilled.Live = new LiveSource();

        var saved = tree.Save();
        Assert.Equal(LiveSource.GitHubPulls, saved[0].Live);
        Assert.Equal(["jd/drift#2"], saved[0].Dismissed!);
        Assert.Null(saved[1].Live);
        Assert.Null(saved[1].Dismissed);

        var again = new FolderTree();
        var folders = again.Load(saved);
        var back = folders[live.Id];
        Assert.NotNull(back.Live);
        Assert.Contains("jd/drift#2", back.Live!.Dismissed);
        Assert.True(back.Live.Filled);
        Assert.Null(folders[saved[1].Id].Live);
        Assert.False(folders[unfilled.Id].Live!.Filled);
    }

    [Fact]
    public void An_open_live_folder_with_nothing_in_it_says_so()
    {
        var tree = new FolderTree();
        var live = tree.NewFolder("Pull Requests");
        live.Live = new LiveSource();
        // Not asked yet: nothing to say.
        Assert.Equal([live], tree.Rows(null));

        live.Live.Filled = true;
        var rows = tree.Rows(null);
        Assert.Equal(2, rows.Count);
        var note = Assert.IsType<LiveNote>(rows[1]);
        Assert.Same(live, note.Folder);
        Assert.Equal(1, note.Depth);
        Assert.Equal("No open pull requests", note.Text);
        Assert.False(note.OpensSignIn);
        // The same row each time, so the list keeps it.
        Assert.Same(note, tree.Rows(null)[1]);

        live.IsExpanded = false;
        Assert.Equal([live], tree.Rows(null));
    }

    [Fact]
    public void Signed_out_it_says_so_above_its_tabs()
    {
        var tree = new FolderTree();
        var live = tree.NewFolder("Pull Requests");
        live.Live = new LiveSource { Filled = true };
        var tab = new Tab();
        tree.Add(tab, live);
        Assert.Equal([live, tab], tree.Rows(null));

        live.NeedsSignIn = true;
        var rows = tree.Rows(null);
        Assert.Equal(3, rows.Count);
        var note = Assert.IsType<LiveNote>(rows[1]);
        Assert.Equal("Sign in to GitHub", note.Text);
        Assert.True(note.OpensSignIn);
        Assert.Same(tab, rows[2]);
    }

    [Fact]
    public void Plain_folders_never_say_anything()
    {
        var tree = new FolderTree();
        var plain = tree.NewFolder("Work");
        Assert.Equal([plain], tree.Rows(null));
    }

    [Fact]
    public void Unknown_live_kinds_load_as_plain_folders()
    {
        var again = new FolderTree();
        var folders = again.Load([new SessionFolder { Id = "a", Name = "X", Live = "figma" }]);
        Assert.Null(folders["a"].Live);
    }
}
