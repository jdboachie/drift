using System.Linq;
using Drift;
using Xunit;

public class HistoryLinesTests
{
    [Fact]
    public void Each_day_is_a_heading_then_its_pages_in_one_list()
    {
        var found = new HistoryView.Result<string>(new[]
        {
            new HistoryView.Day<string>("Today", new[] { "a", "b" }),
            new HistoryView.Day<string>("Yesterday", new[] { "c" }),
        }, HasMore: false);

        var lines = HistoryView.Lines(found);

        Assert.Equal(new[] { "Today", "a", "b", "Yesterday", "c" }, lines.Select(l => l.Day ?? l.Entry).ToArray());
        // The first heading sits close to the search box; later ones leave a gap above.
        Assert.True(lines[0].FirstDay);
        Assert.False(lines[3].FirstDay);
    }

    [Fact]
    public void Nothing_found_is_no_lines()
    {
        Assert.Empty(HistoryView.Lines(new HistoryView.Result<string>(System.Array.Empty<HistoryView.Day<string>>(), false)));
    }
}
