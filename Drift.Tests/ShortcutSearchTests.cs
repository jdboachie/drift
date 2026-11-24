using Drift;
using Xunit;

public class ShortcutSearchTests
{
    [Fact]
    public void Nothing_typed_shows_them_all() =>
        Assert.True(ShortcutSearch.Matches("  ", "New tab", "Tabs", "Ctrl+T"));

    [Fact]
    public void A_word_of_its_name_finds_it_in_any_case() =>
        Assert.True(ShortcutSearch.Matches("REOPEN", "Reopen closed tab", "Tabs", "Ctrl+Shift+T"));

    [Fact]
    public void The_start_of_a_word_is_enough() =>
        Assert.True(ShortcutSearch.Matches("hist", "History", "Library", "Ctrl+H"));

    [Fact]
    public void Its_group_finds_it() =>
        Assert.True(ShortcutSearch.Matches("library", "Downloads", "Library", "Ctrl+J"));

    [Fact]
    public void Its_keys_find_it_however_they_are_typed()
    {
        Assert.True(ShortcutSearch.Matches("ctrl+shift+t", "Reopen closed tab", "Tabs", "Ctrl+Shift+T"));
        Assert.True(ShortcutSearch.Matches("ctrl shift t", "Reopen closed tab", "Tabs", "Ctrl+Shift+T"));
    }

    [Fact]
    public void Every_word_has_to_match() =>
        Assert.False(ShortcutSearch.Matches("close window", "Close tab", "Tabs", "Ctrl+W"));

    [Fact]
    public void A_word_in_the_middle_of_another_doesnt_count() =>
        // "ab" isn't a word of "Close tab": typing it shouldn't keep every tab shortcut.
        Assert.False(ShortcutSearch.Matches("ab", "Close tab", "Tabs", "Ctrl+W"));
}
