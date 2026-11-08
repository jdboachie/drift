using Drift.Models;

namespace Drift.Tests;

/// <summary>A tab as the folder logic sees it: no page, just a name for the assertions.</summary>
internal sealed class FakeTab(string name) : IFolderTab
{
    public string Name { get; } = name;
    public TabFolder? Folder { get; set; }
    public int Depth { get; set; }
    public override string ToString() => Name;
}

public class FolderTreeTests
{
    private readonly FolderTree _tree = new();

    private string Rows(IFolderTab? active = null) => Show(_tree, active);

    /// <summary>A tree's rows as text: folders as "[Name]", tabs by name, each indented two spaces per level.</summary>
    private static string Show(FolderTree tree, IFolderTab? active = null) => string.Join(" | ", tree.Rows(active).Select(row => row switch
    {
        TabFolder f => new string(' ', f.Depth * 2) + $"[{f.Name}]",
        FakeTab t => new string(' ', t.Depth * 2) + t.Name,
        _ => "?",
    }));

    [Fact]
    public void An_open_folder_lists_its_tabs_one_level_in()
    {
        var work = _tree.NewFolder("Work");
        _tree.Add(new FakeTab("a"), work);
        _tree.Add(new FakeTab("b"), work);

        Assert.Equal("[Work] |   a |   b", Rows());
    }

    [Fact]
    public void A_closed_folder_shows_only_the_tab_in_front()
    {
        var work = _tree.NewFolder("Work");
        var a = new FakeTab("a");
        _tree.Add(a, work);
        _tree.Add(new FakeTab("b"), work);
        work.IsExpanded = false;

        Assert.Equal("[Work]", Rows());
        Assert.Equal("[Work] |   a", Rows(active: a));
    }

    [Fact]
    public void A_tab_opened_from_a_closed_folder_stays_under_it_after_switching_away()
    {
        var work = _tree.NewFolder("Work");
        var a = new FakeTab("a");
        var b = new FakeTab("b");
        _tree.Add(a, work);
        _tree.Add(b, work);
        work.IsExpanded = false;

        _tree.Shown(a);
        _tree.Shown(b);

        Assert.Equal("[Work] |   a |   b", Rows(active: null));
    }

    [Fact]
    public void Clicking_a_closed_folder_tucks_its_tabs_away_then_opens_it()
    {
        var work = _tree.NewFolder("Work");
        var a = new FakeTab("a");
        var b = new FakeTab("b");
        _tree.Add(a, work);
        _tree.Add(b, work);
        work.IsExpanded = false;
        _tree.Shown(a);
        _tree.Shown(b);

        _tree.Toggle(work, active: b);
        Assert.False(work.IsExpanded);
        Assert.Equal("[Work] |   b", Rows(active: b));

        _tree.Toggle(work, active: b);
        Assert.True(work.IsExpanded);
        Assert.Equal("[Work] |   a |   b", Rows(active: b));
    }

    [Fact]
    public void Opening_an_already_open_folder_keeps_tabs_showing_under_its_closed_subfolders()
    {
        var work = _tree.NewFolder("Work");
        var docs = _tree.NewFolder("Docs", parent: work);
        var a = new FakeTab("a");
        _tree.Add(a, docs);
        docs.IsExpanded = false;
        _tree.Shown(a);

        _tree.SetExpanded(work, true);

        Assert.Equal("[Work] |   [Docs] |     a", Rows());
    }

    [Fact]
    public void An_unloaded_tab_no_longer_shows_under_a_closed_folder()
    {
        var work = _tree.NewFolder("Work");
        var a = new FakeTab("a");
        _tree.Add(a, work);
        work.IsExpanded = false;
        _tree.Shown(a);

        _tree.Unloaded(a);

        Assert.Equal("[Work]", Rows());
    }

    [Fact]
    public void A_tab_deep_in_a_closed_folder_shows_just_under_that_folder()
    {
        var work = _tree.NewFolder("Work");
        var docs = _tree.NewFolder("Docs", parent: work);
        var a = new FakeTab("a");
        _tree.Add(a, docs);
        work.IsExpanded = false;

        Assert.Equal("[Work] |   a", Rows(active: a));
    }

    [Fact]
    public void Folders_nest_four_levels_deep_and_no_further()
    {
        var one = _tree.NewFolder("1");
        var two = _tree.NewFolder("2", parent: one);
        var three = _tree.NewFolder("3", parent: two);
        var four = _tree.NewFolder("4", parent: three);

        var five = _tree.NewFolder("5", parent: four);

        Assert.Equal(three, five.Parent);
        Assert.Equal("[1] |   [2] |     [3] |       [4] |       [5]", Rows());
    }

    [Fact]
    public void A_folder_cant_go_inside_itself_or_too_deep()
    {
        var one = _tree.NewFolder("1");
        var two = _tree.NewFolder("2", parent: one);
        var three = _tree.NewFolder("3", parent: two);
        var other = _tree.NewFolder("other");
        _tree.NewFolder("other's", parent: other);

        Assert.False(_tree.CanNest(one, one));
        Assert.False(_tree.CanNest(one, three));
        Assert.True(_tree.CanNest(other, two));   // other ends up 4 levels deep: fine
        Assert.False(_tree.CanNest(other, three)); // its subfolder would be a fifth level
        Assert.True(_tree.CanNest(three, null));  // back to the top
    }

    [Fact]
    public void Moving_a_folder_down_among_its_siblings_lands_where_dropped()
    {
        var a = _tree.NewFolder("a");
        _tree.NewFolder("b");
        _tree.NewFolder("c");

        // Dropped in the gap after b (index 2 counts a itself, which leaves first).
        _tree.MoveFolder(a, into: null, index: 2);

        Assert.Equal("[b] | [a] | [c]", Rows());
    }

    [Fact]
    public void Moving_a_folder_into_a_closed_one_opens_it()
    {
        var a = _tree.NewFolder("a");
        var b = _tree.NewFolder("b");
        b.IsExpanded = false;

        _tree.MoveFolder(a, into: b, index: 0);

        Assert.Equal("[b] |   [a]", Rows());
    }

    [Fact]
    public void Moving_a_tab_down_within_its_folder_lands_where_dropped()
    {
        var work = _tree.NewFolder("Work");
        var a = new FakeTab("a");
        _tree.Add(a, work);
        _tree.Add(new FakeTab("b"), work);
        _tree.Add(new FakeTab("c"), work);

        _tree.Add(a, work, index: 2);

        Assert.Equal("[Work] |   b |   a |   c", Rows());
    }

    [Fact]
    public void Moving_a_tab_to_another_folder_takes_it_out_of_the_first()
    {
        var work = _tree.NewFolder("Work");
        var home = _tree.NewFolder("Home");
        var a = new FakeTab("a");
        _tree.Add(a, work);

        _tree.Add(a, home, index: 0);

        Assert.Equal("[Work] | [Home] |   a", Rows());
        Assert.Equal(home, a.Folder);
    }

    [Fact]
    public void A_removed_tab_leaves_its_folder()
    {
        var work = _tree.NewFolder("Work");
        var a = new FakeTab("a");
        _tree.Add(a, work);

        _tree.Remove(a);

        Assert.Equal("[Work]", Rows());
        Assert.Null(a.Folder);
    }

    [Fact]
    public void Deleting_a_folder_but_keeping_its_tabs_moves_them_up_into_its_place()
    {
        var work = _tree.NewFolder("Work");
        _tree.Add(new FakeTab("x"), work);
        var docs = _tree.NewFolder("Docs", parent: work);
        _tree.Add(new FakeTab("a"), docs);
        _tree.NewFolder("Old", parent: docs);
        _tree.Add(new FakeTab("y"), work);

        var homeless = _tree.Delete(docs, keepTabs: true);

        Assert.Empty(homeless);
        Assert.Equal("[Work] |   x |   a |   [Old] |   y", Rows());
    }

    [Fact]
    public void Deleting_a_top_level_folder_but_keeping_its_tabs_hands_the_tabs_back()
    {
        var work = _tree.NewFolder("Work");
        var a = new FakeTab("a");
        _tree.Add(a, work);
        _tree.NewFolder("Docs", parent: work);
        _tree.NewFolder("Home");

        var homeless = _tree.Delete(work, keepTabs: true);

        // The tabs have no folder to go to (they become pinned tabs); subfolders move to the top.
        Assert.Equal(new IFolderTab[] { a }, homeless);
        Assert.Null(a.Folder);
        Assert.Equal("[Docs] | [Home]", Rows());
    }

    [Fact]
    public void Deleting_a_folder_with_its_tabs_removes_everything_in_it()
    {
        var work = _tree.NewFolder("Work");
        var a = new FakeTab("a");
        var b = new FakeTab("b");
        _tree.Add(a, work);
        var docs = _tree.NewFolder("Docs", parent: work);
        _tree.Add(b, docs);

        var closing = _tree.Delete(work, keepTabs: false);

        Assert.Equal(new IFolderTab[] { a, b }, closing);
        Assert.Equal("", Rows());
        Assert.Empty(_tree.AllTabs);
    }

    // ── Where a drag lands. Rows (Arc's rules): ──
    //   0 [Work]        open
    //   1   x
    //   2   [Docs]      closed
    //   3 [Home]        open, empty
    //   gap n = just above row n; gap 4 = the end.

    private (TabFolder Work, TabFolder Docs, TabFolder Home, FakeTab X, IReadOnlyList<object> Rows) DropScene()
    {
        var work = _tree.NewFolder("Work");
        var x = new FakeTab("x");
        _tree.Add(x, work);
        var docs = _tree.NewFolder("Docs", parent: work);
        docs.IsExpanded = false;
        var home = _tree.NewFolder("Home");
        return (work, docs, home, x, _tree.Rows(null));
    }

    [Fact]
    public void A_drop_just_under_an_open_folders_header_goes_first_inside_it()
    {
        var s = DropScene();
        Assert.Equal(new FolderDrop(s.Work, 0), _tree.DropAt(s.Rows, gap: 1, onto: null, dragging: null));
    }

    [Fact]
    public void A_drop_elsewhere_joins_the_folder_of_the_row_below()
    {
        var s = DropScene();
        // Above Docs: into Work, before Docs.
        Assert.Equal(new FolderDrop(s.Work, 1), _tree.DropAt(s.Rows, gap: 2, onto: null, dragging: null));
    }

    [Fact]
    public void A_drop_on_a_folders_header_goes_last_inside_it()
    {
        var s = DropScene();
        Assert.Equal(new FolderDrop(s.Docs, 0), _tree.DropAt(s.Rows, gap: 2, onto: s.Docs, dragging: null));
        Assert.Equal(new FolderDrop(s.Work, 2), _tree.DropAt(s.Rows, gap: 0, onto: s.Work, dragging: null));
    }

    [Fact]
    public void A_tab_dropped_between_top_level_folders_joins_the_folder_above()
    {
        var s = DropScene();
        // Above Home, under the closed Docs: a tab can't sit at the top level, so it goes into Docs, last.
        Assert.Equal(new FolderDrop(s.Docs, 0), _tree.DropAt(s.Rows, gap: 3, onto: null, dragging: null));
    }

    [Fact]
    public void A_folder_dropped_between_top_level_folders_goes_there()
    {
        var s = DropScene();
        var moving = _tree.NewFolder("Moving");
        var rows = _tree.Rows(null);
        Assert.Equal(new FolderDrop(null, 1), _tree.DropAt(rows, gap: 3, onto: null, dragging: moving));
    }

    [Fact]
    public void A_drop_at_the_end_goes_into_an_open_empty_last_folder()
    {
        var s = DropScene();
        Assert.Equal(new FolderDrop(s.Home, 0), _tree.DropAt(s.Rows, gap: 4, onto: null, dragging: null));
    }

    [Fact]
    public void Folders_come_back_from_the_session_as_they_were()
    {
        var work = _tree.NewFolder("Work");
        var a = new FakeTab("a");
        _tree.Add(a, work);
        var docs = _tree.NewFolder("Docs", parent: work);
        docs.IsExpanded = false;
        var b = new FakeTab("b");
        _tree.Add(b, work);
        var c = new FakeTab("c");
        _tree.Add(c, docs);
        _tree.NewFolder("Home");
        var saved = _tree.Save();
        var tabs = new[] { a, b, c }.Select(t => (t.Name, Folder: t.Folder!.Id, Position: _tree.PositionOf(t))).ToList();

        var restored = new FolderTree();
        var byId = restored.Load(saved);
        // Tabs come back in whatever order the session lists them.
        foreach (var (name, folder, position) in Enumerable.Reverse(tabs))
            restored.AddSaved(new FakeTab(name), byId[folder], position);
        restored.Arrange();

        Assert.Equal("[Work] |   a |   [Docs] |   b | [Home]", Show(restored));
        restored.SetExpanded(byId[docs.Id], true);
        Assert.Equal("[Work] |   a |   [Docs] |     c |   b | [Home]", Show(restored));
    }

    [Fact]
    public void A_folder_cant_be_dropped_into_itself()
    {
        var s = DropScene();
        Assert.Null(_tree.DropAt(s.Rows, gap: 2, onto: s.Docs, dragging: s.Work));
        Assert.Null(_tree.DropAt(s.Rows, gap: 1, onto: null, dragging: s.Work));
    }
}
