using Drift;
using Xunit;

public class PickerIconsTests
{
    [Fact]
    public void The_emoji_list_is_read_a_line_each_by_group()
    {
        var emoji = PickerIcons.ParseEmoji("Smileys\t😀\tgrinning face smile happy\nFood & drink\t☕\thot beverage coffee\n\n");
        Assert.Equal(2, emoji.Count);
        Assert.Equal(("Smileys", "😀", "grinning face smile happy"), emoji[0]);
        Assert.Equal("Food & drink", emoji[1].Group);
        Assert.Equal("☕", emoji[1].Icon);
    }

    [Fact]
    public void A_broken_line_is_skipped()
    {
        Assert.Empty(PickerIcons.ParseEmoji("just words\n"));
    }

    [Fact]
    public void Search_finds_by_the_start_of_any_word()
    {
        Assert.True(PickerIcons.Matches("hot beverage coffee", "cof"));
        Assert.True(PickerIcons.Matches("hot beverage coffee", "Coffee"));
        Assert.False(PickerIcons.Matches("hot beverage coffee", "fee"));
    }

    [Fact]
    public void Search_needs_every_word_typed()
    {
        Assert.True(PickerIcons.Matches("grinning face smile", "smile face"));
        Assert.False(PickerIcons.Matches("grinning face smile", "smile cat"));
    }

    [Fact]
    public void An_empty_search_finds_everything()
    {
        Assert.True(PickerIcons.Matches("anything", "  "));
    }
}
