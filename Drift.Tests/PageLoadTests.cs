using Drift;
using Xunit;

public class PageLoadTests
{
    [Fact]
    public void A_page_starting_to_load_is_loading()
    {
        var load = new PageLoad();
        load.Started(1);
        Assert.True(load.IsLoading);
    }

    [Fact]
    public void It_stops_loading_when_that_navigation_finishes()
    {
        var load = new PageLoad();
        load.Started(1);
        load.Finished(1);
        Assert.False(load.IsLoading);
    }

    [Fact]
    public void An_earlier_navigation_finishing_late_doesnt_end_the_newer_one()
    {
        // Click a link, then another before the first loads: the first reports finishing (cancelled)
        // while the second is still on its way. The button stays a stop button.
        var load = new PageLoad();
        load.Started(1);
        load.Started(2);
        load.Finished(1);
        Assert.True(load.IsLoading);
        load.Finished(2);
        Assert.False(load.IsLoading);
    }

    [Fact]
    public void Nothing_started_is_nothing_loading()
    {
        var load = new PageLoad();
        load.Finished(7);
        Assert.False(load.IsLoading);
    }

    // For the loading bar above the page (LoadingBar.cs), as Arc's: waiting for the site, then drawing its page.

    [Fact]
    public void Until_the_site_answers_it_is_waiting()
    {
        var load = new PageLoad();
        load.Started(1);
        Assert.Equal(LoadPhase.Waiting, load.Phase);
    }

    [Fact]
    public void Once_the_new_page_comes_it_is_drawing()
    {
        var load = new PageLoad();
        load.Started(1);
        load.Committed();
        Assert.Equal(LoadPhase.Drawing, load.Phase);
        load.Finished(1);
        Assert.Equal(LoadPhase.None, load.Phase);
    }

    [Fact]
    public void A_new_navigation_waits_again()
    {
        var load = new PageLoad();
        load.Started(1);
        load.Committed();
        load.Started(2);
        Assert.Equal(LoadPhase.Waiting, load.Phase);
    }

    [Fact]
    public void A_page_coming_with_nothing_started_changes_nothing()
    {
        var load = new PageLoad();
        load.Committed();
        Assert.Equal(LoadPhase.None, load.Phase);
    }
}
