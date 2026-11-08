using Drift;
using Xunit;

public class SiteSettingsButtonTests
{
    [Fact]
    public void It_shows_while_the_pointer_is_over_the_sidebar()
    {
        Assert.True(SiteSettingsButton.Shows(hasSite: true, sidebarHovered: true, panelOpen: false, focused: false));
        Assert.False(SiteSettingsButton.Shows(hasSite: true, sidebarHovered: false, panelOpen: false, focused: false));
    }

    [Fact]
    public void It_stays_while_its_panel_is_open_or_it_has_keyboard_focus()
    {
        // The panel opens beside the sidebar, so the pointer leaves it on the way in.
        Assert.True(SiteSettingsButton.Shows(hasSite: true, sidebarHovered: false, panelOpen: true, focused: false));
        Assert.True(SiteSettingsButton.Shows(hasSite: true, sidebarHovered: false, panelOpen: false, focused: true));
    }

    [Fact]
    public void Drifts_own_pages_have_no_site_settings()
    {
        Assert.False(SiteSettingsButton.Shows(hasSite: false, sidebarHovered: true, panelOpen: true, focused: true));
    }
}
