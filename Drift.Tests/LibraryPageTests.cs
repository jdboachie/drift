
namespace Drift.Tests;

/// <summary>The Library button opens Downloads while there's one to see; otherwise the page you were on.</summary>
public class LibraryPageTests
{
    [Fact]
    public void A_download_still_going_opens_Downloads() =>
        Assert.Equal("downloads", LibraryPage.ToOpen(last: "history", unseen: false, running: true));

    [Fact]
    public void A_download_finished_and_not_seen_opens_Downloads() =>
        Assert.Equal("downloads", LibraryPage.ToOpen(last: "bookmarks", unseen: true, running: false));

    [Fact]
    public void Otherwise_the_page_you_were_on() =>
        Assert.Equal("history", LibraryPage.ToOpen(last: "history", unseen: false, running: false));
}
