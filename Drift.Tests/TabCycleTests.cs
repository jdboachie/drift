using Drift;
using Xunit;

namespace Drift.Tests;

public class TabCycleTests
{
    [Fact]
    public void Current_first_then_most_recently_used()
    {
        var order = TabCycle.Order(recent: ["c", "a", "d"], sidebar: ["a", "b", "c", "d"], current: "c");
        Assert.Equal(["c", "a", "d", "b"], order);
    }

    [Fact]
    public void Tabs_never_visited_follow_in_sidebar_order()
    {
        var order = TabCycle.Order(recent: ["b"], sidebar: ["a", "b", "c", "d"], current: "b");
        Assert.Equal(["b", "a", "c", "d"], order);
    }

    [Fact]
    public void Closed_tabs_in_the_history_are_left_out()
    {
        var order = TabCycle.Order(recent: ["x", "a", "y", "b"], sidebar: ["a", "b"], current: "a");
        Assert.Equal(["a", "b"], order);
    }

    [Fact]
    public void Current_comes_first_even_when_the_history_is_behind()
    {
        var order = TabCycle.Order(recent: ["a", "b"], sidebar: ["a", "b", "c"], current: "c");
        Assert.Equal(["c", "a", "b"], order);
    }

    [Fact]
    public void No_current_tab_keeps_the_history_order()
    {
        var order = TabCycle.Order(recent: ["b", "a"], sidebar: ["a", "b"], current: null);
        Assert.Equal(["b", "a"], order);
    }

    [Fact]
    public void Only_tabs_with_a_page_loaded()
    {
        var order = TabCycle.Order(recent: ["a", "b"], sidebar: ["a", "b", "c", "d"], current: "a", loaded: t => t is "a" or "d");
        Assert.Equal(["a", "d"], order);
    }

    [Fact]
    public void The_current_tab_is_always_in()
    {
        var order = TabCycle.Order(recent: ["b"], sidebar: ["a", "b"], current: "a", loaded: t => t == "b");
        Assert.Equal(["a", "b"], order);
    }

    [Fact]
    public void Starting_forward_picks_the_last_tab_and_backward_the_oldest()
    {
        Assert.Equal(1, TabCycle.Start(+1, 5));
        Assert.Equal(4, TabCycle.Start(-1, 5));
        Assert.Equal(0, TabCycle.Start(+1, 1));
        Assert.Equal(0, TabCycle.Start(-1, 1));
    }

    [Fact]
    public void Stepping_goes_round_the_ends()
    {
        Assert.Equal(2, TabCycle.Step(1, +1, 5));
        Assert.Equal(0, TabCycle.Step(4, +1, 5));
        Assert.Equal(4, TabCycle.Step(0, -1, 5));
        Assert.Equal(0, TabCycle.Step(0, +1, 1));
        Assert.Equal(0, TabCycle.Step(3, +1, 0));
    }
}
