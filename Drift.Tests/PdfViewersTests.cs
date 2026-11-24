using Drift;
using Xunit;

public class PdfViewersTests
{
    private const string ViewerUrl = "chrome-extension://mhjfbmdgcfjbbpaeojofohoefgiehjai/edge_pdf/index.html";

    [Fact]
    public void The_viewer_is_a_webview_on_edge_pdf() =>
        Assert.Equal(PdfTarget.Viewer, PdfViewers.Classify("webview", ViewerUrl));

    [Fact]
    public void A_viewer_starts_blank() =>
        Assert.Equal(PdfTarget.Blank, PdfViewers.Classify("other", ""));

    [Theory]
    [InlineData("page", "file:///C:/docs/test.pdf")]
    [InlineData("iframe", "file:///C:/docs/test.pdf")]
    [InlineData("webview", "chrome-extension://abc/popup.html")]
    [InlineData("other", "chrome://newtab/")]
    [InlineData("service_worker", "")]
    public void Anything_else_is_left_alone(string type, string url) =>
        Assert.Equal(PdfTarget.None, PdfViewers.Classify(type, url));

    [Fact]
    public void Missing_details_are_left_alone() =>
        Assert.Equal(PdfTarget.None, PdfViewers.Classify(null, null));

    [Theory]
    [InlineData("*test.pdf", true)]
    [InlineData("test.pdf", false)]
    [InlineData("", false)]
    public void Edits_show_as_a_star_before_the_title(string title, bool edited) =>
        Assert.Equal(edited, PdfViewers.HasUnsavedEdits(title));
}
