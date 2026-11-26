namespace Drift.Tests;

public class ProfileIdsTests
{
    // The engine's own profile, your first Profile's: kept as it was, so nobody's signed out.
    [Fact]
    public void The_first_profile_is_the_engines_own()
    {
        Assert.Equal("Default", ProfileIds.Default);
        Assert.True(ProfileIds.IsEngineDefault("Default"));
        Assert.False(ProfileIds.IsEngineDefault("Profile 2"));
    }

    // A new profile's id is fixed for good (it names its folder of cookies and logins), whatever it's called later.
    [Fact]
    public void A_new_profile_gets_the_next_free_id()
    {
        Assert.Equal("Profile 2", ProfileIds.Next(["Default"]));
        Assert.Equal("Profile 3", ProfileIds.Next(["Default", "Profile 2"]));
        Assert.Equal("Profile 2", ProfileIds.Next(["Default", "Profile 3"]));
    }

    // The engine allows letters, digits, spaces and a few marks, at most 64; and "Private" is the private window's.
    [Fact]
    public void An_id_is_one_the_engine_takes_and_never_the_private_windows()
    {
        var id = ProfileIds.Next(["Default"]);
        Assert.Matches("^[A-Za-z0-9 #@$()+\\-_~.]{1,64}$", id);
        Assert.DoesNotMatch("[. ]$", id);
        Assert.NotEqual("Private", id, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_name_is_trimmed_and_must_be_new()
    {
        Assert.Equal("Work", ProfileIds.CleanName("  Work "));
        Assert.Null(ProfileIds.CleanName("   "));
        Assert.True(ProfileIds.NameTaken("work", ["Default", "Work"]));
        Assert.False(ProfileIds.NameTaken("Home", ["Default", "Work"]));
    }

    // As Safari's: your default Profile (the engine's own, with the data you had first) can't be deleted; any other can.
    [Fact]
    public void Any_Profile_but_the_default_can_be_deleted()
    {
        Assert.True(ProfileIds.CanDelete("Profile 2"));
        Assert.False(ProfileIds.CanDelete("Default"));
        Assert.Equal("Your default Profile can’t be deleted", ProfileIds.DefaultCantBeDeleted);
    }

    // Profiles became Profiles, each with its own data. Profiles that shared one keep it for the first of them; the others
    // start fresh, each in a profile of its own.
    [Fact]
    public void Profiles_that_shared_data_get_their_own_the_first_keeping_it()
    {
        Assert.Equal(["Default", "Profile 2", "Profile 3", "Profile 4"],
            ProfileIds.Unshare([null, "Profile 2", "Profile 2", null]));
        Assert.Equal(["Profile 2", "Default", "Profile 3"], ProfileIds.Unshare(["Profile 2", "default", null]));
    }

    [Fact]
    public void Profiles_with_their_own_data_keep_it() =>
        Assert.Equal(["Default", "Profile 5"], ProfileIds.Unshare([null, "Profile 5"]));

    // A deleted profile's data goes when the engine stops, so its id isn't given to a new one till then.
    [Fact]
    public void A_deleted_profiles_id_is_not_given_out_again()
    {
        Assert.Equal("Profile 3", ProfileIds.Next(["Default"], deleted: ["Profile 2"]));
    }
}
