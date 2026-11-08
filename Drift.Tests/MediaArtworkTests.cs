using Drift;
using Xunit;

namespace Drift.Tests;

public class MediaArtworkTests
{
    [Fact]
    public void Picks_the_smallest_at_least_as_big_as_wanted()
    {
        var art = new[] { ("https://a/1000.jpg", "1000x1000"), ("https://a/96.jpg", "96x96"), ("https://a/256.jpg", "256x256"), ("https://a/48.jpg", "48x48") };
        Assert.Equal("https://a/96.jpg", MediaArtwork.Pick(art, 80));
    }

    [Fact]
    public void Falls_back_to_the_largest_when_all_are_small()
    {
        var art = new[] { ("https://a/32.jpg", "32x32"), ("https://a/64.jpg", "64x64") };
        Assert.Equal("https://a/64.jpg", MediaArtwork.Pick(art, 80));
    }

    [Fact]
    public void Reads_several_sizes_and_any()
    {
        Assert.Equal("https://a/b.png", MediaArtwork.Pick(new[] { ("https://a/a.png", "16x16"), ("https://a/b.png", "32x32 512x512") }, 80));
        Assert.Equal("https://a/s.svg", MediaArtwork.Pick(new[] { ("https://a/s.svg", "any"), ("https://a/16.png", "16x16") }, 80));
    }

    [Fact]
    public void Takes_one_without_sizes_when_nothing_else()
    {
        Assert.Equal("https://a/x.jpg", MediaArtwork.Pick(new[] { ("https://a/x.jpg", (string?)null) }, 80));
        Assert.Equal("https://a/32.jpg", MediaArtwork.Pick(new[] { ("https://a/x.jpg", (string?)""), ("https://a/32.jpg", "32x32") }, 80));
    }

    [Fact]
    public void Only_web_addresses_load()
    {
        var art = new[] { ("blob:https://a/1", "512x512"), ("data:image/png;base64,AA", "256x256"), ("https://a/96.jpg", "96x96") };
        Assert.Equal("https://a/96.jpg", MediaArtwork.Pick(art, 80));
        Assert.Null(MediaArtwork.Pick(new[] { ("blob:https://a/1", "512x512") }, 80));
        Assert.Null(MediaArtwork.Pick(System.Array.Empty<(string, string?)>(), 80));
    }
}
