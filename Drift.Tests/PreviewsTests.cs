using Xunit;

namespace Drift.Tests;

public class PreviewsTests
{
    [Fact]
    public void Trouble_says_what_and_owns_it()
    {
        Assert.Equal("You're signed out of Google Calendar", Previews.Trouble(PreviewKind.GoogleCalendar, hasTab: true, PreviewState.SignedOut));
        Assert.Equal("We couldn't reach Outlook", Previews.Trouble(PreviewKind.OutlookMail, hasTab: true, PreviewState.Failed));
        Assert.Equal("We couldn't reach Outlook", Previews.Trouble(PreviewKind.OutlookCalendar, hasTab: true, PreviewState.Failed));
    }

    [Fact]
    public void Nothing_to_say_when_all_is_well_or_nothing_is_pinned()
    {
        Assert.Null(Previews.Trouble(PreviewKind.Gmail, hasTab: true, PreviewState.Ready));
        Assert.Null(Previews.Trouble(PreviewKind.Gmail, hasTab: true, PreviewState.Loading));
        Assert.Null(Previews.Trouble(PreviewKind.Gmail, hasTab: false, PreviewState.SignedOut));
    }
}
