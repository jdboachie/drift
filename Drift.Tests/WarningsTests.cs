using Drift;

public class WarningsTests
{
    [Fact]
    public void Closing_with_nothing_to_lose_asks_nothing()
    {
        Assert.Null(Warnings.CloseWindow(isPrivate: false, downloads: 0, privateWindows: 0, droppedTabs: 0, unsavedPages: 0));
    }

    [Fact]
    public void Closing_says_each_thing_that_would_be_lost()
    {
        var ask = Warnings.CloseWindow(isPrivate: false, downloads: 2, privateWindows: 1, droppedTabs: 8, unsavedPages: 1)!;
        Assert.Equal("Close Drift?", ask.Title);
        Assert.Equal(
            "2 downloads will stop.\nYour private window will close.\n8 tabs won't be kept.\n1 page has changes that may not be saved.",
            ask.Body);
        Assert.Equal("Close", ask.Action);
    }

    [Fact]
    public void Closing_counts_in_the_singular_and_plural()
    {
        var ask = Warnings.CloseWindow(isPrivate: false, downloads: 1, privateWindows: 3, droppedTabs: 1, unsavedPages: 2)!;
        Assert.Equal(
            "1 download will stop.\nYour 3 private windows will close.\n1 tab won't be kept.\n2 pages have changes that may not be saved.",
            ask.Body);
    }

    [Fact]
    public void A_private_window_is_asked_about_as_itself()
    {
        var ask = Warnings.CloseWindow(isPrivate: true, downloads: 1, privateWindows: 0, droppedTabs: 0, unsavedPages: 0)!;
        Assert.Equal("Close this private window?", ask.Title);
        Assert.Equal("1 download will stop.", ask.Body);
    }

    [Fact]
    public void Leaving_one_page_names_its_site()
    {
        var ask = Warnings.LeavePages(["mail.google.com"])!;
        Assert.Equal("Leave mail.google.com?", ask.Title);
        Assert.Equal("Changes you made may not be saved.", ask.Body);
        Assert.Equal("Leave", ask.Action);
    }

    [Fact]
    public void Leaving_several_pages_counts_them()
    {
        Assert.Equal("Leave 2 pages?", Warnings.LeavePages(["docs.google.com", "github.com"])!.Title);
        Assert.Null(Warnings.LeavePages([]));
    }

    [Fact]
    public void Clearing_data_says_what_and_from_when()
    {
        var ask = Warnings.ClearData(["Browsing history", "Cookies and site data", "Saved passwords"], "Last hour")!;
        Assert.Equal("Clear browsing data?", ask.Title);
        Assert.Equal("Browsing history, cookies and site data, and saved passwords, from the last hour.", ask.Body);
        Assert.Equal("Clear", ask.Action);
    }

    [Fact]
    public void Clearing_one_kind_for_all_time()
    {
        Assert.Equal("Cached images and files, from all time.", Warnings.ClearData(["Cached images and files"], "All time")!.Body);
        Assert.Null(Warnings.ClearData([], "All time"));
    }

    [Fact]
    public void Resetting_shortcuts_asks_only_when_some_were_changed()
    {
        Assert.Null(Warnings.ResetShortcuts(0));
        var ask = Warnings.ResetShortcuts(3)!;
        Assert.Equal("Reset all shortcuts?", ask.Title);
        Assert.Equal("The 3 you changed go back to how they were.", ask.Body);
        Assert.Equal("The one you changed goes back to how it was.", Warnings.ResetShortcuts(1)!.Body);
    }

    [Fact]
    public void Removing_an_extension_names_it()
    {
        var ask = Warnings.RemoveExtension("Dark Reader");
        Assert.Equal("Remove “Dark Reader”?", ask.Title);
        Assert.Equal("Its settings go with it.", ask.Body);
        Assert.Equal("Remove", ask.Action);
    }
}
