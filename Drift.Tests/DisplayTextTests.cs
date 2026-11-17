using Drift;
using Xunit;

namespace Drift.Tests;

public class DisplayTextTests
{
    [Fact]
    public void Short_text_is_left_alone() => Assert.Equal("example.com", DisplayText.Clip("example.com"));

    [Fact]
    public void Nothing_stays_nothing()
    {
        Assert.Equal("", DisplayText.Clip(""));
        Assert.Null(DisplayText.Clip(null));
    }

    [Fact]
    public void Very_long_text_is_cut_to_the_most_a_line_can_show()
    {
        var url = "https://accounts.google.com/signin?continue=" + new string('a', 50_000);
        var shown = DisplayText.Clip(url);
        Assert.Equal(DisplayText.MaxLength, shown!.Length);
        Assert.StartsWith("https://accounts.google.com/signin?continue=", shown);
    }

    [Fact]
    public void A_cut_never_splits_a_two_part_character()
    {
        // An emoji is two UTF-16 units; cutting between them leaves half of one.
        var text = new string('a', DisplayText.MaxLength - 1) + "😀" + "tail";
        var shown = DisplayText.Clip(text)!;
        Assert.False(char.IsHighSurrogate(shown[^1]));
        Assert.Equal(DisplayText.MaxLength - 1, shown.Length);
    }
}
