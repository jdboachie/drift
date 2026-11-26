namespace Drift.Tests;

// Essentials are each Profile's own, saved with the Profile they're in. Files from before were a profile's Essentials, with
// no Profile: each goes to the first Profile on its profile.
public class EssentialProfileTests
{
    // Home (Default), Work (Office), Side (Default).
    private static readonly string?[] Profiles = [null, "Profile 1", null];

    [Fact]
    public void An_Essential_saved_with_its_Profile_goes_back_to_it() =>
        Assert.Equal(2, new SessionTab { Profile = 2 }.EssentialProfileIn(Profiles));

    [Fact]
    public void One_saved_in_the_first_Profile_says_so_and_stays_there() =>
        Assert.Equal(0, new SessionTab { Profile = 0, EssentialEngine = "Profile 1" }.EssentialProfileIn(Profiles));

    [Fact]
    public void An_older_Default_Essential_goes_to_the_first_Profile_on_Default() =>
        Assert.Equal(0, new SessionTab().EssentialProfileIn(Profiles));

    [Fact]
    public void An_older_Essential_of_another_profile_goes_to_the_first_Profile_on_it() =>
        Assert.Equal(1, new SessionTab { EssentialEngine = "profile 1" }.EssentialProfileIn(Profiles));

    [Fact]
    public void One_whose_profile_no_Profile_uses_goes_to_the_first() =>
        Assert.Equal(0, new SessionTab { EssentialEngine = "Profile 9" }.EssentialProfileIn(Profiles));

    [Fact]
    public void The_first_Profile_on_Default_need_not_be_the_first_Profile() =>
        Assert.Equal(1, new SessionTab().EssentialProfileIn(["Profile 1", "Default"]));

    [Fact]
    public void A_Profile_thats_gone_means_the_first() =>
        Assert.Equal(0, new SessionTab { Profile = 5 }.EssentialProfileIn(Profiles));
}
