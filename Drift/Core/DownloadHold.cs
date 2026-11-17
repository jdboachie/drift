namespace Drift;

public enum DownloadSettle { Running, Waiting, Done }

/// <summary>
/// Whether a download has finished. WebView2 doesn't always announce it, so once every byte is in, the
/// file under its final name (the engine renames it from its .crdownload only at the end) settles it too.
/// Every byte in with no such file yet: it's still being checked (a virus scan, say) before it's saved.
/// </summary>
public static class DownloadHold
{
    public static DownloadSettle Check(bool engineDone, long received, long? total, long? fileOnDisk)
    {
        if (engineDone) return DownloadSettle.Done;
        if (!AllIn(received, total)) return DownloadSettle.Running;
        return fileOnDisk == total ? DownloadSettle.Done : DownloadSettle.Waiting;
    }

    /// <summary>
    /// Every byte is in: the last stage, the file being checked and saved under its name, which the
    /// engine won't let be paused or cancelled.
    /// </summary>
    public static bool AllIn(long received, long? total) => total is > 0 && received >= total;
}
