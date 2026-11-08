using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace Drift;

internal sealed class HistoryEntry
{
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public int Visits { get; set; }
    public DateTime LastVisit { get; set; }
}

/// <summary>
/// The pages you've visited and their sites' icons, for the command bar's suggestions. Kept on this
/// machine only, in %LOCALAPPDATA%\Drift: history.json and a Favicons folder (one PNG per site,
/// saved from what the page itself showed, so no favicon service is ever asked).
/// </summary>
internal static class History
{
    private const int MaxEntries = 5000;

    private static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Drift");
    private static readonly string FilePath = Path.Combine(Folder, "history.json");
    private static readonly string IconFolder = Path.Combine(Folder, "Favicons");

    private static readonly Dictionary<string, HistoryEntry> Entries = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, BitmapImage?> Icons = new(StringComparer.OrdinalIgnoreCase);
    private static DispatcherQueueTimer? _saveTimer;

    public static IEnumerable<HistoryEntry> All => Entries.Values;

    private static bool _loaded;

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(FilePath)) return;
            var list = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(FilePath));
            if (list is null) return;
            foreach (var entry in list.Where(e => !string.IsNullOrEmpty(e.Url))) Entries[entry.Url] = entry;
        }
        catch
        {
            // A damaged file just means starting over.
        }
    }

    /// <summary>A page was shown. Reloads and the same page reported twice in a row count once.</summary>
    public static void Record(string? url, string? title)
    {
        if (!Worth(url)) return;
        var now = DateTime.UtcNow;
        if (Entries.TryGetValue(url!, out var entry))
        {
            if ((now - entry.LastVisit).TotalSeconds > 2) entry.Visits++;
            entry.LastVisit = now;
            if (!string.IsNullOrWhiteSpace(title)) entry.Title = title!;
        }
        else
        {
            Entries[url!] = new HistoryEntry { Url = url!, Title = title?.Trim() ?? "", Visits = 1, LastVisit = now };
            if (Entries.Count > MaxEntries) Trim();
        }
        SaveSoon();
    }

    public static void Remove(string url)
    {
        if (Entries.Remove(url)) SaveSoon();
    }

    /// <summary>Forgets pages last visited since then (UTC), or every page.</summary>
    public static void Clear(DateTime? since)
    {
        foreach (var entry in Entries.Values.Where(e => since is null || e.LastVisit >= since).ToList()) Entries.Remove(entry.Url);
        SaveSoon();
    }

    public static void SetTitle(string? url, string? title)
    {
        if (url is null || string.IsNullOrWhiteSpace(title) || !Entries.TryGetValue(url, out var entry)) return;
        if (entry.Title == title) return;
        entry.Title = title!.Trim();
        SaveSoon();
    }

    /// <summary>
    /// How much a page is worth suggesting: visits, weighted down as they age (Firefox's "frecency",
    /// simplified to its recency buckets).
    /// </summary>
    public static double Frecency(HistoryEntry entry)
    {
        var days = (DateTime.UtcNow - entry.LastVisit).TotalDays;
        var weight = days < 4 ? 1.0 : days < 14 ? 0.7 : days < 31 ? 0.5 : days < 90 ? 0.3 : 0.1;
        return entry.Visits * weight;
    }

    /// <summary>The site's icon as last seen, if any: from memory, else from the Favicons folder.</summary>
    public static async Task<BitmapImage?> IconAsync(string? host)
    {
        if (string.IsNullOrEmpty(host)) return null;
        if (Icons.TryGetValue(host, out var cached)) return cached;
        Icons[host] = null;
        try
        {
            var path = IconPath(host);
            if (!File.Exists(path)) return null;
            var bytes = await File.ReadAllBytesAsync(path);
            var image = await DecodeAsync(bytes);
            Icons[host] = image;
            return image;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Keeps the icon a page just showed (as PNG bytes) for its site.</summary>
    public static void StoreIcon(string? host, byte[] png, BitmapImage image)
    {
        if (string.IsNullOrEmpty(host)) return;
        Icons[host] = image;
        _ = Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(IconFolder);
                File.WriteAllBytes(IconPath(host), png);
            }
            catch { }
        });
    }

    public static async Task<BitmapImage> DecodeAsync(byte[] png)
    {
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(png.AsBuffer());
        stream.Seek(0);
        var image = new BitmapImage();
        await image.SetSourceAsync(stream);
        return image;
    }

    private static bool Worth(string? url) =>
        url is not null
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    // Host names are safe file names, apart from the port's colon.
    private static string IconPath(string host) => Path.Combine(IconFolder, host.ToLowerInvariant().Replace(':', '_') + ".png");

    private static void Trim()
    {
        foreach (var old in Entries.Values.OrderBy(Frecency).ThenBy(e => e.LastVisit).Take(Entries.Count - MaxEntries).ToList())
            Entries.Remove(old.Url);
    }

    /// <summary>Writes the file a couple of seconds after the last change, off the UI thread.</summary>
    /// <summary>Goes up with every change, so a list built from history knows it's out of date.</summary>
    public static int Version { get; private set; }

    private static void SaveSoon()
    {
        Version++;
        if (_saveTimer is null)
        {
            var queue = DispatcherQueue.GetForCurrentThread();
            if (queue is null) return;
            _saveTimer = queue.CreateTimer();
            _saveTimer.Interval = TimeSpan.FromSeconds(2);
            _saveTimer.IsRepeating = false;
            _saveTimer.Tick += (_, _) => SaveNow();
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <param name="wait">True when the app is closing, so the write finishes before it exits.</param>
    public static void SaveNow(bool wait = false)
    {
        _saveTimer?.Stop();
        string json;
        try { json = JsonSerializer.Serialize(Entries.Values.ToList()); }
        catch { return; }
        void Write()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var temp = FilePath + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, FilePath, overwrite: true);
            }
            catch { }
        }
        if (wait) Write();
        else _ = Task.Run(Write);
    }
}
