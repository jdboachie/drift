using Drift;
using Xunit;

namespace Drift.Tests;

public class GoogleCalendarLinksTests
{
    [Fact]
    public void Knows_a_calendar_page()
    {
        Assert.True(GoogleCalendarLinks.IsCalendar(new Uri("https://calendar.google.com/calendar/u/0/r")));
        Assert.False(GoogleCalendarLinks.IsCalendar(new Uri("https://mail.google.com/mail/u/0/")));
        Assert.False(GoogleCalendarLinks.IsCalendar(null));
    }

    [Fact]
    public void The_account_comes_from_the_address()
    {
        Assert.Equal(1, GoogleCalendarLinks.Account(new Uri("https://calendar.google.com/calendar/u/1/r/week")));
        Assert.Equal(0, GoogleCalendarLinks.Account(new Uri("https://calendar.google.com/calendar/r")));
        Assert.Equal(2, GoogleCalendarLinks.Account(new Uri("https://calendar.google.com/calendar/u/2")));
    }

    [Fact]
    public void Links_are_for_that_account()
    {
        Assert.Equal("/calendar/u/1/exporticalzip", GoogleCalendarLinks.ExportPath(1));
        Assert.Equal("https://calendar.google.com/calendar/u/0/r/eventedit/abc", GoogleCalendarLinks.Event("abc", 0).ToString());
        Assert.Equal("https://calendar.google.com/calendar/u/3/r/eventedit", GoogleCalendarLinks.NewEvent(3).ToString());
    }
}
