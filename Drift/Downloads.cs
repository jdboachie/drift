using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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

    /// <summary>The folder button: there's something on disk to show.</summary>
    [JsonIgnore] public Visibility FolderVisibility => State == DownloadState.Completed && !IsMissing ? Visibility.Visible : Visibility.Collapsed;

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

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Everything shown is worked out from the fields, so one change refreshes it all.</summary>
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

/// <summary>
/// Downloads, newest first. Drift shows them itself (the sidebar's downloads row and its list) rather
/// than in WebView2's own popup. Finished ones are remembered in %LOCALAPPDATA%\Drift\downloads.json;
/// files go where WebView2 puts them, your Downloads folder.
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

    public static string Folder
    {
        get
        {
            var latest = Items.FirstOrDefault(i => i.State == DownloadState.Completed && File.Exists(i.FilePath));
            if (latest is not null && Path.GetDirectoryName(latest.FilePath) is { } folder) return folder;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }

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
            item.IsPaused = operation.State == CoreWebView2DownloadState.Interrupted && operation.CanResume
                && operation.InterruptReason == CoreWebView2DownloadInterruptReason.None;
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

    public static void Cancel(DownloadItem item) => item.Operation?.Cancel();

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
                return new ObservableCollection<DownloadItem>(saved.Where(i => i.State != DownloadState.InProgress));
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
