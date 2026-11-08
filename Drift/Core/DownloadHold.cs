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
        if (total is not > 0 || received < total) return DownloadSettle.Running;
        return fileOnDisk == total ? DownloadSettle.Done : DownloadSettle.Waiting;
    }
}
