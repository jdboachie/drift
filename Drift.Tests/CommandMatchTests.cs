using Drift;
using Xunit;

namespace Drift.Tests;

public class CommandMatchTests
{
    [Fact]
    public void Every_word_typed_starts_a_word_of_the_name()
    {
        Assert.True(CommandMatch.Score("pin to georg", "Pin to Secret Project / Georgia") > 0);
        Assert.True(CommandMatch.Score("dev", "Developer Tools") > 0);
        Assert.True(CommandMatch.Score("tools dev", "Developer Tools") > 0);
        Assert.Equal(-1, CommandMatch.Score("velop", "Developer Tools"));
        Assert.Equal(-1, CommandMatch.Score("pin to work", "Pin to Secret Project / Georgia"));
    }

    [Fact]
    public void Keywords_count_as_words_of_the_name()
    {
        Assert.True(CommandMatch.Score("incognito", "New Private Window", ["incognito"]) > 0);
        Assert.Equal(-1, CommandMatch.Score("incognito", "New Private Window"));
    }

    [Fact]
    public void Case_and_extra_spaces_dont_matter()
    {
        Assert.True(CommandMatch.Score("  ZOOM   in ", "Zoom In") > 0);
    }

    [Fact]
    public void A_single_letter_matches_nothing()
    {
        Assert.Equal(-1, CommandMatch.Score("p", "Print…"));
        Assert.Equal(-1, CommandMatch.Score("", "Print…"));
    }

    [Fact]
    public void Starting_the_name_beats_a_word_inside_it()
    {
        Assert.True(CommandMatch.Score("cl", "Close Tab") > CommandMatch.Score("cl", "Reopen Closed Tab"));
    }

    [Fact]
    public void Starting_the_name_is_a_strong_match()
    {
        Assert.True(CommandMatch.IsStrong("pin to", "Pin to Reading List"));
        Assert.False(CommandMatch.IsStrong("tools", "Developer Tools"));
        Assert.True(CommandMatch.IsStrong("pin", "Pin Tab"));
        Assert.False(CommandMatch.IsStrong("p", "Pin Tab"));
        Assert.False(CommandMatch.IsStrong("pin xyz", "Pin Tab"));
        Assert.True(CommandMatch.IsStrong("new doc", "New Google Doc"));
    }

    [Fact]
    public void More_of_the_name_typed_scores_higher()
    {
        Assert.True(CommandMatch.Score("zoom o", "Zoom Out") > CommandMatch.Score("zoom", "Zoom Out"));
    }
}
