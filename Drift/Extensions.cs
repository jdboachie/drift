using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Drift;

/// <summary>
/// Browser extensions (Chrome/Edge ones, unpacked), which WebView2 runs in every page of the
/// profile. Drift has no toolbar, so an extension's button and popup don't show; what it does in
/// pages works. Ad blocking is uBlock Origin Lite, downloaded from its GitHub releases the first
/// time it's turned on (Settings › General), into %LOCALAPPDATA%\Drift\Extensions.
/// </summary>
internal static class Extensions
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Drift", "Extensions");
    private const string AdBlockReleases = "https://api.github.com/repos/uBlockOrigin/uBOL-home/releases/latest";

    private static readonly SemaphoreSlim Busy = new(1, 1);

    /// <summary>The profile every page shares; there once the first page's engine has started.</summary>
    public static CoreWebView2Profile? Profile { get; private set; }

    /// <summary>What ad blocking is doing, when it's doing something (or why it couldn't).</summary>
    public static string? AdBlockStatus { get; private set; }

    /// <summary>The list or the ad blocker's status changed. On the UI thread.</summary>
    public static event Action? Changed;

    /// <summary>Called as each page's engine starts; the first one brings ad blocking in line with settings.</summary>
    public static void Attach(CoreWebView2Profile profile)
    {
        if (Profile is not null) return;
        Profile = profile;
        ApplyPreferences();
        _ = SyncAdBlockAsync();
    }

    /// <summary>The engine stopped (it crashed): the next page to start brings a new profile object.</summary>
    public static void Detach()
    {
        Profile = null;
        _privateProfile = null;
    }

    /// <summary>Saving passwords and filling forms, as Settings says.</summary>
    public static void ApplyPreferences()
    {
        if (Profile is not { } profile) return;
        try
        {
            profile.IsPasswordAutosaveEnabled = Settings.Current.SavePasswords;
            profile.IsGeneralAutofillEnabled = Settings.Current.Autofill;
        }
        catch { }
    }

    private static CoreWebView2Profile? _privateProfile;

    /// <summary>
    /// Private windows have a profile of their own that keeps nothing, extensions included, so the
    /// ad blocker is added to it again each time (from the copy already downloaded). Nothing is
    /// saved there: no passwords, no form data.
    /// </summary>
    public static async void AttachPrivate(CoreWebView2Profile profile)
    {
        if (_privateProfile == profile) return;
        _privateProfile = profile;
        try
        {
            profile.IsPasswordAutosaveEnabled = false;
            profile.IsGeneralAutofillEnabled = false;
            if (!Settings.Current.BlockAds) return;
            if ((await profile.GetBrowserExtensionsAsync()).Any(IsAdBlock)) return;
            if (DownloadedAdBlock() is { } folder) await profile.AddBrowserExtensionAsync(folder);
        }
        catch
        {
            // Pages still work; they're just not filtered.
        }
    }

    /// <summary>The newest uBlock Origin Lite already downloaded, if any.</summary>
    private static string? DownloadedAdBlock()
    {
        if (!Directory.Exists(Folder)) return null;
        foreach (var dir in Directory.GetDirectories(Folder, "uBOLite-*").OrderByDescending(Directory.GetLastWriteTimeUtc))
        {
            if (File.Exists(Path.Combine(dir, "manifest.json"))) return dir;
            var inner = Directory.GetDirectories(dir).FirstOrDefault(d => File.Exists(Path.Combine(d, "manifest.json")));
            if (inner is not null) return inner;
        }
        return null;
    }

    // Parts of WebView2 itself that it lists as extensions: removing them would break copying and PDFs.
    private static readonly HashSet<string> BuiltIn = new()
    {
        "dgiklkfkllikcanfonkcabmbdfmgleag", // Microsoft Clipboard Extension
        "mhjfbmdgcfjbbpaeojofohoefgiehjai", // Microsoft Edge PDF Viewer
    };

    /// <summary>The extensions you've added (and the ad blocker), not WebView2's own parts.</summary>
    public static async Task<IReadOnlyList<CoreWebView2BrowserExtension>> ListAsync() =>
        Profile is null ? [] : (await Profile.GetBrowserExtensionsAsync()).Where(e => !BuiltIn.Contains(e.Id)).ToList();

    public static bool IsAdBlock(CoreWebView2BrowserExtension extension) =>
        extension.Id == Settings.Current.AdBlockExtensionId
        || extension.Name.StartsWith("uBlock Origin Lite", StringComparison.OrdinalIgnoreCase);

    /// <summary>Adds an unpacked extension (the folder with its manifest.json).</summary>
    public static async Task AddAsync(string folder)
    {
        if (Profile is null) throw new InvalidOperationException("Open a page first, then add the extension.");
        await Profile.AddBrowserExtensionAsync(folder);
        Changed?.Invoke();
    }

    /// <summary>Ad blocking as Settings says: installed (downloaded the first time) and on, or off.</summary>
    public static async Task SyncAdBlockAsync()
    {
        if (Profile is not { } profile) return;
        await Busy.WaitAsync();
        try
        {
            var installed = (await profile.GetBrowserExtensionsAsync()).FirstOrDefault(IsAdBlock);
            if (Settings.Current.BlockAds)
            {
                if (installed is null)
                {
                    SetStatus("Downloading uBlock Origin Lite…");
                    var folder = await DownloadAdBlockAsync();
                    SetStatus("Installing…");
                    var added = await profile.AddBrowserExtensionAsync(folder);
                    Settings.Update(s => s.AdBlockExtensionId = added.Id);
                }
                else if (!installed.IsEnabled) await installed.EnableAsync(true);
            }
            else if (installed is { IsEnabled: true }) await installed.EnableAsync(false);
            SetStatus(null);
        }
        catch (Exception ex)
        {
            SetStatus("Couldn't set up ad blocking: " + ex.Message);
        }
        finally
        {
            Busy.Release();
        }
    }

    private static void SetStatus(string? status)
    {
        AdBlockStatus = status;
        Changed?.Invoke();
    }

    /// <summary>The latest uBlock Origin Lite for Edge, unzipped; returns its folder.</summary>
    private static async Task<string> DownloadAdBlockAsync()
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Drift");
        using var release = JsonDocument.Parse(await http.GetStringAsync(AdBlockReleases));
        var tag = release.RootElement.GetProperty("tag_name").GetString() ?? "latest";
        var assets = release.RootElement.GetProperty("assets").EnumerateArray()
            .Select(a => (Name: a.GetProperty("name").GetString() ?? "", Url: a.GetProperty("browser_download_url").GetString() ?? ""))
            .ToList();
        var asset = assets.FirstOrDefault(a => a.Name.EndsWith(".edge.zip", StringComparison.OrdinalIgnoreCase));
        if (asset.Url is null or "") asset = assets.FirstOrDefault(a => a.Name.EndsWith(".chromium.zip", StringComparison.OrdinalIgnoreCase));
        if (asset.Url is null or "") throw new InvalidOperationException("no Edge or Chromium build in the latest release");

        var bytes = await http.GetByteArrayAsync(asset.Url);
        var folder = Path.Combine(Folder, "uBOLite-" + tag);
        await Task.Run(() =>
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            Directory.CreateDirectory(folder);
            using var zip = new ZipArchive(new MemoryStream(bytes));
            zip.ExtractToDirectory(folder);
        });
        // Some zips wrap the extension in a folder of its own.
        if (!File.Exists(Path.Combine(folder, "manifest.json")))
        {
            var inner = Directory.GetDirectories(folder).FirstOrDefault(d => File.Exists(Path.Combine(d, "manifest.json")));
            if (inner is not null) return inner;
        }
        return folder;
    }
}
