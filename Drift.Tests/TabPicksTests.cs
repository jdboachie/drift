using Xunit;

namespace Drift.Tests;

public class TabPicksTests
{
    private static readonly string[] Order = ["a", "b", "c", "d"];

    [Fact]
    public void Ctrl_click_picks_the_tab_in_front_and_the_one_clicked() =>
        Assert.Equal(["a", "c"], TabPicks.Toggle([], "c", front: "a"));

    [Fact]
    public void Ctrl_click_on_a_picked_tab_takes_it_out()
    {
        Assert.Equal(["a"], TabPicks.Toggle(["a", "c"], "c", front: "a"));
        Assert.Equal(["c"], TabPicks.Toggle(["a", "c"], "a", front: "a"));
    }

    [Fact]
    public void Ctrl_click_adds_to_what_is_picked_already() =>
        Assert.Equal(["a", "c", "d"], TabPicks.Toggle(["a", "c"], "d", front: "a"));

    [Fact]
    public void With_no_tab_in_front_that_can_be_picked_only_the_one_clicked() =>
        Assert.Equal(["c"], TabPicks.Toggle([], "c", front: null));

    [Fact]
    public void Ctrl_click_on_the_tab_in_front_alone_unpicks_it() =>
        Assert.Empty(TabPicks.Toggle([], "a", front: "a"));

    [Fact]
    public void Shift_click_picks_every_tab_between_either_way()
    {
        Assert.Equal(["b", "c", "d"], TabPicks.Range(Order, anchor: "b", clicked: "d"));
        Assert.Equal(["b", "c", "d"], TabPicks.Range(Order, anchor: "d", clicked: "b"));
        Assert.Equal(["c"], TabPicks.Range(Order, anchor: "c", clicked: "c"));
    }

    [Fact]
    public void Shift_click_with_no_anchor_in_the_list_picks_only_the_one_clicked()
    {
        Assert.Equal(["c"], TabPicks.Range(Order, anchor: null, clicked: "c"));
        Assert.Equal(["c"], TabPicks.Range(Order, anchor: "x", clicked: "c"));
    }

    [Fact]
    public void A_tab_that_cannot_be_picked_picks_nothing() =>
        Assert.Empty(TabPicks.Range(Order, anchor: "a", clicked: "x"));

    [Fact]
    public void Picks_go_into_a_folder_in_the_sidebars_order() =>
        Assert.Equal(["a", "c", "d"], TabPicks.InOrder(["d", "a", "c"], Order));
}
