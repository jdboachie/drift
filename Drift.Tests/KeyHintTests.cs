using Drift;
using Xunit;

public class KeyHintTests
{
    [Fact]
    public void A_shortcut_reads_as_its_keys_side_by_side()
    {
        // Like Arc's "⌘ T" beside New Tab, in Windows keys.
        Assert.Equal("Ctrl T", KeyHint.From("Ctrl+T"));
        Assert.Equal("Ctrl Shift T", KeyHint.From("Ctrl+Shift+T"));
        Assert.Equal("F6", KeyHint.From("F6"));
    }

    [Fact]
    public void No_shortcut_no_hint()
    {
        Assert.Equal("", KeyHint.From(""));
        Assert.Equal("", KeyHint.From(null));
    }
}
