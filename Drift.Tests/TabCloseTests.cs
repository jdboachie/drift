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
}
