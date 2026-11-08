using Drift;
using Xunit;

public class PeekFreezeTests
{
    [Fact]
    public void A_picture_taken_while_hovering_open_as_tab_is_used_if_it_is_recent()
    {
        Assert.True(PeekFreeze.IsFresh(takenAt: 1000, now: 1400));
    }

    [Fact]
    public void An_older_picture_is_retaken_since_the_page_may_have_moved_on()
    {
        Assert.False(PeekFreeze.IsFresh(takenAt: 1000, now: 2500));
    }

    [Fact]
    public void No_picture_is_never_fresh()
    {
        Assert.False(PeekFreeze.IsFresh(takenAt: null, now: 1000));
    }
}
