using Drift;
using Xunit;

public class CustomIconTests
{
    [Fact]
    public void A_symbol_is_one_of_the_icon_font_s_characters()
    {
        Assert.True(CustomIcon.IsSymbol(""));
    }

    [Fact]
    public void Anything_else_is_an_emoji()
    {
        Assert.False(CustomIcon.IsSymbol("🚀"));
        Assert.False(CustomIcon.IsSymbol("❤️"));
        Assert.False(CustomIcon.IsSymbol(""));
    }

    [Fact]
    public void Drift_s_own_icons_are_symbols_drawn_in_its_font()
    {
        // Ionicons, in Drift Icons, past the Segoe Fluent Icons characters already saved on folders and tabs.
        var star = PickerIcons.Symbols[0].Icon;
        Assert.True(CustomIcon.IsSymbol(star));
        Assert.True(CustomIcon.IsDriftSymbol(star));
        Assert.False(CustomIcon.IsDriftSymbol(""));
        Assert.False(CustomIcon.IsDriftSymbol("🚀"));
    }

    [Fact]
    public void Every_picker_symbol_is_one_of_drift_s_own()
    {
        Assert.NotEmpty(PickerIcons.Symbols);
        Assert.All(PickerIcons.Symbols, s => Assert.True(CustomIcon.IsDriftSymbol(s.Icon)));
    }
}
