namespace Drift.Tests;

public class SlidePlanTests
{
    private static (object, double)[] Rows(params (string Key, double Y)[] rows) =>
        rows.Select(r => ((object)r.Key, r.Y)).ToArray();

    [Fact]
    public void Rows_pushed_down_slide_from_where_they_were()
    {
        var before = Rows(("a", 0), ("b", 36));
        var after = Rows(("new", 0), ("a", 36), ("b", 72));

        var plan = SlidePlan.For(before, after);

        Assert.Contains(new Slide("a", -36, Fade: false), plan);
        Assert.Contains(new Slide("b", -36, Fade: false), plan);
    }

    [Fact]
    public void Rows_that_stay_put_dont_move()
    {
        var before = Rows(("a", 0), ("b", 36), ("c", 72));
        var after = Rows(("a", 0), ("c", 36));

        var plan = SlidePlan.For(before, after);

        Assert.Equal(new[] { new Slide("c", 36, Fade: false) }, plan);
    }

    [Fact]
    public void A_dropped_tab_lands_where_it_was_dropped()
    {
        var before = Rows(("a", 0), ("b", 36), ("c", 72));
        var after = Rows(("c", 0), ("a", 36), ("b", 72));

        var plan = SlidePlan.For(before, after, except: "c");

        Assert.DoesNotContain(plan, s => Equals(s.Key, "c"));
        Assert.Contains(new Slide("a", -36, Fade: false), plan);
    }

    [Fact]
    public void A_new_row_appears_in_place()
    {
        var before = Rows(("a", 0));
        var after = Rows(("a", 0), ("new", 36));

        Assert.Empty(SlidePlan.For(before, after));
    }

    [Fact]
    public void Opening_a_folder_unfolds_its_rows_from_under_the_header()
    {
        // The header itself moves too (a folder above it opened): its rows start where it started.
        var before = Rows(("header", 40), ("next", 76));
        var after = Rows(("header", 0), ("x", 36), ("y", 72), ("next", 108));

        var plan = SlidePlan.For(before, after, unfold: true);

        Assert.Contains(new Slide("x", 40 - 36, Fade: true), plan);
        Assert.Contains(new Slide("y", 40 - 72, Fade: true), plan);
        Assert.Contains(new Slide("next", 76 - 108, Fade: false), plan);
    }

    [Fact]
    public void A_new_row_with_nothing_above_it_appears_in_place_even_when_unfolding()
    {
        var before = Rows(("a", 0));
        var after = Rows(("new", 0), ("a", 36));

        var plan = SlidePlan.For(before, after, unfold: true);

        Assert.Equal(new[] { new Slide("a", -36, Fade: false) }, plan);
    }
}
