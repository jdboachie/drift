using System;
using Drift;
using Xunit;

public class AdBlockSiteTests
{
    [Fact]
    public void The_switch_covers_the_whole_site_not_just_its_www()
    {
        // Figma's sign-in runs on www.figma.com inside a page on www.figma.com, and its embeds on
        // embed.figma.com: "figma.com" covers them all.
        Assert.Equal("figma.com", AdBlockSite.HostFor(new Uri("https://www.figma.com/community/file/1")));
        Assert.Equal("news.ycombinator.com", AdBlockSite.HostFor(new Uri("https://news.ycombinator.com/item?id=1")));
        Assert.Equal("localhost", AdBlockSite.HostFor(new Uri("http://localhost:3000/")));
    }

    [Fact]
    public void Pages_that_arent_websites_have_no_switch()
    {
        Assert.Null(AdBlockSite.HostFor(new Uri("file:///C:/page.html")));
        Assert.Null(AdBlockSite.HostFor(new Uri("about:blank")));
        Assert.Null(AdBlockSite.HostFor(null));
    }

    [Fact]
    public void Off_means_no_filtering_at_all()
    {
        Assert.Equal(0, AdBlockSite.LevelFor(on: false, defaultLevel: 2));
        Assert.False(AdBlockSite.IsOn(0));
    }

    [Fact]
    public void On_goes_back_to_the_blockers_default_level()
    {
        // uBlock Origin Lite's levels: 1 basic, 2 optimal, 3 complete. A site at the default isn't listed.
        Assert.Equal(3, AdBlockSite.LevelFor(on: true, defaultLevel: 3));
        Assert.Equal(1, AdBlockSite.LevelFor(on: true, defaultLevel: 1));
        Assert.True(AdBlockSite.IsOn(1));
    }

    [Fact]
    public void On_still_filters_when_the_default_is_off()
    {
        Assert.Equal(2, AdBlockSite.LevelFor(on: true, defaultLevel: 0));
    }
}
