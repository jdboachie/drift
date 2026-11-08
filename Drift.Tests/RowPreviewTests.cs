namespace Drift.Tests;

public class RowPreviewTests
{
    // Rows 36 high: a at 0, b at 36, c at 72, d at 108.
    private static readonly RowLayout[] Abcd =
        { new("a", 36), new("b", 36), new("c", 36), new("d", 36) };

    private static HashSet<object> Dragging(params string[] keys) => new(keys);

    [Fact]
    public void Lands_before_the_first_row_whose_middle_is_below_the_pointer()
    {
        var land = RowPreview.Landing(Abcd, Dragging(), y: 50);

        Assert.Equal(1, land.Slot);   // past a's middle (18), not b's (54)
        Assert.Equal(1, land.Gap);
    }

    [Fact]
    public void A_row_dragged_within_its_list_lands_among_the_others()
    {
        // b lifted: the others close up to a 0, c 36, d 72. 60 is past c's middle.
        var land = RowPreview.Landing(Abcd, Dragging("b"), y: 60);

        Assert.Equal(2, land.Slot);
        // In the list's own indexing (b still in it): before d.
        Assert.Equal(3, land.Gap);
    }

    [Fact]
    public void Past_the_end_lands_last()
    {
        var land = RowPreview.Landing(Abcd, Dragging(), y: 500);

        Assert.Equal(4, land.Slot);
        Assert.Equal(4, land.Gap);
    }

    [Fact]
    public void Rows_after_the_slot_make_room_and_the_dragged_row_sits_in_it()
    {
        var tops = RowPreview.Tops(Abcd, Dragging("d"), slot: 1);

        Assert.Equal(0, tops["a"]);
        Assert.Equal(36, tops["d"]);
        Assert.Equal(72, tops["b"]);
        Assert.Equal(108, tops["c"]);
    }

    [Fact]
    public void A_row_from_another_list_opens_a_gap_its_height()
    {
        var tops = RowPreview.Tops(Abcd, Dragging(), slot: 2, arriving: 40);

        Assert.Equal(36, tops["b"]);
        Assert.Equal(72 + 40, tops["c"]);
    }

    [Fact]
    public void With_no_slot_the_others_close_up_and_the_dragged_row_is_gone()
    {
        var tops = RowPreview.Tops(Abcd, Dragging("b"), slot: null);

        Assert.False(tops.ContainsKey("b"));
        Assert.Equal(36, tops["c"]);
        Assert.Equal(72, tops["d"]);
    }

    [Fact]
    public void A_dragged_folder_moves_with_its_rows()
    {
        var tops = RowPreview.Tops(Abcd, Dragging("c", "d"), slot: 0);

        Assert.Equal(0, tops["c"]);
        Assert.Equal(36, tops["d"]);
        Assert.Equal(72, tops["a"]);
    }

    [Fact]
    public void The_middle_of_a_folder_header_is_onto_it()
    {
        var rows = new[] { new RowLayout("a", 36), new RowLayout("folder", 36, IsHeader: true) };

        Assert.Equal("folder", RowPreview.Landing(rows, Dragging(), y: 54).Onto);
        Assert.Null(RowPreview.Landing(rows, Dragging(), y: 40).Onto);   // its top edge: the gap above it
        Assert.Null(RowPreview.Landing(rows, Dragging(), y: 18).Onto);   // a isn't a folder
    }
}
