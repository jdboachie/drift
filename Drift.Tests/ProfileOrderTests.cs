using System.Collections.Generic;
using Drift;
using Xunit;

public class ProfileOrderTests
{
    // Arc's fallback name for a Space is "Space N", N its place; a Profile's is "Profile N" (SpaceModel: 'Fallback title for a space, with the
    // index of the space. e.g "Space N"').

    [Fact]
    public void The_first_Profile_is_Profile_1() => Assert.Equal("Profile 1", ProfileOrder.DefaultName([]));

    [Fact]
    public void A_new_Profile_is_named_for_its_place() => Assert.Equal("Profile 3", ProfileOrder.DefaultName(["Work", "Home"]));

    [Fact]
    public void A_name_already_taken_is_skipped() => Assert.Equal("Profile 3", ProfileOrder.DefaultName(["Profile 2", "Home"]));

    // "New Spaces now appear next to your active space, rather than at the end" (Arc, April 2023).
    [Fact]
    public void A_new_Profile_goes_next_to_the_one_you_are_on() => Assert.Equal(2, ProfileOrder.NewIndex(current: 1, count: 4));

    [Fact]
    public void Next_and_previous_stop_at_the_ends()
    {
        Assert.Equal(2, ProfileOrder.Step(current: 1, count: 3, by: 1));
        Assert.Equal(0, ProfileOrder.Step(current: 1, count: 3, by: -1));
        Assert.Null(ProfileOrder.Step(current: 2, count: 3, by: 1));
        Assert.Null(ProfileOrder.Step(current: 0, count: 3, by: -1));
    }

    [Fact]
    public void Ctrl_and_a_number_goes_to_that_Profile_if_there_is_one()
    {
        Assert.Equal(0, ProfileOrder.ForNumber(1, count: 3));
        Assert.Equal(2, ProfileOrder.ForNumber(3, count: 3));
        Assert.Null(ProfileOrder.ForNumber(4, count: 3));
    }

    // The next Profile is "one to the right" (Arc's Select Next Space): fingers moving left bring it in. The Library is
    // before the first Profile, where a swipe the other way opened it before there were Profiles.
    [Fact]
    public void Fingers_moving_left_go_to_the_next_Profile()
    {
        Assert.Equal(ProfileSwipe.Next, ProfileOrder.Swipe(current: 0, count: 2, fingersRight: false));
        Assert.Equal(ProfileSwipe.None, ProfileOrder.Swipe(current: 1, count: 2, fingersRight: false));
    }

    [Fact]
    public void Fingers_moving_right_go_back_a_Profile_and_from_the_first_to_the_Library()
    {
        Assert.Equal(ProfileSwipe.Previous, ProfileOrder.Swipe(current: 1, count: 2, fingersRight: true));
        Assert.Equal(ProfileSwipe.Library, ProfileOrder.Swipe(current: 0, count: 2, fingersRight: true));
        Assert.Equal(ProfileSwipe.Library, ProfileOrder.Swipe(current: 0, count: 1, fingersRight: true));
    }

    [Fact]
    public void Deleting_the_Profile_you_are_on_shows_the_one_before_it()
    {
        Assert.Equal(1, ProfileOrder.AfterDelete(deleted: 2, current: 2));
        Assert.Equal(0, ProfileOrder.AfterDelete(deleted: 0, current: 0));
    }

    [Fact]
    public void Deleting_another_Profile_keeps_you_where_you_are()
    {
        Assert.Equal(1, ProfileOrder.AfterDelete(deleted: 0, current: 2));
        Assert.Equal(1, ProfileOrder.AfterDelete(deleted: 3, current: 1));
    }

    // A swipe between Profiles: the sidebar follows the fingers towards a Profile there is, and doesn't move any other way
    // (the user, 2026-11-24, of a rubber band there: "remove the rubber banding"). Fingers right is positive; direction
    // is +1 that way.

    [Fact]
    public void The_sidebar_follows_the_fingers_towards_a_Profile_there_is() =>
        Assert.Equal(-80, ProfileOrder.Follow(moved: -80, direction: -1, canGo: true, page: 224));

    [Fact]
    public void With_no_Profile_that_way_it_stays_put() =>
        Assert.Equal(0, ProfileOrder.Follow(moved: -200, direction: -1, canGo: false, page: 224));

    // As Arc's pages: the fingers can bring the next Profile all the way in, and no further (the user's first swipe took
    // the tabs 242 along a 196-wide sidebar, the next Profile past its place and back).
    [Fact]
    public void The_sidebar_goes_no_further_than_the_next_Profile()
    {
        Assert.Equal(-224, ProfileOrder.Follow(moved: -400, direction: -1, canGo: true, page: 224));
        Assert.Equal(224, ProfileOrder.Follow(moved: 300, direction: 1, canGo: true, page: 224));
    }

    [Fact]
    public void Back_past_where_it_started_it_stays_put() =>
        Assert.Equal(0, ProfileOrder.Follow(moved: 60, direction: -1, canGo: true, page: 224));

    // Profiles page past side by side, as Arc's Spaces do: a page apart, the sidebar's width and a gap.
    [Fact]
    public void The_next_Profile_is_a_sidebar_and_a_gap_away() =>
        Assert.Equal(208 + ProfileOrder.PageGap, ProfileOrder.PageStep(208));

    // Where a page's spring has got to, to catch it mid-way.
    [Fact]
    public void A_spring_starts_where_it_was_let_go() =>
        Assert.Equal((-224.0, 0.0), ProfileOrder.SpringAt(offset: -224, velocity: 0, seconds: 0, duration: 0.18));

    [Fact]
    public void A_spring_is_at_rest_once_it_has_settled()
    {
        var (offset, velocity) = ProfileOrder.SpringAt(-224, 0, ProfileOrder.SettleSeconds(-224, 0, 0.18), 0.18);
        Assert.InRange(offset, -0.5, 0.5);
        Assert.InRange(velocity, -50, 50);
    }

    [Fact]
    public void A_spring_from_rest_is_moving_towards_its_end_part_way()
    {
        var (offset, velocity) = ProfileOrder.SpringAt(-224, 0, 0.03, 0.18);
        Assert.InRange(offset, -224, -1);
        Assert.True(velocity > 0);
    }

    // A swipe picking the strip up mid-slide carries on from where it is.
    [Fact]
    public void A_caught_strip_starts_where_it_was() =>
        Assert.Equal(100, ProfileOrder.FollowFrom(carried: 100, moved: 0, direction: -1, canGo: true, page: 224));

    [Fact]
    public void A_caught_strip_goes_no_further_than_the_next_Profile() =>
        Assert.Equal(-224, ProfileOrder.FollowFrom(carried: 100, moved: -500, direction: -1, canGo: true, page: 224));

    [Fact]
    public void A_caught_strip_past_an_end_only_goes_back()
    {
        Assert.Equal(40, ProfileOrder.FollowFrom(carried: 100, moved: -60, direction: -1, canGo: false, page: 224));
        Assert.Equal(0, ProfileOrder.FollowFrom(carried: 100, moved: -300, direction: -1, canGo: false, page: 224));
        Assert.Equal(100, ProfileOrder.FollowFrom(carried: 100, moved: 50, direction: 1, canGo: false, page: 224));
    }

    [Theory]
    [InlineData(-80, -1, true)]
    [InlineData(60, -1, true)]
    [InlineData(-200, -1, false)]
    [InlineData(-400, -1, true)]
    [InlineData(300, 1, true)]
    public void Caught_with_nothing_carried_it_follows_as_from_rest(double moved, int direction, bool canGo) =>
        Assert.Equal(ProfileOrder.Follow(moved, direction, canGo, 224), ProfileOrder.FollowFrom(0, moved, direction, canGo, 224));

    // Every Profile's sidebar sits on one strip, in the Profiles' order, a page apart; the strip moves the one you're on into view.
    [Fact]
    public void Each_Profiles_sidebar_is_a_page_further_along_the_strip()
    {
        Assert.Equal(0, ProfileOrder.PageLeft(0, 208));
        Assert.Equal(2 * (208 + ProfileOrder.PageGap), ProfileOrder.PageLeft(2, 208));
    }

    [Fact]
    public void The_strip_moves_back_by_as_much_to_show_a_Profile()
    {
        Assert.Equal(0, ProfileOrder.StripAt(0, 208));
        Assert.Equal(-2 * (208 + ProfileOrder.PageGap), ProfileOrder.StripAt(2, 208));
    }

    // How long a page's spring takes to come to rest (within half a pixel), from how far off and how fast.
    [Fact]
    public void A_spring_with_nowhere_to_go_is_at_rest_already() =>
        Assert.Equal(0, ProfileOrder.SettleSeconds(offset: 0, velocity: 0, duration: 0.18));

    [Fact]
    public void A_page_from_rest_settles_in_a_fraction_of_a_second() =>
        Assert.InRange(ProfileOrder.SettleSeconds(offset: 224, velocity: 0, duration: 0.18), 0.12, 0.35);

    [Fact]
    public void A_fling_past_the_end_takes_longer_to_settle_than_one_towards_it()
    {
        var towards = ProfileOrder.SettleSeconds(offset: 120, velocity: -1500, duration: 0.18);
        var away = ProfileOrder.SettleSeconds(offset: 120, velocity: 1500, duration: 0.18);
        Assert.True(away > towards, $"{away} vs {towards}");
    }

    [Fact]
    public void Let_go_past_a_quarter_of_the_way_it_goes_on()
    {
        Assert.True(ProfileOrder.Commits(moved: -60, speed: 0, width: 200, direction: -1, canGo: true));
        Assert.False(ProfileOrder.Commits(moved: -40, speed: 0, width: 200, direction: -1, canGo: true));
    }

    [Fact]
    public void A_flick_goes_the_way_it_was_flicked()
    {
        Assert.True(ProfileOrder.Commits(moved: -10, speed: -400, width: 200, direction: -1, canGo: true));
        Assert.False(ProfileOrder.Commits(moved: -100, speed: 400, width: 200, direction: -1, canGo: true));
    }

    [Fact]
    public void With_no_Profile_that_way_it_always_goes_back() =>
        Assert.False(ProfileOrder.Commits(moved: -150, speed: -900, width: 200, direction: -1, canGo: false));

    [Fact]
    public void The_switcher_shows_icons_while_they_fit_and_dots_after()
    {
        Assert.False(ProfileOrder.Dots(count: 4, room: 4 * ProfileOrder.IconWidth));
        Assert.True(ProfileOrder.Dots(count: 5, room: 4 * ProfileOrder.IconWidth));
    }
}

public class SessionProfilesTests
{
    [Fact]
    public void A_file_from_before_Profiles_puts_everything_in_your_default_Profile()
    {
        var folders = new List<SessionFolder> { new() { Id = "f", Name = "Work" } };
        var data = new SessionData { Folders = folders };
        var profiles = data.ProfileList();
        Assert.Single(profiles);
        Assert.Equal("Default", profiles[0].Name);
        Assert.Null(profiles[0].Engine);
        Assert.Same(folders, profiles[0].Folders);
    }

    [Fact]
    public void A_tab_saved_without_a_Profile_is_in_the_first()
    {
        Assert.Equal(0, new SessionTab().ProfileIn(3));
        Assert.Equal(2, new SessionTab { Profile = 2 }.ProfileIn(3));
    }

    [Fact]
    public void A_tab_whose_Profile_is_gone_goes_to_the_first() => Assert.Equal(0, new SessionTab { Profile = 5 }.ProfileIn(2));
}
