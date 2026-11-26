using Drift.Models;

namespace Drift.Tests;

// A split with a tab in an unpinned group shows as one row inside that group (Split.cs), in place of its tabs there:
// the group's rows map each of those tabs to the split's row, shown once, where the first of them is.
public class GroupSplitRowTests
{
    private sealed class Row(string name, params IFolderTab[] pages) : IFolderTab
    {
        public TabFolder? Folder { get; set; }
        public int Depth { get; set; }
        public bool StandsFor(IFolderTab tab) => pages.Contains(tab);
        public override string ToString() => name;
    }

    private readonly FolderTree _tree = new();

    private static string Show(IReadOnlyList<object> rows) => string.Join(" | ", rows.Select(row => row switch
    {
        TabFolder f => new string(' ', f.Depth * 2) + $"[{f.Name}]",
        IFolderTab t => new string(' ', t.Depth * 2) + t,
        _ => "?",
    }));

    private (TabFolder Trip, FakeTab A, FakeTab B, FakeTab C, Row Split) TripWithSplit()
    {
        var trip = _tree.NewFolder("Trip", pinned: false);
        var a = new FakeTab("a");
        var b = new FakeTab("b");
        var c = new FakeTab("c");
        _tree.Add(a, trip);
        _tree.Add(b, trip);
        _tree.Add(c, trip);
        var split = new Row("b+c", b, c) { Folder = trip };
        return (trip, a, b, c, split);
    }

    private Func<IFolderTab, IFolderTab?> RowOf(Row split) => tab => split.StandsFor(tab) ? split : tab;

    [Fact]
    public void A_split_shows_once_where_its_first_tab_is()
    {
        var (_, _, _, _, split) = TripWithSplit();
        Assert.Equal("[Trip] |   a |   b+c", Show(_tree.Rows(null, pinned: false, RowOf(split))));
    }

    [Fact]
    public void A_closed_group_shows_the_split_you_are_on()
    {
        var (trip, _, _, c, split) = TripWithSplit();
        _tree.SetExpanded(trip, false);
        Assert.Equal("[Trip] |   b+c", Show(_tree.Rows(c, pinned: false, RowOf(split))));
    }

    [Fact]
    public void A_drop_below_the_split_lands_after_all_its_tabs()
    {
        var (trip, _, _, _, split) = TripWithSplit();
        var rows = _tree.Rows(null, pinned: false, RowOf(split));
        // The end of the list, after the split's row.
        var drop = _tree.DropAt(rows, rows.Count, null, null);
        Assert.Equal(new FolderDrop(trip, 3), drop);
    }

    [Fact]
    public void A_drop_above_the_split_lands_before_its_first_tab()
    {
        var (trip, _, _, _, split) = TripWithSplit();
        var rows = _tree.Rows(null, pinned: false, RowOf(split));
        // Between a and the split's row.
        var drop = _tree.DropAt(rows, 2, null, null);
        Assert.Equal(new FolderDrop(trip, 1), drop);
    }
}
