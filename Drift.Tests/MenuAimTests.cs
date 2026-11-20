using Xunit;

namespace Drift.Tests;

public class MenuAimTests
{
    // The pointer left the header at (60, 120); the list's near edge is at x 230, from y 100 to 440.
    private static bool Toward(double x, double y) => MenuAim.Toward(x, y, fromX: 60, fromY: 120, edgeX: 230, topY: 100, bottomY: 440);

    [Fact]
    public void Heading_diagonally_for_the_lists_last_item_is_on_its_way()
    {
        Assert.True(Toward(150, 280));
        Assert.True(Toward(220, 420));
    }

    [Fact]
    public void Straight_across_to_the_list_is_on_its_way() => Assert.True(Toward(150, 118));

    [Fact]
    public void Dropping_down_the_sidebar_instead_is_not()
    {
        Assert.False(Toward(150, 330));
        Assert.False(Toward(70, 300));
    }

    [Fact]
    public void Going_back_the_other_way_is_not() => Assert.False(Toward(40, 120));

    [Fact]
    public void Above_the_list_is_not() => Assert.False(Toward(200, 60));
}
