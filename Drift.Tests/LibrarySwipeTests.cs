namespace Drift.Tests;

public class LibrarySwipeTests
{
    // The sidebar is 208 wide; the library twice that.
    private readonly LibrarySwipe _swipe = new(closed: 208, open: 416);

    [Fact]
    public void Within_its_range_the_sidebar_follows_the_finger_exactly()
    {
        Assert.Equal(300, _swipe.Follow(300));
        Assert.Equal(208, _swipe.Follow(208));
    }

    [Fact]
    public void Past_either_end_it_gives_a_little_and_no_more()
    {
        var past = _swipe.Follow(416 + 200);

        Assert.InRange(past, 417, 416 + LibrarySwipe.Stretch);
        Assert.True(_swipe.Follow(416 + 400) > past);   // still gives, ever less
        Assert.InRange(_swipe.Follow(208 - 200), 208 - LibrarySwipe.Stretch, 207);
    }

    [Fact]
    public void A_flick_decides_whichever_way_it_goes()
    {
        Assert.True(_swipe.Opens(from: 208, width: 230, velocity: 600));    // barely started, flicked open
        Assert.False(_swipe.Opens(from: 416, width: 400, velocity: -600));  // nearly open, flicked shut
        Assert.False(_swipe.Opens(from: 208, width: 400, velocity: -600));  // swiped open, flicked back
    }

    [Fact]
    public void Let_go_slowly_it_goes_the_way_it_was_swiped_once_a_quarter_of_the_way()
    {
        // A slow swipe that's got a quarter of the way doesn't spring back.
        Assert.True(_swipe.Opens(from: 208, width: 261, velocity: 30));
        Assert.True(_swipe.Opens(from: 208, width: 261, velocity: -30));
        Assert.False(_swipe.Opens(from: 416, width: 363, velocity: -30));
    }

    [Fact]
    public void Let_go_slowly_short_of_that_it_goes_back_where_it_was()
    {
        Assert.False(_swipe.Opens(from: 208, width: 250, velocity: 30));
        Assert.True(_swipe.Opens(from: 416, width: 380, velocity: -30));
    }

    [Fact]
    public void Let_go_moving_away_from_where_it_springs_to_it_carries_none_of_that_on()
    {
        // Pulled short and flicked further left: it springs back, not on further left first.
        Assert.Equal(0, _swipe.Carry(width: 190, target: 208, velocity: -4000));
        Assert.Equal(0, _swipe.Carry(width: 300, target: 416, velocity: -100));
    }

    [Fact]
    public void Flicked_towards_where_it_springs_to_it_goes_no_faster_than_a_button_sends_it()
    {
        // As fast as a button's open from there, which overshoots a little; not the flick's 4000.
        Assert.Equal((416 - 330) * LibrarySwipe.CarryPerPixel, _swipe.Carry(width: 330, target: 416, velocity: 4000));
        Assert.Equal(-(250 - 208) * LibrarySwipe.CarryPerPixel, _swipe.Carry(width: 250, target: 208, velocity: -4000));
        // Slower than that, it keeps its own speed.
        Assert.Equal(300, _swipe.Carry(width: 300, target: 416, velocity: 300));
    }

    [Fact]
    public void The_tabs_fade_out_over_the_first_third_of_the_way()
    {
        Assert.Equal((1.0, 0.0), _swipe.Fade(208));
        Assert.InRange(_swipe.Fade(208 + 0.16 * 208).Tabs, 0.5, 0.65);   // Zen: 61% at 16%
        Assert.Equal(0.0, _swipe.Fade(208 + 0.35 * 208).Tabs, precision: 6);
    }

    [Fact]
    public void The_library_only_fades_in_once_the_tabs_are_all_but_gone()
    {
        Assert.Equal(0.0, _swipe.Fade(208 + 0.3 * 208).Library, precision: 6);
        Assert.InRange(_swipe.Fade(208 + 0.45 * 208).Library, 0.2, 0.8);
        Assert.Equal(1.0, _swipe.Fade(208 + 0.6 * 208).Library, precision: 6);
        Assert.Equal((0.0, 1.0), _swipe.Fade(416));
    }

    [Fact]
    public void A_touchpad_swipe_over_the_page_scrolls_the_page()
    {
        // The sidebar ends at 208: over it the swipe is the library's, over the page it's the page's.
        Assert.True(LibrarySwipe.Catches(x: 100, sidebarRight: 208, swiping: false));
        Assert.False(LibrarySwipe.Catches(x: 600, sidebarRight: 208, swiping: false));
        // Nothing showing (the sidebar hidden): nothing is caught.
        Assert.False(LibrarySwipe.Catches(x: 4, sidebarRight: 0, swiping: false));
    }

    [Fact]
    public void A_swipe_under_way_is_kept_wherever_the_pointer_is()
    {
        Assert.True(LibrarySwipe.Catches(x: 600, sidebarRight: 208, swiping: true));
    }
}
