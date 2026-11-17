using System;
using System.Linq;

namespace Drift;

/// <summary>
/// The Microsoft sites Windows can sign you in to (Outlook, Office, SharePoint, Teams, OneDrive), and
/// the two pages that do the signing in: login.microsoftonline.com for work and school accounts,
/// login.live.com for personal ones. Sign-in happens there, not on the sites.
/// </summary>
public static class MicrosoftSites
{
    private static readonly string[] SignInHosts = ["login.microsoftonline.com", "login.live.com"];

    // A site is one of these or under one. The sign-in pages are not sites.
    private static readonly string[] Domains =
        ["office.com", "office365.com", "outlook.com", "microsoft365.com", "sharepoint.com", "cloud.microsoft", "onedrive.com", "live.com"];

    // Microsoft's own domain is mostly not about signing in (Learn, the store…), so only these on it.
    private static readonly string[] Hosts =
        ["teams.microsoft.com", "myaccount.microsoft.com", "account.microsoft.com", "myapps.microsoft.com"];

    public static bool IsSignIn(string host) => SignInHosts.Contains(host.ToLowerInvariant());

    public static bool IsSite(string host)
    {
        host = host.ToLowerInvariant();
        if (host.Length == 0 || IsSignIn(host)) return false;
        return Hosts.Contains(host) || Domains.Any(d => host == d || host.EndsWith("." + d, StringComparison.Ordinal));
    }

    /// <summary>A cookie (by its domain) that keeps you signed in to Microsoft: the sign-in pages' and the sites'.</summary>
    public static bool OwnsCookie(string domain)
    {
        var host = domain.TrimStart('.').ToLowerInvariant();
        if (host.Length == 0) return false;
        return IsSignIn(host) || host == "microsoftonline.com" || host.EndsWith(".microsoftonline.com", StringComparison.Ordinal)
            || IsSite(host);
    }

    /// <summary>The site a cookie's domain names, for clearing its storage; null for a whole domain (".live.com").</summary>
    public static string? Origin(string cookieDomain)
    {
        var host = cookieDomain.TrimStart('.').ToLowerInvariant();
        if (host.Length == 0 || Domains.Contains(host) || host == "microsoftonline.com") return null;
        return "https://" + host;
    }

    /// <summary>The sites' own storage, cleared with their cookies (Outlook keeps its sign-in there too).</summary>
    public static readonly string[] KnownOrigins =
    [
        "https://login.microsoftonline.com", "https://login.live.com",
        "https://outlook.office.com", "https://outlook.office365.com", "https://outlook.live.com", "https://outlook.cloud.microsoft",
        "https://www.office.com", "https://www.microsoft365.com", "https://m365.cloud.microsoft",
        "https://teams.microsoft.com", "https://teams.live.com", "https://onedrive.live.com",
        "https://myaccount.microsoft.com", "https://account.microsoft.com",
    ];

    /// <summary>Microsoft's sign-in page showing your accounts to choose from, rather than signing in as Windows' one.</summary>
    public static readonly Uri AccountPicker = new("https://outlook.office.com/mail/?prompt=select_account");

    /// <summary>
    /// The header the engine adds to a request for the sign-in page when it signs in with the Windows
    /// account (seen on work and school sign-ins, with x-ms-DeviceCredential): proof it was Windows.
    /// </summary>
    public const string WindowsAccountHeader = "x-ms-RefreshTokenCredential";

    public static bool CarriesWindowsAccount(System.Collections.Generic.IEnumerable<string> headerNames) =>
        headerNames.Any(h => string.Equals(h, WindowsAccountHeader, StringComparison.OrdinalIgnoreCase));

    /// <summary>What you'd call a site: its product ("Outlook", "SharePoint"), or else its host less "www.".</summary>
    public static string ProductName(string host)
    {
        host = host.ToLowerInvariant();
        string first = host.Split('.')[0];
        if (first == "outlook") return "Outlook";
        if (first == "teams") return "Teams";
        if (first == "onedrive" || (host.EndsWith(".sharepoint.com", StringComparison.Ordinal) && first.EndsWith("-my", StringComparison.Ordinal)))
            return "OneDrive";
        if (host.EndsWith(".sharepoint.com", StringComparison.Ordinal)) return "SharePoint";
        if (host is "m365.cloud.microsoft" or "www.microsoft365.com" or "microsoft365.com" or "www.office.com" or "office.com")
            return "Microsoft 365";
        if (host is "myaccount.microsoft.com" or "account.microsoft.com") return "My Account";
        return Name(host);
    }

    /// <summary>How a site is named in a notice: its host, less "www.".</summary>
    public static string Name(string host) => host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
}

/// <summary>
/// Notices when a tab was signed in without you: it went from a Microsoft site to a sign-in page
/// and came back to a Microsoft site (within <see cref="Window"/>), with nothing done by you on the
/// way. That's Windows' account, or the sign-in page remembering you, at work. A guess, for when the
/// engine's own sign-in header can't be seen (<see cref="MicrosoftSites.CarriesWindowsAccount"/>).
/// The sign-in page can take a few seconds (office.com: 4.7s), and anything you do there is a
/// navigation of yours, so the window is generous.
/// </summary>
public sealed class SilentSignIn
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private string? _site;      // The Microsoft site the tab's on, if it is.
    private DateTime? _left;    // When it went to sign in, while it's there.

    /// <summary>
    /// A navigation starting in the tab. <paramref name="userAction"/>: you started it (a link, the
    /// address bar, a button on the page), not a redirect or the page's script.
    /// The site you were signed in to, when this navigation completes a silent sign-in.
    /// </summary>
    public string? Navigating(Uri uri, bool userAction, DateTime now)
    {
        var host = uri.Host.ToLowerInvariant();
        if (MicrosoftSites.IsSignIn(host))
        {
            if (userAction) _left = null;
            else if (_left is null && _site is not null) _left = now;
            _site = null;
            return null;
        }
        var silent = _left is { } left && !userAction && now - left <= Window && MicrosoftSites.IsSite(host);
        _left = null;
        _site = MicrosoftSites.IsSite(host) ? host : null;
        return silent ? host : null;
    }

    /// <summary>You typed or clicked on the page: whatever comes next isn't silent.</summary>
    public void Input() => _left = null;
}
