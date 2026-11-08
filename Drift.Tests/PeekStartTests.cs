using Drift;
using Xunit;

public class PeekStartTests
{
    [Fact]
    public void With_no_peek_open_a_new_one_opens()
    {
        Assert.Equal(PeekStart.New, PeekLink.Start(peekOpen: false, sameOwner: false));
    }

    [Fact]
    public void A_link_from_the_tab_whose_peek_is_showing_goes_into_that_peek()
    {
        Assert.Equal(PeekStart.Navigate, PeekLink.Start(peekOpen: true, sameOwner: true));
    }

    [Fact]
    public void A_link_from_another_tab_replaces_the_peek_left_behind_in_the_background()
    {
        // It was out of sight with its own tab; reusing it left the new Peek hidden.
        Assert.Equal(PeekStart.Replace, PeekLink.Start(peekOpen: true, sameOwner: false));
    }
}
