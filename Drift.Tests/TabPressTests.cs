using Drift;
using Xunit;

public class TabPressTests
{
    // As Chrome (tab.cc, Tab::OnMousePressed) and Arc: a plain press switches at once; anything else waits as it did.

    [Fact]
    public void A_plain_left_press_with_a_mouse_or_touchpad_switches_at_once() =>
        Assert.True(TabPress.SwitchesOnPress(mouse: true, leftButton: true, ctrl: false, shift: false, onControl: false));

    [Fact]
    public void Ctrl_or_Shift_held_it_picks_tabs_on_release_instead()
    {
        Assert.False(TabPress.SwitchesOnPress(mouse: true, leftButton: true, ctrl: true, shift: false, onControl: false));
        Assert.False(TabPress.SwitchesOnPress(mouse: true, leftButton: true, ctrl: false, shift: true, onControl: false));
    }

    [Fact]
    public void Other_buttons_keep_their_own_jobs() =>
        Assert.False(TabPress.SwitchesOnPress(mouse: true, leftButton: false, ctrl: false, shift: false, onControl: false));

    [Fact]
    public void A_press_on_the_rows_own_button_is_that_buttons() =>
        Assert.False(TabPress.SwitchesOnPress(mouse: true, leftButton: true, ctrl: false, shift: false, onControl: true));

    [Fact]
    public void A_finger_still_switches_on_lifting_so_the_list_can_scroll() =>
        Assert.False(TabPress.SwitchesOnPress(mouse: false, leftButton: true, ctrl: false, shift: false, onControl: false));
}
