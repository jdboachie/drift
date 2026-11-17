using System;
using Drift;
using Xunit;

public class MicrosoftSitesTests
{
    [Theory]
    [InlineData("login.microsoftonline.com")]
    [InlineData("login.live.com")]
    [InlineData("LOGIN.MICROSOFTONLINE.COM")]
    public void The_sign_in_pages(string host) => Assert.True(MicrosoftSites.IsSignIn(host));

    [Theory]
    [InlineData("outlook.office.com")]
    [InlineData("outlook.live.com")]
    public void Sites_are_not_sign_in_pages(string host) => Assert.False(MicrosoftSites.IsSignIn(host));

    [Theory]
    [InlineData("outlook.office.com")]
    [InlineData("outlook.office365.com")]
    [InlineData("outlook.live.com")]
    [InlineData("www.office.com")]
    [InlineData("www.microsoft365.com")]
    [InlineData("contoso.sharepoint.com")]
    [InlineData("contoso-my.sharepoint.com")]
    [InlineData("m365.cloud.microsoft")]
    [InlineData("teams.microsoft.com")]
    [InlineData("onedrive.live.com")]
    public void Microsoft_sites(string host) => Assert.True(MicrosoftSites.IsSite(host));

    [Theory]
    [InlineData("login.microsoftonline.com")]
    [InlineData("learn.microsoft.com")]
    [InlineData("example.com")]
    [InlineData("notoffice.com")]
    [InlineData("office.com.example.com")]
    [InlineData("")]
    public void Not_microsoft_sites(string host) => Assert.False(MicrosoftSites.IsSite(host));

    [Theory]
    [InlineData("login.microsoftonline.com")]
    [InlineData(".login.microsoftonline.com")]
    [InlineData(".live.com")]
    [InlineData("outlook.office.com")]
    [InlineData(".office.com")]
    [InlineData(".contoso.sharepoint.com")]
    [InlineData(".microsoftonline.com")]
    public void Cookies_that_sign_you_in(string domain) => Assert.True(MicrosoftSites.OwnsCookie(domain));

    [Theory]
    [InlineData(".microsoft.com")]
    [InlineData("learn.microsoft.com")]
    [InlineData(".google.com")]
    [InlineData("")]
    public void Cookies_left_alone(string domain) => Assert.False(MicrosoftSites.OwnsCookie(domain));

    [Fact]
    public void A_cookies_site_as_an_origin() =>
        Assert.Equal("https://contoso.sharepoint.com", MicrosoftSites.Origin(".contoso.sharepoint.com"));

    [Fact]
    public void A_cookie_for_a_whole_domain_has_no_one_origin() =>
        Assert.Null(MicrosoftSites.Origin(".live.com"));

    [Fact]
    public void A_sign_in_carrying_the_windows_account() =>
        Assert.True(MicrosoftSites.CarriesWindowsAccount(["Accept", "x-ms-RefreshTokenCredential", "Cookie"]));

    [Fact]
    public void Header_names_match_in_any_case() =>
        Assert.True(MicrosoftSites.CarriesWindowsAccount(["X-MS-REFRESHTOKENCREDENTIAL"]));

    [Fact]
    public void A_sign_in_without_it() =>
        Assert.False(MicrosoftSites.CarriesWindowsAccount(["Accept", "Cookie", "x-ms-gateway-slice"]));

    [Theory]
    [InlineData("outlook.cloud.microsoft", "Outlook")]
    [InlineData("outlook.office.com", "Outlook")]
    [InlineData("outlook.office365.com", "Outlook")]
    [InlineData("outlook.live.com", "Outlook")]
    [InlineData("m365.cloud.microsoft", "Microsoft 365")]
    [InlineData("www.microsoft365.com", "Microsoft 365")]
    [InlineData("www.office.com", "Microsoft 365")]
    [InlineData("contoso.sharepoint.com", "SharePoint")]
    [InlineData("contoso-my.sharepoint.com", "OneDrive")]
    [InlineData("onedrive.live.com", "OneDrive")]
    [InlineData("teams.microsoft.com", "Teams")]
    [InlineData("teams.cloud.microsoft", "Teams")]
    [InlineData("myaccount.microsoft.com", "My Account")]
    public void Sites_go_by_their_product_names(string host, string name) =>
        Assert.Equal(name, MicrosoftSites.ProductName(host));

    [Fact]
    public void A_site_without_a_product_name_goes_by_its_host() =>
        Assert.Equal("example.office.com", MicrosoftSites.ProductName("www.example.office.com"));

    [Fact]
    public void The_toast_names_the_site_without_www() =>
        Assert.Equal("office.com", MicrosoftSites.Name("www.office.com"));
}

public class SilentSignInTests
{
    private static readonly DateTime T0 = new(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);
    private static Uri U(string url) => new(url);

    [Fact]
    public void Through_the_sign_in_page_and_straight_back_is_silent()
    {
        var s = new SilentSignIn();
        Assert.Null(s.Navigating(U("https://outlook.office.com/mail/"), userAction: true, T0));
        Assert.Null(s.Navigating(U("https://login.microsoftonline.com/common/oauth2/authorize?x=1"), userAction: false, T0.AddMilliseconds(200)));
        Assert.Equal("outlook.office.com", s.Navigating(U("https://outlook.office.com/owa/"), userAction: false, T0.AddMilliseconds(900)));
    }

    [Fact]
    public void Personal_accounts_through_login_live_com()
    {
        var s = new SilentSignIn();
        s.Navigating(U("https://outlook.live.com/mail/"), userAction: true, T0);
        s.Navigating(U("https://login.live.com/login.srf"), userAction: false, T0.AddMilliseconds(100));
        Assert.Equal("outlook.live.com", s.Navigating(U("https://outlook.live.com/mail/0/"), userAction: false, T0.AddSeconds(1)));
    }

    [Fact]
    public void Several_steps_at_the_sign_in_pages_count_from_the_first()
    {
        var s = new SilentSignIn();
        s.Navigating(U("https://www.office.com/"), userAction: true, T0);
        s.Navigating(U("https://login.microsoftonline.com/a"), userAction: false, T0.AddMilliseconds(100));
        s.Navigating(U("https://login.microsoftonline.com/kmsi"), userAction: false, T0.AddSeconds(8));
        Assert.Null(s.Navigating(U("https://www.office.com/home"), userAction: false, T0.AddSeconds(11)));
    }

    [Fact]
    public void The_sign_in_page_can_take_a_few_seconds()
    {
        // office.com, measured: 4.7s at the sign-in page before it posted back.
        var s = new SilentSignIn();
        s.Navigating(U("https://m365.cloud.microsoft/login"), userAction: true, T0);
        s.Navigating(U("https://login.microsoftonline.com/common/oauth2/v2.0/authorize"), userAction: false, T0.AddMilliseconds(300));
        s.Navigating(U("https://login.microsoftonline.com/common/reprocess"), userAction: false, T0.AddSeconds(4.6));
        Assert.Equal("m365.cloud.microsoft", s.Navigating(U("https://m365.cloud.microsoft/landingv2"), userAction: false, T0.AddSeconds(5)));
    }

    [Fact]
    public void Too_slow_means_you_signed_in_yourself()
    {
        var s = new SilentSignIn();
        s.Navigating(U("https://outlook.office.com/mail/"), userAction: true, T0);
        s.Navigating(U("https://login.microsoftonline.com/a"), userAction: false, T0.AddMilliseconds(100));
        Assert.Null(s.Navigating(U("https://outlook.office.com/owa/"), userAction: false, T0.AddSeconds(15)));
    }

    [Fact]
    public void Doing_something_on_the_sign_in_page_isnt_silent()
    {
        var s = new SilentSignIn();
        s.Navigating(U("https://outlook.office.com/mail/"), userAction: true, T0);
        s.Navigating(U("https://login.microsoftonline.com/a"), userAction: false, T0.AddMilliseconds(100));
        s.Navigating(U("https://login.microsoftonline.com/login"), userAction: true, T0.AddMilliseconds(600));
        Assert.Null(s.Navigating(U("https://outlook.office.com/owa/"), userAction: false, T0.AddMilliseconds(900)));
    }

    [Fact]
    public void Input_on_the_sign_in_page_isnt_silent()
    {
        var s = new SilentSignIn();
        s.Navigating(U("https://outlook.office.com/mail/"), userAction: true, T0);
        s.Navigating(U("https://login.microsoftonline.com/a"), userAction: false, T0.AddMilliseconds(100));
        s.Input();
        Assert.Null(s.Navigating(U("https://outlook.office.com/owa/"), userAction: false, T0.AddMilliseconds(900)));
    }

    [Fact]
    public void Going_to_the_sign_in_page_yourself_isnt_silent()
    {
        var s = new SilentSignIn();
        s.Navigating(U("https://outlook.office.com/mail/"), userAction: true, T0);
        s.Navigating(U("https://login.microsoftonline.com/"), userAction: true, T0.AddMilliseconds(100));
        Assert.Null(s.Navigating(U("https://outlook.office.com/owa/"), userAction: false, T0.AddMilliseconds(900)));
    }

    [Fact]
    public void Only_from_a_microsoft_site()
    {
        var s = new SilentSignIn();
        s.Navigating(U("https://example.com/"), userAction: true, T0);
        s.Navigating(U("https://login.microsoftonline.com/a"), userAction: false, T0.AddMilliseconds(100));
        Assert.Null(s.Navigating(U("https://outlook.office.com/owa/"), userAction: false, T0.AddMilliseconds(900)));
    }

    [Fact]
    public void Only_back_to_a_microsoft_site()
    {
        var s = new SilentSignIn();
        s.Navigating(U("https://outlook.office.com/mail/"), userAction: true, T0);
        s.Navigating(U("https://login.microsoftonline.com/a"), userAction: false, T0.AddMilliseconds(100));
        Assert.Null(s.Navigating(U("https://example.com/"), userAction: false, T0.AddMilliseconds(900)));
    }

    [Fact]
    public void Ordinary_browsing_never_counts()
    {
        var s = new SilentSignIn();
        Assert.Null(s.Navigating(U("https://outlook.office.com/mail/"), userAction: true, T0));
        Assert.Null(s.Navigating(U("https://outlook.office.com/mail/inbox"), userAction: false, T0.AddMilliseconds(300)));
    }

    [Fact]
    public void Once_counted_it_starts_over()
    {
        var s = new SilentSignIn();
        s.Navigating(U("https://outlook.office.com/mail/"), userAction: true, T0);
        s.Navigating(U("https://login.microsoftonline.com/a"), userAction: false, T0.AddMilliseconds(100));
        s.Navigating(U("https://outlook.office.com/owa/"), userAction: false, T0.AddMilliseconds(900));
        Assert.Null(s.Navigating(U("https://outlook.office.com/owa/x"), userAction: false, T0.AddMilliseconds(1000)));
    }
}
