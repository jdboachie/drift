using Drift;
using Xunit;

public class EssentialsGridTests
{
    [Fact]
    public void Up_to_four_sit_in_one_row()
    {
        Assert.Equal(3, EssentialsGrid.Columns(3));
        Assert.Equal(4, EssentialsGrid.Columns(4));
    }

    [Fact]
    public void One_or_two_are_two_across()
    {
        Assert.Equal(2, EssentialsGrid.Columns(1));
        Assert.Equal(2, EssentialsGrid.Columns(2));
    }

    [Fact]
    public void Rows_are_kept_even()
    {
        Assert.Equal(3, EssentialsGrid.Columns(5));
        Assert.Equal(3, EssentialsGrid.Columns(6));
        Assert.Equal(4, EssentialsGrid.Columns(7));
        Assert.Equal(4, EssentialsGrid.Columns(8));
        Assert.Equal(3, EssentialsGrid.Columns(9));
    }

    [Fact]
    public void Never_more_than_four_across()
    {
        Assert.Equal(4, EssentialsGrid.Columns(10));
        Assert.Equal(4, EssentialsGrid.Columns(12));
        Assert.Equal(4, EssentialsGrid.Columns(23));
    }
}
