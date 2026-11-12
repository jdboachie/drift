using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Drift;

/// <summary>
/// Windows' icon for a file, at 48px (sharp at any display scale for the 24 it's shown at), from the
/// same place Explorer gets it. Each is asked for once, off the UI thread, and kept.
/// </summary>
public static partial class FileIcons
{
    private const int Size = 48;
    private static readonly Dictionary<string, Task<ImageSource?>> Loaded = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The icon for <paramref name="path"/> (on the UI thread); null if Windows has none to give.</summary>
    public static Task<ImageSource?> ForAsync(string path, bool exists)
    {
        var (key, fromFile) = Source(path, exists);
        if (Loaded.TryGetValue(key, out var loading)) return loading;
        return Loaded[key] = LoadAsync(fromFile ? path : "file" + key, fromFile);
    }

    private static async Task<ImageSource?> LoadAsync(string name, bool fromFile)
    {
        byte[]? pixels;
        try { pixels = await Task.Run(() => Pixels(name, fromFile)); }
        catch { pixels = null; }
        if (pixels is null) return null;
        var bitmap = new WriteableBitmap(Size, Size);
        using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(pixels, 0, pixels.Length);
        bitmap.Invalidate();
        return bitmap;
    }

    /// <summary>The icon's pixels, premultiplied BGRA, 48×48; null if there's no icon to be had.</summary>
    private static byte[]? Pixels(string name, bool fromFile)
    {
        var info = new ShFileInfo();
        var flags = SHGFI_SYSICONINDEX | (fromFile ? 0u : SHGFI_USEFILEATTRIBUTES);
        if (SHGetFileInfo(name, FILE_ATTRIBUTE_NORMAL, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), flags) == IntPtr.Zero) return null;
        var imageList = typeof(IImageList).GUID;
        if (SHGetImageList(SHIL_EXTRALARGE, ref imageList, out var list) != 0) return null;
        try
        {
            if (list.GetIcon(info.iIcon, ILD_TRANSPARENT, out var icon) != 0 || icon == IntPtr.Zero) return null;
            try { return IconToPixels(icon); }
            finally { DestroyIcon(icon); }
        }
        finally { Marshal.ReleaseComObject(list); }
    }

    private static byte[]? IconToPixels(IntPtr icon)
    {
        if (!GetIconInfo(icon, out var parts)) return null;
        try
        {
            // A black-and-white icon has no colour part: not worth showing.
            if (parts.hbmColor == IntPtr.Zero) return null;
            var color = Bits(parts.hbmColor);
            if (color is null) return null;
            var mask = parts.hbmMask == IntPtr.Zero ? null : Bits(parts.hbmMask);
            IconPixels.Prepare(color, mask);
            return color;
        }
        finally
        {
            if (parts.hbmColor != IntPtr.Zero) DeleteObject(parts.hbmColor);
            if (parts.hbmMask != IntPtr.Zero) DeleteObject(parts.hbmMask);
        }
    }

    /// <summary>A bitmap's pixels as 48×48 BGRA, top row first.</summary>
    private static byte[]? Bits(IntPtr bitmap)
    {
        var header = new BitmapInfoHeader
        {
            biSize = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
            biWidth = Size,
            biHeight = -Size,
            biPlanes = 1,
            biBitCount = 32,
        };
        var pixels = new byte[Size * Size * 4];
        var screen = GetDC(IntPtr.Zero);
        try
        {
            return GetDIBits(screen, bitmap, 0, Size, pixels, ref header, 0) == Size ? pixels : null;
        }
        finally { ReleaseDC(IntPtr.Zero, screen); }
    }

    // ── Windows ──

    private const uint SHGFI_SYSICONINDEX = 0x4000, SHGFI_USEFILEATTRIBUTES = 0x10, FILE_ATTRIBUTE_NORMAL = 0x80;
    private const int SHIL_EXTRALARGE = 2, ILD_TRANSPARENT = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public bool fIcon;
        public int xHotspot, yHotspot;
        public IntPtr hbmMask, hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    /// <summary>The shell's image list, as far as GetIcon (the methods before it are only placeholders).</summary>
    [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        [PreserveSig] int Add(IntPtr image, IntPtr mask, out int index);
        [PreserveSig] int ReplaceIcon(int i, IntPtr icon, out int index);
        [PreserveSig] int SetOverlayImage(int image, int overlay);
        [PreserveSig] int Replace(int i, IntPtr image, IntPtr mask);
        [PreserveSig] int AddMasked(IntPtr image, int mask, out int index);
        [PreserveSig] int Draw(IntPtr parameters);
        [PreserveSig] int Remove(int i);
        [PreserveSig] int GetIcon(int i, int flags, out IntPtr icon);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShFileInfo info, uint size, uint flags);

    [DllImport("shell32.dll")]
    private static extern int SHGetImageList(int list, ref Guid iid, out IImageList imageList);

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr gdiObject);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BitmapInfoHeader info, uint usage);
}
