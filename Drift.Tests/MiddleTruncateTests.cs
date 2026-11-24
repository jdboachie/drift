using Drift;
using Xunit;

public class MiddleTruncateTests
{
    // One unit a character, so widths are lengths.
    private static double Width(string text) => text.Length;

    [Fact]
    public void Text_that_fits_stays_whole() =>
        Assert.Equal(@"C:\Users\Downloads", MiddleTruncate.Fit(@"C:\Users\Downloads", Width, 30));

    [Fact]
    public void Too_long_loses_its_middle_and_keeps_both_ends()
    {
        var fitted = MiddleTruncate.Fit(@"C:\Users\SomeoneLongName\AppData\Local\DriftDemo\Downloads", Width, 25);
        Assert.Equal(25, fitted.Length);
        Assert.StartsWith(@"C:\Users\", fitted);
        Assert.EndsWith(@"\Downloads", fitted);
        Assert.Contains("…", fitted);
    }

    [Fact]
    public void It_uses_all_the_room_there_is()
    {
        // Never shorter than the room allows: 9 of 10 would waste a character.
        Assert.Equal(10, MiddleTruncate.Fit("abcdefghijklmnopqrstuvwxyz", Width, 10).Length);
    }

    [Fact]
    public void The_end_gets_the_odd_character()
    {
        // The end usually says which folder it is: it's never the shorter side.
        Assert.Equal("abcd…vwxyz", MiddleTruncate.Fit("abcdefghijklmnopqrstuvwxyz", Width, 10));
    }

    [Fact]
    public void No_room_at_all_leaves_just_the_ellipsis() =>
        Assert.Equal("…", MiddleTruncate.Fit("abcdefghij", Width, 0));
}
