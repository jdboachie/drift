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

    // The library slides in, as Zen's does (translateX(-100% * (1 - progress)), never faded): seen fading in once it
    // was nearly in place, it seemed just to appear (the user, 2026-11-24: "the library should slide in. not just appear").
    // The tabs all but gone before the library comes, never across each other (the user, 2026-11-24: "the sidebar
    // should be almost faded out completely before the library comes sliding in... its overlapping and its ugly").
    [Fact]
    public void The_tabs_are_all_but_gone_before_the_library_starts_coming()
    {
        Assert.True(_swipe.Fade(208 + LibrarySwipe.LibraryFrom * 208).Tabs <= 0.15);
        Assert.Equal(0.0, _swipe.Fade(208 + LibrarySwipe.LibraryFrom * 208).Library);
        Assert.Equal(-416 + LibrarySwipe.KeptOnWindow, _swipe.LibraryShift(208 + LibrarySwipe.LibraryFrom * 208));
    }

    [Fact]
    public void The_library_is_at_full_strength_as_soon_as_it_starts_coming()
    {
        Assert.Equal(0.0, _swipe.Fade(208).Library);
        Assert.Equal(1.0, _swipe.Fade(208 + (LibrarySwipe.LibraryFrom + 0.05) * 208).Library, precision: 6);
        Assert.Equal((0.0, 1.0), _swipe.Fade(416));
    }

    [Fact]
    public void The_library_slides_in_from_off_the_window()
    {
        Assert.InRange(_swipe.LibraryShift(208), -416, -416 + 32);
        var halfway = 208 + (LibrarySwipe.LibraryFrom + (1 - LibrarySwipe.LibraryFrom) / 2) * 208;
        Assert.Equal(-208, _swipe.LibraryShift(halfway), precision: 6);
        Assert.Equal(0, _swipe.LibraryShift(416));
    }

    // Timing (2026-11-25, "refine the animation timing"). Stepped the way the window steps it, from a button or a key.
    private static (double TabsGone, double LibraryIn, double LibraryOut, double AllIn) Timeline(bool open)
    {
        var swipe = new LibrarySwipe(closed: 208, open: 416);
        var motion = LibrarySwipe.Motion(open, thrown: false);
        var (from, to) = open ? (208.0, 416.0) : (416.0, 208.0);
        var spring = new SpringMotion(motion.Seconds, motion.Bounce) { Position = from, Target = to, Velocity = (to - from) * motion.Kick };
        double? tabsGone = null, libraryIn = null, libraryOut = null, allIn = null;
        for (var ms = 1; ms <= 1500; ms++)
        {
            spring.Step(0.001);
            var (tabs, library) = swipe.Fade(spring.Position);
            var shift = swipe.LibraryShift(spring.Position);
            if (open)
            {
                tabsGone ??= tabs <= 0.15 ? ms : null;
                libraryIn ??= library > 0 ? ms : null;
                libraryOut ??= shift >= -0.05 * 416 ? ms : null;   // 95% of the way in
            }
            else
            {
                libraryOut ??= library <= 0 ? ms : null;           // gone
                tabsGone ??= tabs >= 0.95 ? ms : null;             // back
            }
            allIn ??= spring.AtRest ? ms : null;
        }
        return (tabsGone ?? -1, libraryIn ?? -1, libraryOut ?? -1, allIn ?? -1);
    }

    // Opening, as a drawer: the tabs go quickly, then the library slides in over a couple of hundred milliseconds,
    // easing out. It had crossed its whole width in about 90ms, too fast to be seen sliding: it seemed to appear.
    [Fact]
    public void Opened_by_a_button_the_tabs_go_quickly_then_the_library_slides_in_for_long_enough_to_be_seen()
    {
        var (tabsGone, libraryIn, libraryThere, _) = Timeline(open: true);
        Assert.InRange(tabsGone, 30, 90);
        Assert.True(libraryIn >= tabsGone - 15, $"library from {libraryIn}ms, tabs gone {tabsGone}ms");
        Assert.InRange(libraryThere - libraryIn, 200, 320);
    }

    // Closing is quicker than opening: what's going away needn't be watched.
    [Fact]
    public void Shut_by_a_button_it_goes_quicker_than_it_came()
    {
        var opening = Timeline(open: true);
        var (tabsBack, _, libraryGone, _) = Timeline(open: false);
        Assert.True(libraryGone < 140, $"library gone at {libraryGone}ms");
        Assert.True(tabsBack < opening.LibraryOut, $"tabs back at {tabsBack}ms, opening took {opening.LibraryOut}ms");
    }

    // A button or a key doesn't throw anything: no bounce. Fingers that let go moving do, a little.
    [Fact]
    public void Only_a_throw_bounces()
    {
        Assert.Equal(0, LibrarySwipe.Motion(open: true, thrown: false).Bounce);
        Assert.Equal(0, LibrarySwipe.Motion(open: false, thrown: false).Bounce);
        Assert.InRange(LibrarySwipe.Motion(open: true, thrown: true).Bounce, 0.1, 0.2);
    }

    // Springing open, past its place and back, it goes only as far as the page: sliding faster than the page to come in
    // later, it went three times as far (the user's screenshot, 2026-11-24: an empty strip at the window's edge).
    [Fact]
    public void Past_open_the_library_goes_no_further_than_the_page()
    {
        Assert.Equal(20, _swipe.LibraryShift(416 + 20), precision: 6);
    }

    // Never wholly off the window, even as it's drawn sliding: its History list builds the rows it takes to be on
    // screen, and finding itself off the window as an open began, it built none and slid in empty (the user,
    // 2026-11-25: "its just empty and then shows"). The strip left on is unseen: it's wholly transparent till it comes.
    // (The list ends 12px in from the panel's edge: its padding, and the panel's.)
    [Fact]
    public void Shut_a_strip_of_the_library_stays_on_the_window_unseen()
    {
        // Past the panel's padding and the list's (12px), into the list itself.
        Assert.True(LibrarySwipe.KeptOnWindow > 12);
        // No wider than it's come once it's fully shown, so it never seems to jump in.
        var shown = 208 + (LibrarySwipe.LibraryFrom + LibrarySwipe.LibraryShown) * 208;
        Assert.True(LibrarySwipe.KeptOnWindow <= 416 + _swipe.LibraryShift(shown) + 0.001);
        Assert.Equal(-416 + LibrarySwipe.KeptOnWindow, _swipe.LibraryShift(208));
        Assert.Equal(-416 + LibrarySwipe.KeptOnWindow, _swipe.LibraryShift(208 - 20));
        Assert.Equal(0.0, _swipe.Fade(208 + LibrarySwipe.LibraryFrom * 208).Library);
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
