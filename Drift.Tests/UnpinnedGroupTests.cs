using System.Text.Json;
using Drift.Models;

namespace Drift.Tests;

// Tab groups are folders that can also sit with your unpinned tabs, as Chrome's groups do and Arc's Tidy Tabs
// sort Today's: one tree, each top-level group pinned or not, the sidebar showing each section's in its own list.
public class UnpinnedGroupTests
{
    private readonly FolderTree _tree = new();

    private static string Show(IReadOnlyList<object> rows) => string.Join(" | ", rows.Select(row => row switch
    {
        TabFolder f => new string(' ', f.Depth * 2) + $"[{f.Name}]",
        FakeTab t => new string(' ', t.Depth * 2) + t.Name,
        _ => "?",
    }));

    [Fact]
    public void A_new_group_is_pinned_unless_asked()
    {
        Assert.True(_tree.NewFolder("Work").IsPinned);
        Assert.False(_tree.NewFolder("Trip", pinned: false).IsPinned);
    }

    [Fact]
    public void A_group_inside_another_goes_with_it()
    {
        var trip = _tree.NewFolder("Trip", pinned: false);
        var hotels = _tree.NewFolder("Hotels", trip);
        Assert.False(hotels.IsPinned);
        _tree.SetPinned(trip, true);
        Assert.True(hotels.IsPinned);
    }

    [Fact]
    public void Each_section_lists_only_its_own_groups()
    {
        var work = _tree.NewFolder("Work");
        _tree.Add(new FakeTab("a"), work);
        var trip = _tree.NewFolder("Trip", pinned: false);
        _tree.Add(new FakeTab("b"), trip);

        Assert.Equal("[Work] |   a", Show(_tree.Rows(null, pinned: true)));
        Assert.Equal("[Trip] |   b", Show(_tree.Rows(null, pinned: false)));
        Assert.Equal("[Work] |   a | [Trip] |   b", Show(_tree.Rows(null)));
    }

    [Fact]
    public void Unpinning_a_group_moves_it_to_the_other_list()
    {
        var work = _tree.NewFolder("Work");
        _tree.Add(new FakeTab("a"), work);
        _tree.SetPinned(work, false);

        Assert.Equal("", Show(_tree.Rows(null, pinned: true)));
        Assert.Equal("[Work] |   a", Show(_tree.Rows(null, pinned: false)));
    }

    // Chrome's groups go when their last tab does; Arc's pinned folders stay, empty.
    [Fact]
    public void An_unpinned_group_with_no_tabs_left_goes()
    {
        var trip = _tree.NewFolder("Trip", pinned: false);
        var a = new FakeTab("a");
        _tree.Add(a, trip);
        var work = _tree.NewFolder("Work");

        _tree.Remove(a);
        var gone = _tree.PruneEmptyGroups();

        Assert.Equal([trip], gone);
        Assert.Equal([work], _tree.Folders);
    }

    [Fact]
    public void An_unpinned_group_with_tabs_only_in_a_group_inside_stays()
    {
        var trip = _tree.NewFolder("Trip", pinned: false);
        var hotels = _tree.NewFolder("Hotels", trip);
        _tree.Add(new FakeTab("a"), hotels);

        Assert.Empty(_tree.PruneEmptyGroups());
        Assert.Equal([trip], _tree.Folders);
    }

    [Fact]
    public void A_group_dragged_to_the_top_of_the_other_list_takes_its_side()
    {
        var work = _tree.NewFolder("Work");
        var trip = _tree.NewFolder("Trip", pinned: false);
        _tree.Add(new FakeTab("b"), trip);

        // Dropped at the end of the unpinned list.
        _tree.MoveFolder(work, null, _tree.Folders.Count, pinned: false);

        Assert.False(work.IsPinned);
        Assert.Equal("[Trip] |   b | [Work]", Show(_tree.Rows(null, pinned: false)));
    }

    [Fact]
    public void A_drop_between_groups_in_one_list_counts_only_that_list()
    {
        var work = _tree.NewFolder("Work");
        var trip = _tree.NewFolder("Trip", pinned: false);
        var home = _tree.NewFolder("Home");
        var news = _tree.NewFolder("News", pinned: false);
        // The unpinned list: [Trip] [News], Trip closed. A group dropped above News lands before it.
        _tree.SetExpanded(trip, false);
        var rows = _tree.Rows(null, pinned: false);
        var drop = _tree.DropAt(rows, 1, null, home);

        Assert.NotNull(drop);
        _tree.MoveFolder(home, drop!.Value.Into, drop.Value.Index, pinned: false);
        Assert.Equal("[Trip] | [Home] | [News]", Show(_tree.Rows(null, pinned: false)));
        Assert.Equal("[Work]", Show(_tree.Rows(null, pinned: true)));
    }

    [Fact]
    public void A_session_keeps_which_groups_are_unpinned()
    {
        var work = _tree.NewFolder("Work");
        var trip = _tree.NewFolder("Trip", pinned: false);
        _tree.NewFolder("Hotels", trip);

        var json = JsonSerializer.Serialize(_tree.Save());
        var back = new FolderTree();
        back.Load(JsonSerializer.Deserialize<List<SessionFolder>>(json));

        Assert.Equal([true, false], back.Folders.Select(f => f.IsPinned));
        Assert.False(back.AllFolders.Single(f => f.Name == "Hotels").IsPinned);
        // A pinned group's saved as before: nothing new in the file.
        Assert.DoesNotContain("Unpinned\":false", json);
    }

    [Fact]
    public void A_folder_from_an_older_file_is_pinned()
    {
        var back = new FolderTree();
        back.Load(JsonSerializer.Deserialize<List<SessionFolder>>("""[{"Id":"w","Name":"Work","Position":0}]"""));
        Assert.True(back.Folders[0].IsPinned);
    }
}
