using Drift;
using Xunit;

public class PeekLinkTests
{
    [Fact]
    public void A_web_link_alt_clicked_opens_in_peek()
    {
        Assert.Equal(new System.Uri("https://example.com/a?b=1"), PeekLink.Parse("https://example.com/a?b=1"));
        Assert.NotNull(PeekLink.Parse("http://example.com"));
    }

    [Fact]
    public void Anything_else_does_not()
    {
        // Scripts, mail, files and nonsense have no page to peek at.
        Assert.Null(PeekLink.Parse("javascript:alert(1)"));
        Assert.Null(PeekLink.Parse("mailto:someone@example.com"));
        Assert.Null(PeekLink.Parse("file:///C:/Windows/win.ini"));
        Assert.Null(PeekLink.Parse("not a link"));
        Assert.Null(PeekLink.Parse(""));
    }
}
