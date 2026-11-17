using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;

namespace Drift;

public enum DownloadState
{
    InProgress,
    Completed,
    Failed,
    Cancelled,
}

/// <summary>One download: while it runs it follows its WebView2 operation; afterwards it's a saved record.</summary>
public sealed class DownloadItem : INotifyPropertyChanged
{
    public string FilePath { get; set; } = "";
    public string Url { get; set; } = "";
    public DateTime Started { get; set; }
    public long Received { get; set; }
    public long? Total { get; set; }
    public DownloadState State { get; set; }
    public string? FailReason { get; set; }
    /// <summary>When it finished (lists saved before Drift kept this have only <see cref="Started"/>).</summary>
    public DateTime? Finished { get; set; }

    /// <summary>From a private window: listed until the last private window closes, never saved.</summary>
    [JsonIgnore] public bool IsPrivate { get; set; }
    [JsonIgnore] public CoreWebView2DownloadOperation? Operation { get; set; }
    [JsonIgnore] public bool IsPaused { get; set; }
    [JsonIgnore] public DateTime? EstimatedEnd { get; set; }
    /// <summary>Every byte's in and Drift is looking for the saved file (Downloads.WatchFinish).</summary>
    [JsonIgnore] public bool IsWatched { get; set; }

    [JsonIgnore] public string FileName => Path.GetFileName(FilePath);

    [JsonIgnore] public bool IsRunning => State == DownloadState.InProgress;

    [JsonIgnore] public Visibility RunningVisibility => IsRunning ? Visibility.Visible : Visibility.Collapsed;

    [JsonIgnore] public Visibility DoneVisibility => IsRunning ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Every byte in, the file being checked and saved: it can't be paused or cancelled now.</summary>
    [JsonIgnore] public bool IsFinishing => IsRunning && !IsPaused && DownloadHold.AllIn(Received, Total);

    /// <summary>Pause and Cancel: while it runs, till it's finishing.</summary>
    [JsonIgnore] public Visibility ControlsVisibility => IsRunning && !IsFinishing ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The folder button: there's something on disk to show.</summary>
    [JsonIgnore] public Visibility FolderVisibility => State == DownloadState.Completed && !IsMissing ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The row's pause button: pause while it runs, resume once it's paused.</summary>
    [JsonIgnore] public string PauseGlyph => IsPaused ? "\uE768" : "\uE769";

    [JsonIgnore] public string PauseLabel => IsPaused ? "Resume" : "Pause";

    [JsonIgnore] public double Percent => Total is > 0 ? Received * 100.0 / Total.Value : 0;

    [JsonIgnore] public bool IsIndeterminate => IsRunning && Total is not > 0;

    [JsonIgnore] public double ContentOpacity => State is DownloadState.Failed or DownloadState.Cancelled || IsMissing ? 0.6 : 1;

    [JsonIgnore] public bool IsMissing => State == DownloadState.Completed && !File.Exists(FilePath);

    [JsonIgnore]
    public string Status
    {
        get
        {
            var host = Uri.TryCreate(Url, UriKind.Absolute, out var uri) ? uri.Host : "";
            switch (State)
            {
                case DownloadState.InProgress:
                    var size = Total is > 0 ? $"{Size(Received)} of {Size(Total.Value)}" : Size(Received);
                    if (IsPaused) return "Paused · " + size;
                    if (IsFinishing) return "Finishing · " + Size(Total!.Value);
                    if (EstimatedEnd is { } end && end > DateTime.Now)
                    {
                        var left = end - DateTime.Now;
                        size += left.TotalMinutes >= 1.5 ? $" · {Math.Round(left.TotalMinutes)} min left" : $" · {Math.Max(1, Math.Round(left.TotalSeconds))} s left";
                    }
                    return size;
                case DownloadState.Completed:
                    return IsMissing ? "Deleted" : string.Join(" · ", new[] { Size(Total ?? Received), host }.Where(p => p.Length > 0));
                case DownloadState.Cancelled:
                    return "Cancelled";
                default:
                    return "Failed" + (FailReason is { Length: > 0 } why ? " · " + why : "");
            }
        }
    }

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.0} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.00} GB",
    };

    private Microsoft.UI.Xaml.Media.ImageSource? _icon;
    private string? _iconFor;

    /// <summary>
    /// Windows' icon for the file (FileIcons), once it's come; the plain file glyph shows till then, or
    /// if there's none. A program's own icon replaces its type's once it's saved.
    /// </summary>
    [JsonIgnore]
    public Microsoft.UI.Xaml.Media.ImageSource? Icon
    {
        get
        {
            LoadIcon();
            return _icon;
        }
    }

    [JsonIgnore] public Visibility GlyphVisibility => Picture is null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>What a row shows under the name in the downloads peek: progress, or when it finished.</summary>
    [JsonIgnore] public string PeekStatus => IsRunning || State != DownloadState.Completed ? Status : DownloadPeek.Ago(Finished ?? Started, DateTime.Now);

    private Microsoft.UI.Xaml.Media.ImageSource? _thumbnail;
    private string? _thumbnailFor;

    /// <summary>
    /// The file's own picture once it's saved (FileThumbnails: an image, a PDF's first page…), or
    /// Windows' icon for its type.
    /// </summary>
    [JsonIgnore]
    public Microsoft.UI.Xaml.Media.ImageSource? Picture
    {
        get
        {
            LoadThumbnail();
            return _thumbnail ?? Icon;
        }
    }

    /// <summary>Finished and still on disk: it can be opened, shown, or dragged somewhere.</summary>
    [JsonIgnore] public bool IsOnDisk => State == DownloadState.Completed && File.Exists(FilePath);

    private async void LoadThumbnail()
    {
        if (!IsOnDisk || FilePath == _thumbnailFor) return;
        _thumbnailFor = FilePath;
        var picture = await FileThumbnails.ForAsync(FilePath);
        if (_thumbnailFor != FilePath || picture == _thumbnail) return;
        _thumbnail = picture;
        Refresh();
    }

    private async void LoadIcon()
    {
        var exists = !IsRunning && File.Exists(FilePath);
        var key = FileIcons.Source(FilePath, exists).Key;
        if (key == _iconFor) return;
        _iconFor = key;
        var icon = await FileIcons.ForAsync(FilePath, exists);
        // Renamed or saved meanwhile: that one's on its way instead.
        if (_iconFor != key || icon == _icon) return;
        _icon = icon;
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Everything shown is worked out from the fields, so one change refreshes it all.</summary>
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

/// <summary>
/// Downloads, newest first. Drift shows them itself (on the Library button, its peek and the library's list) rather
/// than in WebView2's own popup. Finished ones are remembered in %LOCALAPPDATA%\Drift\downloads.json,
/// till they're older than Settings keeps them (DownloadArchive); files go to the folder chosen in Settings,
/// or your Downloads folder.
/// </summary>
internal static class Downloads
{
    private const int MaxSaved = 100;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Drift", "downloads.json");

    // Before Items: static fields start in the order they're written, and Load() needs this.
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    public static ObservableCollection<DownloadItem> Items { get; } = Load();

    /// <summary>A download started, moved on, or finished; or the list changed. On the UI thread.</summary>
    public static event Action? Changed;

    /// <summary>A download finished since the list was last looked at.</summary>
    public static bool HasUnseen { get; private set; }

    public static bool AnyRunning => Items.Any(i => i.IsRunning);

    /// <summary>Where files are saved: the folder chosen in Settings, or your Downloads folder.</summary>
    public static string Folder =>
        Settings.Current.DownloadFolder is { Length: > 0 } chosen && Directory.Exists(chosen) ? chosen : WindowsFolder;

    /// <summary>Your Downloads folder, wherever Windows has it (it can be moved, to OneDrive or another drive).</summary>
    public static string WindowsFolder
    {
        get
        {
            try
            {
                if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out var path) == 0) return path;
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStruct)] Guid id,
        uint flags, IntPtr token, out string path);

    /// <summary>Takes over a download a page started: WebView2's own popup stays closed.</summary>
    public static void Track(CoreWebView2DownloadStartingEventArgs e, bool isPrivate)
    {
        e.Handled = true;
        var operation = e.DownloadOperation;
        var item = new DownloadItem
        {
            FilePath = e.ResultFilePath,
            Url = operation.Uri,
            Started = DateTime.Now,
            Total = operation.TotalBytesToReceive is long total and > 0 ? total : null,
            State = DownloadState.InProgress,
            IsPrivate = isPrivate,
            Operation = operation,
        };
        Items.Insert(0, item);

        operation.BytesReceivedChanged += (_, _) =>
        {
            item.Received = operation.BytesReceived;
            if (operation.TotalBytesToReceive is long t and > 0) item.Total = t;
            item.Refresh();
            Changed?.Invoke();
            if (operation.TotalBytesToReceive is long all and > 0 && operation.BytesReceived >= all) WatchFinish(item, operation);
        };
        operation.EstimatedEndTimeChanged += (_, _) =>
        {
            item.EstimatedEnd = DateTime.TryParse(operation.EstimatedEndTime, out var end) ? end : null;
            item.Refresh();
        };
        operation.StateChanged += (_, _) =>
        {
            item.FilePath = operation.ResultFilePath;
            item.IsPaused = operation.State == CoreWebView2DownloadState.Interrupted
                && operation.InterruptReason is CoreWebView2DownloadInterruptReason.UserPaused or CoreWebView2DownloadInterruptReason.None;
            switch (operation.State)
            {
                case CoreWebView2DownloadState.Completed:
                    Complete(item, operation.BytesReceived);
                    break;
                case CoreWebView2DownloadState.Interrupted when operation.InterruptReason == CoreWebView2DownloadInterruptReason.UserCanceled:
                    item.State = DownloadState.Cancelled;
                    Finish(item);
                    break;
                case CoreWebView2DownloadState.Interrupted when !item.IsPaused:
                    item.State = DownloadState.Failed;
                    item.FailReason = Reason(operation.InterruptReason);
                    Finish(item);
                    break;
            }
            item.Refresh();
            Changed?.Invoke();
        };
        Changed?.Invoke();
    }

    private static void Complete(DownloadItem item, long size)
    {
        if (!item.IsRunning) return;
        item.State = DownloadState.Completed;
        item.Received = size;
        item.Total = size;
        Finish(item);
    }

    /// <summary>
    /// Every byte is in: WebView2 doesn't always say when the file's saved, so Drift looks for itself
    /// until it's finished one way or the other (DownloadHold).
    /// </summary>
    private static async void WatchFinish(DownloadItem item, CoreWebView2DownloadOperation operation)
    {
        if (item.IsWatched) return;
        item.IsWatched = true;
        while (item.IsRunning && item.Operation == operation)
        {
            await Task.Delay(1000);
            if (!item.IsRunning || item.Operation != operation) return;
            bool engineDone;
            string path;
            try
            {
                engineDone = operation.State == CoreWebView2DownloadState.Completed;
                path = operation.ResultFilePath;
            }
            catch
            {
                engineDone = false;
                path = item.FilePath;
            }
            long? onDisk;
            try { onDisk = File.Exists(path) ? new FileInfo(path).Length : null; }
            catch { onDisk = null; }
            if (DownloadHold.Check(engineDone, item.Received, item.Total, onDisk) != DownloadSettle.Done) continue;
            item.FilePath = path;
            Complete(item, item.Total ?? item.Received);
            item.Refresh();
            Changed?.Invoke();
        }
    }

    private static void Finish(DownloadItem item)
    {
        item.Operation = null;
        item.EstimatedEnd = null;
        item.Finished = DateTime.Now;
        HasUnseen = true;
        Save();
    }

    private static string Reason(CoreWebView2DownloadInterruptReason reason) => reason switch
    {
        CoreWebView2DownloadInterruptReason.FileNoSpace => "disk full",
        CoreWebView2DownloadInterruptReason.FileAccessDenied => "couldn't save the file",
        CoreWebView2DownloadInterruptReason.FileNameTooLong => "file name too long",
        CoreWebView2DownloadInterruptReason.FileTooLarge => "file too large",
        CoreWebView2DownloadInterruptReason.FileMalicious or CoreWebView2DownloadInterruptReason.FileSecurityCheckFailed
            or CoreWebView2DownloadInterruptReason.FileBlockedByPolicy => "blocked as unsafe",
        CoreWebView2DownloadInterruptReason.NetworkFailed or CoreWebView2DownloadInterruptReason.NetworkTimeout
            or CoreWebView2DownloadInterruptReason.NetworkDisconnected => "network error",
        CoreWebView2DownloadInterruptReason.ServerUnauthorized or CoreWebView2DownloadInterruptReason.ServerForbidden => "not allowed",
        CoreWebView2DownloadInterruptReason.ServerBadContent => "file not found",
        CoreWebView2DownloadInterruptReason.ServerFailed or CoreWebView2DownloadInterruptReason.ServerNoRange
            or CoreWebView2DownloadInterruptReason.ServerCertificateProblem
            or CoreWebView2DownloadInterruptReason.ServerUnexpectedResponse
            or CoreWebView2DownloadInterruptReason.ServerContentLengthMismatch
            or CoreWebView2DownloadInterruptReason.ServerCrossOriginRedirect => "server error",
        CoreWebView2DownloadInterruptReason.DownloadProcessCrashed or CoreWebView2DownloadInterruptReason.UserShutdown => "Drift closed",
        _ => "",
    };

    public static void MarkSeen()
    {
        if (!HasUnseen) return;
        HasUnseen = false;
        Changed?.Invoke();
    }

    /// <summary>
    /// Stops a download. Once every byte is in, the engine won't: it's checking the file (an .exe is
    /// scanned for safety) and saving it under its name, and it finishes on its own. Then nothing
    /// happens here, rather than the error that used to close Drift. One it finished without saying
    /// is shown as saved.
    /// </summary>
    public static void Cancel(DownloadItem item)
    {
        if (item.Operation is not { } operation) return;
        try
        {
            operation.Cancel();
            return;
        }
        catch
        {
            // Being finished, or already over.
        }
        try
        {
            if (operation.State != CoreWebView2DownloadState.Completed) return;
            Complete(item, operation.BytesReceived);
        }
        catch
        {
            return;
        }
        item.Refresh();
        Changed?.Invoke();
    }

    /// <summary>Pauses a running download, or carries on with a paused one.</summary>
    public static void PauseOrResume(DownloadItem item)
    {
        if (item.Operation is not { } operation) return;
        try
        {
            if (item.IsPaused) operation.Resume();
            else operation.Pause();
            item.IsPaused = !item.IsPaused;
        }
        catch
        {
            return;
        }
        item.Refresh();
        Changed?.Invoke();
    }

    /// <summary>
    /// The file itself, to the Recycle Bin (as Explorer does it: StorageFile's delete skipped the bin), and
    /// off the list. Windows says so if it can't (the file's open somewhere).
    /// </summary>
    public static bool Recycle(DownloadItem item)
    {
        if (!item.IsOnDisk) return false;
        try
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(item.FilePath,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }
        catch
        {
            return false;
        }
        Remove(item);
        return true;
    }

    /// <summary>Takes finished downloads older than Settings keeps them off the list (the files stay).</summary>
    public static void ArchiveOld()
    {
        var now = DateTime.Now;
        var old = Items.Where(i => Leaves(i, now)).ToList();
        if (old.Count == 0) return;
        foreach (var item in old) Items.Remove(item);
        Save();
        Changed?.Invoke();
    }

    private static bool Leaves(DownloadItem item, DateTime now) =>
        DownloadArchive.Leaves(item.IsRunning, item.Finished ?? item.Started, now, Settings.Current.KeepDownloadsDays);

    // ── Where files go ──

    private static readonly List<CoreWebView2Profile> Profiles = new();

    /// <summary>
    /// A profile (Drift's own, or a private window's) saves to the folder chosen in Settings, or your
    /// Downloads folder. Every time: WebView2 remembers a profile's folder from one run to the next.
    /// </summary>
    public static void UseProfile(CoreWebView2Profile profile)
    {
        try
        {
            if (Profiles.Any(p => p.ProfilePath == profile.ProfilePath)) return;
            Profiles.Add(profile);
            ApplyFolder(profile);
        }
        catch { }
    }

    /// <summary>Settings changed: every profile follows the chosen folder.</summary>
    public static void FolderChanged()
    {
        foreach (var profile in Profiles) ApplyFolder(profile);
    }

    private static void ApplyFolder(CoreWebView2Profile profile)
    {
        var folder = Folder;
        try
        {
            if (profile.DefaultDownloadFolderPath != folder) profile.DefaultDownloadFolderPath = folder;
        }
        catch { }
    }

    public static void Open(DownloadItem item)
    {
        if (item.State != DownloadState.Completed || !File.Exists(item.FilePath)) return;
        try { Process.Start(new ProcessStartInfo(item.FilePath) { UseShellExecute = true }); }
        catch { }
    }

    /// <summary>Explorer, on the file itself when it's still there.</summary>
    public static void ShowInFolder(DownloadItem? item)
    {
        try
        {
            if (item is not null && File.Exists(item.FilePath))
                Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
            else
                Process.Start(new ProcessStartInfo(Folder) { UseShellExecute = true });
        }
        catch { }
    }

    public static void Remove(DownloadItem item)
    {
        if (item.IsRunning) return;
        Items.Remove(item);
        Save();
        Changed?.Invoke();
    }

    /// <summary>Forgets finished downloads (the files stay).</summary>
    public static void ClearFinished(DateTime? since = null)
    {
        foreach (var item in Items.Where(i => !i.IsRunning && (since is null || i.Started >= since)).ToList()) Items.Remove(item);
        HasUnseen = false;
        Save();
        Changed?.Invoke();
    }

    /// <summary>The last private window closed: its downloads leave the list.</summary>
    public static void ForgetPrivate()
    {
        foreach (var item in Items.Where(i => i.IsPrivate && !i.IsRunning).ToList()) Items.Remove(item);
        Changed?.Invoke();
    }

    private static ObservableCollection<DownloadItem> Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<DownloadItem[]>(File.ReadAllText(FilePath), Json) is { } saved)
            {
                var now = DateTime.Now;
                return new ObservableCollection<DownloadItem>(saved.Where(i => i.State != DownloadState.InProgress && !Leaves(i, now)));
            }
        }
        catch
        {
            // A damaged file: start with an empty list.
        }
        return new();
    }

    private static void Save()
    {
        var saved = Items.Where(i => !i.IsRunning && !i.IsPrivate).Take(MaxSaved).ToArray();
        string json;
        try { json = JsonSerializer.Serialize(saved, Json); }
        catch { return; }
        _ = Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, json);
            }
            catch { }
        });
    }
}
