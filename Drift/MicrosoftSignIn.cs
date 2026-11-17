using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Drift.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using Windows.Storage.Streams;

namespace Drift;

/// <summary>
/// Signing in to Microsoft sites with the Windows account, as Edge does: Outlook, Office,
/// SharePoint and the rest send you to Microsoft's sign-in page, which the web engine answers with
/// the account you're signed in to Windows with (work or school, or a personal Microsoft account),
/// so you never see it. It's an engine option (MainWindow.EngineOptions), so it's chosen before
/// the engine starts: on the first run, from a card; afterwards in Settings > Accounts, applying
/// after a restart. As it's silent, Drift says so the first time it happens, and you can use a
/// different account, sign out of the Microsoft sites, or turn it off.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly TaskCompletionSource SignInAnswer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static bool _askingSignIn;

    /// <summary>Done once the first-run card is answered (straight away, every run after that).</summary>
    private static Task SignInAnswered => Settings.Current.MicrosoftSignInAsked ? Task.CompletedTask : SignInAnswer.Task;

    /// <summary>Whether the running engine signs in with Windows; null until it has started.</summary>
    internal static bool? EngineSignsIn { get; private set; }

    /// <summary>A change to the setting waiting for a restart.</summary>
    internal static bool SignInRestartPending => EngineSignsIn is { } running && running != Settings.Current.MicrosoftSignIn;

    private static readonly TimeSpan SignInNoticeTime = TimeSpan.FromSeconds(12);

    // ---- The first run ----

    /// <summary>
    /// The card asking about Windows sign-in, the first time Drift runs (or the first time since it
    /// learnt to). The engine waits for it, so the answer applies at once.
    /// </summary>
    private async void AskAboutSignInOnce()
    {
        if (_private || _askingSignIn || Settings.Current.MicrosoftSignInAsked) return;
        _askingSignIn = true;
        const string choice = "Use your Windows account to sign in to Outlook, Office and other Microsoft sites automatically.";
        var toggle = new ToggleSwitch
        {
            IsOn = Settings.Current.MicrosoftSignIn,
            OnContent = "",
            OffContent = "",
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, choice);
        var row = new Grid { ColumnSpacing = 16 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = choice, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(toggle, 1);
        row.Children.Add(toggle);
        var content = new StackPanel { Spacing = 12, MaxWidth = 420 };
        content.Children.Add(row);
        content.Children.Add(new TextBlock
        {
            Text = "You can change this in Settings > Accounts.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["DriftSecondaryTextBrush"],
        });
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        title.Children.Add(MicrosoftLogo(18));
        title.Children.Add(new TextBlock { Text = "Microsoft sign-in", VerticalAlignment = VerticalAlignment.Center });
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = title,
            Content = content,
            PrimaryButtonText = "Continue",
            DefaultButton = ContentDialogButton.Primary,
        };
        try
        {
            await dialog.ShowAsync();
        }
        catch (Exception)
        {
            // Another dialog's open: the default stands, and the card isn't asked again.
        }
        Settings.Update(s =>
        {
            s.MicrosoftSignIn = toggle.IsOn;
            s.MicrosoftSignInAsked = true;
        });
        SignInAnswer.TrySetResult();
    }

    /// <summary>Microsoft's logo, its four squares, <paramref name="size"/> across.</summary>
    internal static FrameworkElement MicrosoftLogo(double size)
    {
        var gap = Math.Max(1, Math.Round(size / 21));
        var square = (size - gap) / 2;
        var logo = new Grid
        {
            Width = size,
            Height = size,
            ColumnSpacing = gap,
            RowSpacing = gap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(logo, "Microsoft");
        logo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(square) });
        logo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(square) });
        logo.RowDefinitions.Add(new RowDefinition { Height = new GridLength(square) });
        logo.RowDefinitions.Add(new RowDefinition { Height = new GridLength(square) });
        (byte R, byte G, byte B)[] colours = [(0xF2, 0x50, 0x22), (0x7F, 0xBA, 0x00), (0x00, 0xA4, 0xEF), (0xFF, 0xB9, 0x00)];
        for (var i = 0; i < 4; i++)
        {
            var (r, g, b) = colours[i];
            var part = new Microsoft.UI.Xaml.Shapes.Rectangle { Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, r, g, b)) };
            Grid.SetColumn(part, i % 2);
            Grid.SetRow(part, i / 2);
            logo.Children.Add(part);
        }
        return logo;
    }

    // ---- The first silent sign-in ----

    private readonly ConditionalWeakTable<BrowserTab, SilentSignIn> _signIns = new();

    private bool WatchingSignIn => !_private && EngineSignsIn == true && !Settings.Current.MicrosoftSignInNoticeShown;

    /// <summary>
    /// A tab's requests to the sign-in pages, from the page or any frame in it (Outlook signs in from
    /// a hidden frame, never leaving its own page), until the first sign-in with Windows' account has
    /// been pointed out. The engine marks those with its own header.
    /// </summary>
    private void WatchSignInRequests(BrowserTab tab, CoreWebView2 core)
    {
        if (!WatchingSignIn) return;
        foreach (var host in new[] { "login.microsoftonline.com", "login.live.com" })
            core.AddWebResourceRequestedFilter($"https://{host}/*", CoreWebView2WebResourceContext.Document,
                CoreWebView2WebResourceRequestSourceKinds.All);
        core.WebResourceRequested += OnSignInRequest;

        void OnSignInRequest(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            if (!WatchingSignIn)
            {
                sender.WebResourceRequested -= OnSignInRequest;
                return;
            }
            if (!MicrosoftSites.CarriesWindowsAccount(e.Request.Headers.Select(h => h.Key))) return;
            // Named by the site you're on; a sign-in page in front, by the site it signs in to (shown once back).
            if (Uri.TryCreate(sender.Source, UriKind.Absolute, out var page) && MicrosoftSites.IsSite(page.Host))
                ShowSignInNotice(page.Host);
            else _signedInWithWindows.AddOrUpdate(tab, new object());
        }
    }

    // Tabs signed in with Windows' account while on the sign-in page: noticed once they're back on the site.
    private readonly ConditionalWeakTable<BrowserTab, object> _signedInWithWindows = new();

    /// <summary>Each of a tab's navigations, until the first sign-in that came from Windows has been pointed out.</summary>
    private void WatchSignIn(BrowserTab tab, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!WatchingSignIn) return;
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)) return;
        if (_signedInWithWindows.TryGetValue(tab, out _) && MicrosoftSites.IsSite(uri.Host))
        {
            _signedInWithWindows.Remove(tab);
            ShowSignInNotice(uri.Host);
            return;
        }
        var watch = _signIns.GetValue(tab, _ => new SilentSignIn());
        // A redirect carries on whatever started it, so only a navigation of its own is something you did.
        if (watch.Navigating(uri, e.IsUserInitiated && !e.IsRedirected, DateTime.UtcNow) is { } host) ShowSignInNotice(host);
    }

    private void ShowSignInNotice(string host)
    {
        if (!WatchingSignIn) return;
        Settings.Update(s => s.MicrosoftSignInNoticeShown = true);
        ShowNotice(MicrosoftLogo(20), $"Signed in to {MicrosoftSites.ProductName(host)}", "Automatically, with your Windows account",
            SignInNoticeTime,
            ("Use another account", UseDifferentMicrosoftAccount),
            ("Turn off", TurnOffSignIn));
    }

    private void TurnOffSignIn()
    {
        Settings.Update(s => s.MicrosoftSignIn = false);
        ShowNotice(MicrosoftLogo(20), "Automatic sign-in is off", "From when Drift restarts", SignInNoticeTime,
            ("Restart now", () => _ = RestartAsync()));
    }

    // ---- Another account, or none ----

    /// <summary>
    /// Microsoft's sign-in page in a new tab, listing your accounts with "Use another account"
    /// rather than signing in as Windows' one. Whichever you pick, the sign-in page remembers it,
    /// and every Microsoft site follows.
    /// </summary>
    internal void UseDifferentMicrosoftAccount()
    {
        OpenInNewTab(MicrosoftSites.AccountPicker);
        Activate();
    }

    /// <summary>
    /// Asks, then signs you out of the Microsoft sites (their cookies and storage, and the sign-in
    /// pages'), offering to turn automatic sign-in off too, as Windows would sign you straight back
    /// in. What happened, for Settings to say; null if you cancelled.
    /// </summary>
    internal async Task<string?> SignOutOfMicrosoftAsync(XamlRoot root)
    {
        var signsIn = Settings.Current.MicrosoftSignIn;
        var turnOff = new CheckBox { Content = "Also turn off automatic sign-in" };
        var content = new StackPanel { Spacing = 12, MaxWidth = 420 };
        content.Children.Add(new TextBlock
        {
            Text = signsIn
                ? "Outlook, Office and other Microsoft sites will ask you to sign in again. While automatic sign-in is on, Windows may sign you straight back in."
                : "Outlook, Office and other Microsoft sites will ask you to sign in again.",
            TextWrapping = TextWrapping.Wrap,
        });
        if (signsIn) content.Children.Add(turnOff);
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "Sign out of Microsoft sites?",
            Content = content,
            PrimaryButtonText = "Sign out",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        }
        catch (Exception)
        {
            return null;
        }
        if (!await ClearMicrosoftSignInAsync()) return "Couldn't sign out. Try again.";
        if (turnOff.IsChecked != true) return "Signed out";
        Settings.Update(s => s.MicrosoftSignIn = false);
        return "Signed out. Automatic sign-in turns off when Drift restarts.";
    }

    /// <summary>
    /// The Microsoft sign-in pages' and sites' cookies, and the sites' storage (Outlook keeps its
    /// sign-in there as well).
    /// </summary>
    private async Task<bool> ClearMicrosoftSignInAsync()
    {
        CoreWebView2Controller? started = null;
        try
        {
            // Open Microsoft pages would sign straight back in (with automatic sign-in on until a
            // restart), so they go first: tabs are unloaded, to load again when opened, and Outlook's
            // hidden view (Outlook.cs) closes until Drift restarts.
            StopOutlook();
            foreach (var host in _hiddenSites.Keys.Where(h => MicrosoftSites.IsSite(h) || MicrosoftSites.IsSignIn(h)).ToList())
                CloseHiddenSite(host);
            var microsoft = AllTabs.Where(OnMicrosoft).ToList();
            // Off a Microsoft tab first: unloading the one in front brings back the one before it,
            // which mustn't be another to load and sign in. If every tab is one, the front one stays.
            if (_active is { } front && microsoft.Contains(front))
            {
                if (_recentTabs.Concat(AllTabs).FirstOrDefault(t => !microsoft.Contains(t) && !OnMicrosoft(t)) is { } other)
                    Activate(other);
                else microsoft.Remove(front);
            }
            foreach (var tab in microsoft) UnloadTab(tab);

            var core = AllTabs.Select(t => t.View.CoreWebView2).FirstOrDefault(c => c is not null);
            if (core is null)
            {
                // No page open yet: an engine of its own for a moment, on the same profile.
                var environment = await EnvironmentAsync();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                started = await environment.CreateCoreWebView2ControllerAsync(
                    CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)hwnd));
                started.IsVisible = false;
                core = started.CoreWebView2;
            }
            var cookies = core.CookieManager;
            var origins = new HashSet<string>(MicrosoftSites.KnownOrigins, StringComparer.OrdinalIgnoreCase);
            foreach (var cookie in await cookies.GetCookiesAsync(""))
            {
                if (!MicrosoftSites.OwnsCookie(cookie.Domain)) continue;
                if (MicrosoftSites.Origin(cookie.Domain) is { } origin) origins.Add(origin);
                cookies.DeleteCookie(cookie);
            }
            foreach (var origin in origins)
                await core.CallDevToolsProtocolMethodAsync("Storage.clearDataForOrigin",
                    JsonSerializer.Serialize(new { origin, storageTypes = "all" }));
            // Microsoft tabs aren't reloaded: with automatic sign-in still on until a restart, they'd
            // be signed straight back in. They find out on their own, or come back signed out.
            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            started?.Close();
        }
    }

    /// <summary>A tab whose page (loaded, or waiting to load) is a Microsoft site or sign-in page.</summary>
    private static bool OnMicrosoft(BrowserTab tab)
    {
        var url = tab.View.CoreWebView2?.Source is { Length: > 0 } source && source != "about:blank" ? source : tab.PendingUri?.ToString() ?? tab.Url;
        return Uri.TryCreate(url, UriKind.Absolute, out var page) && (MicrosoftSites.IsSite(page.Host) || MicrosoftSites.IsSignIn(page.Host));
    }

    // ---- Restarting ----

    /// <summary>
    /// Drift again, for a setting the engine only reads as it starts. Every tab is kept, whatever
    /// happens to tabs on close; anything else that would be lost is asked about first. The new Drift
    /// waits for this one and its engine to be gone (Program.WaitForRestart).
    /// </summary>
    internal async Task RestartAsync(XamlRoot? root = null)
    {
        var downloads = Downloads.Items.Count(i => i.IsRunning);
        var unsaved = (await PagesWithChangesAsync(AllTabs)).Count;
        foreach (var window in PrivateWindows.ToList())
            unsaved += (await window.PagesWithChangesAsync(window.AllTabs)).Count;
        if (Warnings.Restart(downloads, PrivateWindows.Count, unsaved) is { } warning
            && !await (root is null ? AskAsync(warning) : ConfirmDialog.AskAsync(root, warning)))
            return;
        SaveSession(wait: true, everything: true);
        History.SaveNow(wait: true);
        var waitFor = new List<int> { Environment.ProcessId };
        if (AllTabs.Select(t => t.View.CoreWebView2).FirstOrDefault(c => c is not null) is { } core)
            waitFor.Add((int)core.BrowserProcessId);
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, Program.RestartArgument + string.Join(",", waitFor))
            {
                UseShellExecute = false,
            });
        }
        catch (Exception)
        {
            return;
        }
        Environment.Exit(0);
    }
}

/// <summary>
/// The account you're signed in to Windows with, for Settings > Accounts: its name and picture from
/// Windows, and its email: a work or school account's sign-in name, or the personal Microsoft
/// account Windows is linked to. No sign-in or app registration needed.
/// </summary>
internal static class WindowsAccount
{
    public sealed record Info(string? Name, string? Email, IRandomAccessStreamReference? Picture);

    /// <summary>The account, once loaded; and its picture, decoded, so Settings shows both at once.</summary>
    public static Info? Loaded { get; private set; }
    public static Microsoft.UI.Xaml.Media.Imaging.BitmapImage? Picture { get; private set; }

    private static Task? _loading;

    /// <summary>
    /// Loads the account and decodes its picture, once, ahead of Settings opening (Drift starts it
    /// after the tabs are back). On the UI thread, which every Drift window shares, so the picture
    /// can be shown in any of them.
    /// </summary>
    public static Task PreloadAsync() => _loading ??= LoadAsync();

    private static async Task LoadAsync()
    {
        var info = await GetAsync();
        if (info.Picture is { } source)
        {
            try
            {
                using var stream = await source.OpenReadAsync();
                var picture = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage { DecodePixelWidth = 112 };
                await picture.SetSourceAsync(stream);
                Picture = picture;
            }
            catch (Exception)
            {
                // Initials instead.
            }
        }
        Loaded = info;
    }

    public static async Task<Info> GetAsync()
    {
        string? name = null;
        IRandomAccessStreamReference? picture = null;
        try
        {
            var users = await Windows.System.User.FindAllAsync();
            if (users.FirstOrDefault() is { } user)
            {
                name = await user.GetPropertyAsync(Windows.System.KnownUserProperties.DisplayName) as string;
                picture = await user.GetPictureAsync(Windows.System.UserPictureSize.Size208x208);
            }
        }
        catch (Exception)
        {
            // Windows wouldn't say.
        }
        return new(string.IsNullOrWhiteSpace(name) ? Environment.UserName : name, Email(), picture);
    }

    private static string? Email()
    {
        // Work or school (Entra ID): the account's user principal name, its email.
        var size = 256u;
        var text = new StringBuilder((int)size);
        if (GetUserNameExW(UserPrincipalName, text, ref size) && text.ToString().Contains('@')) return text.ToString();
        // A personal Microsoft account: Windows keeps it by its email.
        try
        {
            using var accounts = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\IdentityCRL\UserExtendedProperties");
            return accounts?.GetSubKeyNames().FirstOrDefault(n => n.Contains('@'));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private const int UserPrincipalName = 8;

    [DllImport("secur32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserNameExW(int format, StringBuilder name, ref uint size);
}
