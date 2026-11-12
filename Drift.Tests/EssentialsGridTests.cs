using Drift;
using Xunit;

public class EssentialsGridTests
{
    // Tiles are 46 tall with 4 around them: a cell is square at 54 wide.

    [Fact]
    public void As_many_across_as_fit_without_a_tile_going_narrower_than_it_is_tall()
    {
        // The default sidebar is 196 inside: three square tiles fit, four wouldn't.
        Assert.Equal(3, EssentialsGrid.MostAcross(196));
        Assert.Equal(4, EssentialsGrid.MostAcross(216));
        Assert.Equal(5, EssentialsGrid.MostAcross(270));
        Assert.Equal(6, EssentialsGrid.MostAcross(324));
    }

    [Fact]
    public void Widening_adds_a_column_at_a_time()
    {
        // Six Essentials: three, four, five, then six across as the sidebar widens, never straight from three to six.
        Assert.Equal(3, EssentialsGrid.Columns(6, 200));
        Assert.Equal(4, EssentialsGrid.Columns(6, 230));
        Assert.Equal(5, EssentialsGrid.Columns(6, 280));
        Assert.Equal(6, EssentialsGrid.Columns(6, 330));
    }

    [Fact]
    public void Narrowing_takes_them_away_again()
    {
        Assert.Equal(4, EssentialsGrid.Columns(6, 250));
        Assert.Equal(3, EssentialsGrid.Columns(6, 180));
    }

    [Fact]
    public void Never_more_than_six_across()
    {
        Assert.Equal(6, EssentialsGrid.MostAcross(500));
        Assert.Equal(6, EssentialsGrid.Columns(12, 500));
    }

    [Fact]
    public void No_wider_than_there_are_tiles()
    {
        Assert.Equal(3, EssentialsGrid.Columns(3, 400));
    }

    [Fact]
    public void One_or_two_are_two_across()
    {
        Assert.Equal(2, EssentialsGrid.Columns(1, 400));
        Assert.Equal(2, EssentialsGrid.Columns(2, 400));
    }

    [Fact]
    public void Never_fewer_than_two()
    {
        Assert.Equal(2, EssentialsGrid.MostAcross(40));
        Assert.Equal(2, EssentialsGrid.Columns(6, 40));
    }
}
