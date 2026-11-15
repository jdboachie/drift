using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Drift;

/// <summary>
/// A saved file's picture, as Explorer's thumbnails show it: an image's own pixels, a PDF's or a
/// document's first page, a video's frame. Files Windows has no picture for get none (their type's icon
/// shows instead, FileIcons).
/// </summary>
public static class FileThumbnails
{
    /// <summary>Big enough to stay sharp at 40px on a 200% display.</summary>
    private const uint Size = 96;

    private static readonly Dictionary<string, Task<ImageSource?>> Loaded = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>On the UI thread. Asked again for a file that's changed since.</summary>
    public static Task<ImageSource?> ForAsync(string path)
    {
        DateTime written;
        try { written = File.GetLastWriteTimeUtc(path); }
        catch { written = default; }
        var key = path + "|" + written.Ticks;
        if (Loaded.TryGetValue(key, out var loading)) return loading;
        return Loaded[key] = LoadAsync(path);
    }

    private static async Task<ImageSource?> LoadAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, Size, ThumbnailOptions.ResizeThumbnail);
            // Only an icon for its type: FileIcons has that already, drawn the same as everywhere else.
            if (thumbnail is null || thumbnail.Type != ThumbnailType.Image) return null;
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(thumbnail);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
