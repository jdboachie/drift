using Drift;
using Xunit;

public class CloseTabsTests
{
    [Fact]
    public void Below_is_every_row_after_it()
    {
        Assert.Equal([3, 4], CloseTabs.Below(count: 5, index: 2));
        Assert.Empty(CloseTabs.Below(count: 5, index: 4));
    }

    [Fact]
    public void Others_is_every_row_but_it()
    {
        Assert.Equal([0, 1, 3], CloseTabs.Others(count: 4, index: 2));
        Assert.Empty(CloseTabs.Others(count: 1, index: 0));
    }

    [Fact]
    public void A_duplicate_is_a_page_open_earlier_in_the_list()
    {
        string?[] rows = ["a", "b", "a", "c", "b"];
        Assert.Equal([2, 4], CloseTabs.Duplicates(rows, kept: [], front: null));
    }

    [Fact]
    public void A_page_already_pinned_counts_as_open()
    {
        string?[] rows = ["a", "b"];
        Assert.Equal([1], CloseTabs.Duplicates(rows, kept: ["b"], front: null));
    }

    [Fact]
    public void The_tab_you_are_on_stays_and_its_earlier_twin_goes()
    {
        string?[] rows = ["a", "b", "a"];
        Assert.Equal([0], CloseTabs.Duplicates(rows, kept: [], front: 2));
    }

    [Fact]
    public void Rows_without_a_page_of_their_own_are_never_duplicates()
    {
        // A split's row (its pages count as kept), or a blank tab.
        string?[] rows = [null, "a", null];
        Assert.Equal([1], CloseTabs.Duplicates(rows, kept: ["a"], front: null));
    }
}
