using Drift;
using Xunit;

public class PageColorTests
{
    [Fact]
    public void The_pages_background_is_the_colour_most_of_its_edges_share()
    {
        // Corners and edge midpoints: a logo or a scroll bar in one of them doesn't win.
        Assert.Equal(0xFFFFFFFFu, PageColor.Pick([0xFFFFFFFFu, 0xFF3366CCu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFEEEEEEu]));
    }

    [Fact]
    public void With_no_colour_shared_the_first_is_taken()
    {
        Assert.Equal(0xFF000000u, PageColor.Pick([0xFF000000u, 0xFF111111u, 0xFF222222u]));
    }

    [Fact]
    public void Without_samples_there_is_no_colour()
    {
        Assert.Null(PageColor.Pick([]));
    }

    [Fact]
    public void Samples_are_taken_from_the_corners_and_edge_middles_just_inside()
    {
        Assert.Equal(
            [(2, 2), (97, 2), (2, 47), (97, 47), (49, 2), (49, 47), (2, 24), (97, 24)],
            PageColor.SamplePoints(100, 50));
    }
}
