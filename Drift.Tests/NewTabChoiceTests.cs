using Xunit;

namespace Drift.Tests;

public class NewTabChoiceTests
{
    [Fact]
    public void A_link_you_open_in_a_new_tab_stays_behind_by_default() =>
        Assert.False(NewTabChoice.ComesToFront(chosen: true, switchToNewTabs: false, shift: false));

    [Fact]
    public void With_the_setting_on_it_comes_to_the_front() =>
        Assert.True(NewTabChoice.ComesToFront(chosen: true, switchToNewTabs: true, shift: false));

    [Fact]
    public void Shift_turns_the_setting_round()
    {
        Assert.True(NewTabChoice.ComesToFront(chosen: true, switchToNewTabs: false, shift: true));
        Assert.False(NewTabChoice.ComesToFront(chosen: true, switchToNewTabs: true, shift: true));
    }

    [Fact]
    public void A_page_opening_a_tab_itself_always_comes_to_the_front()
    {
        Assert.True(NewTabChoice.ComesToFront(chosen: false, switchToNewTabs: false, shift: false));
        Assert.True(NewTabChoice.ComesToFront(chosen: false, switchToNewTabs: false, shift: true));
    }

    [Fact]
    public void A_press_counts_for_the_tab_it_opens_for_three_seconds()
    {
        Assert.True(NewTabChoice.PressedJustNow(pressedAt: 10_000, now: 10_400));
        Assert.True(NewTabChoice.PressedJustNow(pressedAt: 10_000, now: 12_999));
        Assert.False(NewTabChoice.PressedJustNow(pressedAt: 10_000, now: 13_000));
    }

    [Fact]
    public void No_press_is_never_just_now() => Assert.False(NewTabChoice.PressedJustNow(pressedAt: 0, now: 500));

    [Fact]
    public void A_window_with_no_address_is_for_the_page_to_fill_in()
    {
        Assert.True(NewTabChoice.PageFillsItIn("about:blank"));
        Assert.True(NewTabChoice.PageFillsItIn("ABOUT:BLANK"));
        Assert.True(NewTabChoice.PageFillsItIn(""));
        Assert.True(NewTabChoice.PageFillsItIn(null));
    }

    [Fact]
    public void A_window_with_an_address_is_not() =>
        Assert.False(NewTabChoice.PageFillsItIn("https://example.com/"));
}
