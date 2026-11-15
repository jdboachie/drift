using System.Collections.Generic;
using Drift;
using Xunit;

public class SplitPartnerTests
{
    private static bool Free(string a, string b) => a != b && a != "split" && b != "split";

    [Fact]
    public void Another_tab_goes_beside_the_page_in_front() =>
        Assert.Equal("front", SplitLayout.Partner("other", "front", ["front", "last"], Free));

    [Fact]
    public void The_tab_in_front_goes_beside_the_one_you_were_on_before() =>
        Assert.Equal("last", SplitLayout.Partner("front", "front", ["front", "last", "older"], Free));

    [Fact]
    public void A_tab_that_cant_be_split_is_passed_over() =>
        Assert.Equal("older", SplitLayout.Partner("front", "front", ["front", "split", "older"], Free));

    [Fact]
    public void With_no_tab_before_it_there_is_none() =>
        Assert.Null(SplitLayout.Partner("front", "front", ["front"], Free));

    [Fact]
    public void With_no_page_in_front_there_is_none() =>
        Assert.Null(SplitLayout.Partner("other", null, new List<string>(), Free));

    [Fact]
    public void A_page_in_front_that_cant_be_split_takes_no_tab() =>
        Assert.Null(SplitLayout.Partner("other", "split", ["split", "last"], Free));

    [Theory]
    [InlineData(SplitSide.Left, SplitSide.Right)]
    [InlineData(SplitSide.Right, SplitSide.Left)]
    public void The_other_half(SplitSide side, SplitSide other) =>
        Assert.Equal(other, SplitLayout.Other(side));
}
