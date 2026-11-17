using Drift;
using Xunit;

public class TabCloseTests
{
    [Fact]
    public void A_tab_with_a_peek_closes_the_peek_first()
    {
        Assert.Equal(TabClose.ClosePeek, TabCloses.First(hasPeek: true, pinned: false));
        Assert.Equal(TabClose.ClosePeek, TabCloses.First(hasPeek: true, pinned: true));
    }

    [Fact]
    public void Then_a_regular_tab_closes()
    {
        Assert.Equal(TabClose.Remove, TabCloses.First(hasPeek: false, pinned: false));
    }

    [Fact]
    public void And_a_pinned_tab_unloads()
    {
        Assert.Equal(TabClose.Unload, TabCloses.First(hasPeek: false, pinned: true));
    }

    [Fact]
    public void The_button_says_it_closes_the_peek_while_there_is_one()
    {
        Assert.Equal("Close Peek", TabCloses.Name(hasPeek: true, pinned: false, unloaded: false));
        Assert.Equal("Close Peek", TabCloses.Name(hasPeek: true, pinned: true, unloaded: false));
    }

    [Fact]
    public void Otherwise_what_it_does_to_the_tab()
    {
        Assert.Equal("Close tab", TabCloses.Name(hasPeek: false, pinned: false, unloaded: false));
        Assert.Equal("Unload tab", TabCloses.Name(hasPeek: false, pinned: true, unloaded: false));
        // An unloaded pinned tab's button takes it out of the sidebar.
        Assert.Equal("Close tab", TabCloses.Name(hasPeek: false, pinned: true, unloaded: true));
    }
}
