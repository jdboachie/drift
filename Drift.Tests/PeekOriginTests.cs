using Drift;

public class PeekOriginTests
{
    [Fact]
    public void A_press_without_the_links_box_has_only_the_point()
    {
        var press = PeekOrigin.Parse("120.5,40,1280");
        Assert.Equal(new LinkPressReport(120.5, 40, 1280, null), press);
    }

    [Fact]
    public void A_press_with_the_links_box()
    {
        var press = PeekOrigin.Parse("120,40,1280,100,30,200,18.5");
        Assert.Equal(new LinkPressReport(120, 40, 1280, new Area(100, 30, 200, 18.5)), press);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1,2")]
    [InlineData("1,2,0")]
    [InlineData("a,2,1280")]
    [InlineData("1,2,1280,3,4,5")]
    public void Anything_else_is_no_press(string args) => Assert.Null(PeekOrigin.Parse(args));

    [Fact]
    public void A_box_with_no_size_is_left_out()
    {
        Assert.Null(PeekOrigin.Parse("120,40,1280,100,30,0,18")!.Value.Link);
    }

    [Fact]
    public void The_card_starts_covering_the_link()
    {
        var start = PeekOrigin.From(link: new Area(100, 50, 200, 20), card: new Area(10, 10, 1000, 800));
        Assert.Equal(0.2, start.ScaleX, 6);
        Assert.Equal(0.025, start.ScaleY, 6);
        Assert.Equal(90, start.OffsetX, 6);
        Assert.Equal(40, start.OffsetY, 6);
    }

    [Fact]
    public void A_tiny_link_starts_at_least_a_few_pixels_across_about_its_middle()
    {
        var start = PeekOrigin.From(link: new Area(100, 50, 1, 1), card: new Area(0, 0, 1000, 800), minSide: 4);
        Assert.Equal(4 / 1000.0, start.ScaleX, 6);
        Assert.Equal(4 / 800.0, start.ScaleY, 6);
        Assert.Equal(98.5, start.OffsetX, 6);
        Assert.Equal(48.5, start.OffsetY, 6);
    }

    [Fact]
    public void A_link_partly_out_of_sight_is_cut_to_what_shows()
    {
        Assert.Equal(new Area(0, 700, 150, 100), PeekOrigin.Clip(new Area(-50, 700, 200, 150), new Area(0, 0, 1000, 800)));
    }

    [Fact]
    public void A_link_out_of_sight_has_no_box()
    {
        Assert.Null(PeekOrigin.Clip(new Area(0, 900, 200, 20), new Area(0, 0, 1000, 800)));
    }
}
