using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Drift.Tests;

public class ListSyncTests
{
    private static (ObservableCollection<object> List, List<NotifyCollectionChangedAction> Changes) Watch(params object[] items)
    {
        var list = new ObservableCollection<object>(items);
        var changes = new List<NotifyCollectionChangedAction>();
        list.CollectionChanged += (_, e) => changes.Add(e.Action);
        return (list, changes);
    }

    [Fact]
    public void The_list_ends_up_as_wanted()
    {
        var (list, _) = Watch("a", "b", "c", "d");

        ListSync.Apply(list, new object[] { "d", "a", "e", "c" });

        Assert.Equal(new object[] { "d", "a", "e", "c" }, list);
    }

    [Fact]
    public void Rows_that_stay_are_left_alone()
    {
        var (list, changes) = Watch("a", "b", "c");

        // A folder closing: its rows go, the rest stay put.
        ListSync.Apply(list, new object[] { "a", "c" });

        Assert.Equal(new[] { NotifyCollectionChangedAction.Remove }, changes);
    }

    [Fact]
    public void A_row_moving_is_one_move()
    {
        var (list, changes) = Watch("a", "b", "c");

        ListSync.Apply(list, new object[] { "c", "a", "b" });

        Assert.Equal(new[] { NotifyCollectionChangedAction.Move }, changes);
    }
}
