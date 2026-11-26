using System.Text.Json;
using Drift;
using Xunit;

// session.json keeps the keys it was written with before Profiles took over from Spaces, so a file from any
// earlier Drift still reads back: "Spaces" and "Space" for a Profile and its place, "Profile" for an engine profile.
public class SessionFileTests
{
    private static SessionData Sample() => new()
    {
        Profile = 1,
        Profiles =
        [
            new SessionProfile { Name = "Home" },
            new SessionProfile { Name = "Work", Engine = "Profile 2" },
        ],
        Tabs =
        [
            new SessionTab { Url = "https://example.com/", Place = "Essential", Profile = 1, EssentialEngine = "Profile 2" },
        ],
        Splits = [new SessionSplit { Pages = [0, 1], Row = 2, Profile = 1 }],
    };

    [Fact]
    public void A_session_with_Profiles_saves()
    {
        var json = JsonSerializer.Serialize(Sample());
        Assert.False(string.IsNullOrEmpty(json));
    }

    [Fact]
    public void A_session_reads_back_as_it_was_saved()
    {
        var back = JsonSerializer.Deserialize<SessionData>(JsonSerializer.Serialize(Sample()))!;
        Assert.Equal(1, back.Profile);
        Assert.Equal(["Home", "Work"], back.ProfileList().ConvertAll(p => p.Name));
        Assert.Equal("Profile 2", back.Profiles![1].Engine);
        Assert.Equal(1, back.Tabs[0].Profile);
        Assert.Equal("Profile 2", back.Tabs[0].EssentialEngine);
        Assert.Equal(1, back.Splits[0].Profile);
    }

    [Fact]
    public void The_file_keeps_its_old_keys()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(Sample()));
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("Space").GetInt32());
        Assert.Equal("Profile 2", root.GetProperty("Spaces")[1].GetProperty("Profile").GetString());
        Assert.Equal(1, root.GetProperty("Tabs")[0].GetProperty("Space").GetInt32());
        Assert.Equal("Profile 2", root.GetProperty("Tabs")[0].GetProperty("Profile").GetString());
        Assert.Equal(1, root.GetProperty("Splits")[0].GetProperty("Space").GetInt32());
    }

    [Fact]
    public void A_file_from_before_Profiles_reads_its_Spaces()
    {
        var back = JsonSerializer.Deserialize<SessionData>(
            """{"Tabs":[{"Url":"https://a.com/","Space":1}],"Spaces":[{"Name":"Home"},{"Name":"Work","Profile":"Profile 2"}],"Space":1}""")!;
        Assert.Equal(1, back.Profile);
        Assert.Equal("Work", back.ProfileList()[1].Name);
        Assert.Equal("Profile 2", back.ProfileList()[1].Engine);
        Assert.Equal(1, back.Tabs[0].Profile);
    }
}
