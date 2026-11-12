using Drift;
using Xunit;

public class RectCarveTests
{
    [Fact]
    public void Apart_it_stays_whole()
    {
        Assert.Equal([new Area(0, 0, 10, 10)], Areas.Carve(new Area(0, 0, 10, 10), new Area(20, 0, 5, 5)));
    }

    [Fact]
    public void The_top_strip_loses_its_right_end()
    {
        // The window's top margin, with the bar over its right end.
        Assert.Equal([new Area(200, 0, 862, 8)], Areas.Carve(new Area(200, 0, 1000, 8), new Area(1062, 0, 138, 32)));
    }

    [Fact]
    public void The_right_strip_starts_below_it()
    {
        Assert.Equal([new Area(1192, 32, 8, 768)], Areas.Carve(new Area(1192, 0, 8, 800), new Area(1062, 0, 138, 32)));
    }

    [Fact]
    public void A_hole_in_the_middle_leaves_four_sides()
    {
        var parts = Areas.Carve(new Area(0, 0, 10, 10), new Area(4, 4, 2, 2));
        Assert.Equal(4, parts.Count);
        Assert.Equal(100 - 4, parts.Sum(p => p.Width * p.Height));
    }

    [Fact]
    public void Covered_it_is_gone()
    {
        Assert.Empty(Areas.Carve(new Area(2, 2, 4, 4), new Area(0, 0, 10, 10)));
    }
}
