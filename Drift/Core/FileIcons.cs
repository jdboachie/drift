using System;
using System.IO;

namespace Drift;

/// <summary>
/// A download's icon is Windows' own for its kind of file, as Explorer shows it: one per type (every
/// PDF the same), except programs, shortcuts and icon files, which each have their own once they're on
/// disk. This part is which icon a file gets; FileIconImages.cs asks Windows for it.
/// </summary>
public static partial class FileIcons
{
    /// <summary>Kinds of file whose icon is inside each one, not their type's.</summary>
    private static readonly string[] OwnIcon = [".exe", ".ico", ".lnk", ".url", ".cur", ".ani", ".scr"];

    /// <summary>
    /// Which icon <paramref name="path"/> gets: its type's (Key is the extension, "" for none, and the
    /// file needn't exist), or its own (Key is the path, and it's read from the file).
    /// </summary>
    public static (string Key, bool FromFile) Source(string path, bool exists)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (exists && Array.IndexOf(OwnIcon, extension) >= 0) return (path, true);
        return (extension, false);
    }
}

/// <summary>An icon's pixels (BGRA, as Windows gives them) made ready to show.</summary>
public static class IconPixels
{
    /// <summary>
    /// In place: premultiplied by their transparency, as XAML draws them. An old icon with no
    /// transparency of its own takes it from its <paramref name="mask"/> (BGRA too; white is clear).
    /// </summary>
    public static void Prepare(byte[] pixels, byte[]? mask)
    {
        var hasAlpha = false;
        for (var i = 3; i < pixels.Length; i += 4)
            if (pixels[i] != 0) { hasAlpha = true; break; }

        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = hasAlpha ? pixels[i + 3] : mask is not null && mask[i] != 0 ? (byte)0 : (byte)255;
            pixels[i] = (byte)(pixels[i] * alpha / 255);
            pixels[i + 1] = (byte)(pixels[i + 1] * alpha / 255);
            pixels[i + 2] = (byte)(pixels[i + 2] * alpha / 255);
            pixels[i + 3] = alpha;
        }
    }
}
