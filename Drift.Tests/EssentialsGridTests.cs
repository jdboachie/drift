using Drift;
using Xunit;

public class EssentialsGridTests
{
    // Tiles are 48 tall with 5 around them: a cell is square at 58 wide.

    [Fact]
    public void As_many_across_as_fit_without_a_tile_going_narrower_than_it_is_tall()
    {
        // The default sidebar is 196 inside: three square tiles fit, four wouldn't.
        Assert.Equal(3, EssentialsGrid.MostAcross(196));
        Assert.Equal(4, EssentialsGrid.MostAcross(240));
        Assert.Equal(5, EssentialsGrid.MostAcross(295));
        Assert.Equal(6, EssentialsGrid.MostAcross(350));
    }

    [Fact]
    public void A_column_waits_until_its_tiles_would_be_square()
    {
        // Just short of four, five and six cells of 58.
        Assert.Equal(3, EssentialsGrid.MostAcross(230));
        Assert.Equal(4, EssentialsGrid.MostAcross(288));
        Assert.Equal(5, EssentialsGrid.MostAcross(346));
    }

    [Fact]
    public void Widening_adds_a_column_at_a_time()
    {
        // Six Essentials: three, four, five, then six across as the sidebar widens, never straight from three to six.
        Assert.Equal(3, EssentialsGrid.Columns(6, 200));
        Assert.Equal(4, EssentialsGrid.Columns(6, 240));
        Assert.Equal(5, EssentialsGrid.Columns(6, 300));
        Assert.Equal(6, EssentialsGrid.Columns(6, 355));
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

    // ── Where each tile goes: a full row's share the width evenly, and so do a short last row's ──

    [Fact]
    public void A_full_row_shares_the_width_evenly()
    {
        Assert.Equal(new EssentialsGrid.Cell(0, 0, 70), EssentialsGrid.CellOf(0, 5, 3, 210));
        Assert.Equal(new EssentialsGrid.Cell(70, 0, 70), EssentialsGrid.CellOf(1, 5, 3, 210));
        Assert.Equal(new EssentialsGrid.Cell(140, 0, 70), EssentialsGrid.CellOf(2, 5, 3, 210));
    }

    [Fact]
    public void A_short_last_row_fills_the_width_too()
    {
        // Five across three: the last two are half the width each.
        Assert.Equal(new EssentialsGrid.Cell(0, 58, 105), EssentialsGrid.CellOf(3, 5, 3, 210));
        Assert.Equal(new EssentialsGrid.Cell(105, 58, 105), EssentialsGrid.CellOf(4, 5, 3, 210));
    }

    [Fact]
    public void A_last_tile_on_its_own_takes_the_whole_row()
    {
        Assert.Equal(new EssentialsGrid.Cell(0, 116, 210), EssentialsGrid.CellOf(6, 7, 3, 210));
    }

    [Fact]
    public void Fewer_tiles_than_columns_still_fill_the_row()
    {
        Assert.Equal(new EssentialsGrid.Cell(0, 0, 210), EssentialsGrid.CellOf(0, 1, 2, 210));
        Assert.Equal(new EssentialsGrid.Cell(70, 0, 70), EssentialsGrid.CellOf(1, 3, 3, 210));
    }

    [Fact]
    public void Rows_are_as_many_as_the_tiles_need()
    {
        Assert.Equal(0, EssentialsGrid.Rows(0, 3));
        Assert.Equal(1, EssentialsGrid.Rows(3, 3));
        Assert.Equal(2, EssentialsGrid.Rows(4, 3));
    }

    [Fact]
    public void The_slot_under_a_point_in_a_full_row()
    {
        Assert.Equal(0, EssentialsGrid.SlotAt(10, 20, 5, 3, 210));
        Assert.Equal(2, EssentialsGrid.SlotAt(200, 20, 5, 3, 210));
    }

    [Fact]
    public void The_slot_under_a_point_in_a_short_row_goes_by_its_wider_tiles()
    {
        // At x 80 in the last row: still the first of its two 105-wide tiles, not the second column.
        Assert.Equal(3, EssentialsGrid.SlotAt(80, 70, 5, 3, 210));
        Assert.Equal(4, EssentialsGrid.SlotAt(110, 70, 5, 3, 210));
    }

    [Fact]
    public void A_point_outside_the_grid_takes_the_nearest_slot()
    {
        Assert.Equal(0, EssentialsGrid.SlotAt(-30, -30, 5, 3, 210));
        Assert.Equal(4, EssentialsGrid.SlotAt(400, 500, 5, 3, 210));
    }
}
