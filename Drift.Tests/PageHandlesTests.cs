using Drift;

public class PageHandlesTests
{
    // Two pages below an 8px gap at the window's top: left 0–600, right 608–1000.
    private static readonly Area Left = new(0, 8, 600, 700);
    private static readonly Area Right = new(608, 8, 392, 700);

    [Fact]
    public void Away_from_the_pages_no_handle_shows()
    {
        Assert.Equal((false, false), PageHandles.Shown(null, Left, Right));
    }

    [Fact]
    public void Only_the_page_under_the_pointer_shows_its_handle()
    {
        Assert.Equal((false, true), PageHandles.Shown((800, 300), Left, Right));
        Assert.Equal((true, false), PageHandles.Shown((100, 300), Left, Right));
    }

    [Fact]
    public void Reaching_up_for_a_handle_in_the_gap_above_its_page_keeps_it()
    {
        Assert.Equal((false, true), PageHandles.Shown((800, 2), Left, Right));
    }

    [Fact]
    public void Between_the_pages_or_below_them_is_neither()
    {
        Assert.Equal((false, false), PageHandles.Shown((604, 300), Left, Right));
        Assert.Equal((false, false), PageHandles.Shown((800, 720), Left, Right));
    }
}
