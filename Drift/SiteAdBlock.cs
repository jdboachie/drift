using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;

namespace Drift;

/// <summary>
/// Ad blocking per site: the switch in the site settings panel. Some sites break with ads blocked
/// (Figma's sign-in never appears), so you can turn blocking off for just that site.
/// <para>
/// uBlock Origin Lite keeps a filtering level per site, and only takes changes to it from its own
/// pages: its toolbar popup does it with a message to its background. Drift has no toolbar, so it
/// keeps a hidden view of its own (AdBlockHelper) on the extension's dashboard page, in the same
/// profile as the window's pages, and sends the same messages from there.
/// </para>
/// </summary>
public sealed partial class MainWindow
{
    private AdBlockHelper? _adBlock;
    // Set while the switch is being put in place, so that isn't taken for a flick of it.
    private bool _showingSiteAdBlock;
    private Uri? _siteAdBlockPage;

    /// <summary>The panel's switch for this site, once the blocker says how it stands; hidden if there's no blocker.</summary>
    private async void ShowSiteAdBlock(Uri page)
    {
        SiteAdBlockRow.Visibility = Visibility.Collapsed;
        _siteAdBlockPage = page;
        if (AdBlockSite.HostFor(page) is null || !Settings.Current.BlockAds) return;
        _adBlock ??= new AdBlockHelper(WinRT.Interop.WindowNative.GetWindowHandle(this), _private);
        var level = await _adBlock.LevelAsync(page.Host);
        if (level is null || _siteAdBlockPage != page) return;

        _showingSiteAdBlock = true;
        SiteAdBlockSwitch.IsOn = AdBlockSite.IsOn(level.Value);
        _showingSiteAdBlock = false;
        SiteAdBlockSwitch.IsEnabled = true;
        SiteAdBlockRow.Visibility = Visibility.Visible;
    }

    private async void SiteAdBlock_Toggled(object sender, RoutedEventArgs e)
    {
        if (_showingSiteAdBlock || _adBlock is null || _siteAdBlockPage is not { } page || AdBlockSite.HostFor(page) is not { } site) return;
        var on = SiteAdBlockSwitch.IsOn;
        SiteAdBlockSwitch.IsEnabled = false;
        var level = AdBlockSite.LevelFor(on, await _adBlock.DefaultLevelAsync() ?? 2);
        var done = await _adBlock.SetAsync(site, level);
        // A setting for the exact host (made in the blocker itself, say) would outweigh the site's.
        if (done && !string.Equals(page.Host, site, StringComparison.OrdinalIgnoreCase)) done = await _adBlock.SetAsync(page.Host, level);
        SiteAdBlockSwitch.IsEnabled = true;
        if (!done)
        {
            // It didn't take: show how it really stands.
            ShowSiteAdBlock(page);
            return;
        }
        // The page as it now should be: loaded again, with or without filtering.
        if (_active?.View.CoreWebView2 is { } core && Uri.TryCreate(core.Source, UriKind.Absolute, out var here) && here.Host == page.Host)
            core.Reload();
    }
}

/// <summary>
/// A hidden view on uBlock Origin Lite's dashboard page, for asking it about, and changing, a
/// site's filtering level. Started the first time it's needed; one per window, so a private
/// window's goes with it (and with its profile).
/// </summary>
internal sealed class AdBlockHelper(IntPtr window, bool isPrivate)
{
    private CoreWebView2Controller? _controller;
    private Task<CoreWebView2?>? _ready;

    public Task<int?> LevelAsync(string host) => AskAsync($"chrome.runtime.sendMessage({{ what: 'getFilteringMode', hostname: {JsonSerializer.Serialize(host)} }})");

    public Task<int?> DefaultLevelAsync() => AskAsync("chrome.runtime.sendMessage({ what: 'getDefaultFilteringMode' })");

    public async Task<bool> SetAsync(string host, int level) =>
        await AskAsync($"chrome.runtime.sendMessage({{ what: 'setFilteringMode', hostname: {JsonSerializer.Serialize(host)}, level: {level} }})") == level;

    public void Close()
    {
        try { _controller?.Close(); } catch { }
        _controller = null;
        _ready = null;
    }

    private async Task<int?> AskAsync(string expression)
    {
        try
        {
            if (await (_ready ??= StartAsync()) is not { } core)
            {
                _ready = null; // no blocker yet; look again next time
                return null;
            }
            var asking = core.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
                JsonSerializer.Serialize(new { expression, awaitPromise = true, returnByValue = true })).AsTask();
            if (await Task.WhenAny(asking, Task.Delay(5000)) != asking) return null;
            using var reply = JsonDocument.Parse(await asking);
            return reply.RootElement.GetProperty("result").TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Number
                ? value.GetInt32()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<CoreWebView2?> StartAsync()
    {
        var environment = await MainWindow.EnvironmentAsync();
        var parent = CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)window);
        _controller = isPrivate
            ? await environment.CreateCoreWebView2ControllerAsync(parent, MainWindow.PrivateOptions(environment))
            : await environment.CreateCoreWebView2ControllerAsync(parent);
        _controller.IsVisible = false;
        var core = _controller.CoreWebView2;
        var blocker = (await core.Profile.GetBrowserExtensionsAsync()).FirstOrDefault(e => Extensions.IsAdBlock(e) && e.IsEnabled);
        if (blocker is null)
        {
            Close();
            return null;
        }
        var loaded = new TaskCompletionSource<bool>();
        core.NavigationCompleted += (_, e) => loaded.TrySetResult(e.IsSuccess);
        core.Navigate($"chrome-extension://{blocker.Id}/dashboard.html");
        if (await Task.WhenAny(loaded.Task, Task.Delay(8000)) != loaded.Task || !loaded.Task.Result)
        {
            Close();
            return null;
        }
        return core;
    }
}
