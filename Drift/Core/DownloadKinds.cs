using System;
using System.Collections.Generic;
using System.IO;

namespace Drift;

/// <summary>What sort of file a download is, for choosing which the downloads peek shows (as in Arc).</summary>
public enum DownloadKind
{
    Images,
    Videos,
    Audio,
    Documents,
    Archives,
    Apps,
    Other,
}

public static class DownloadKinds
{
    public static IReadOnlyList<DownloadKind> All { get; } = Enum.GetValues<DownloadKind>();

    private static readonly Dictionary<string, DownloadKind> ByExtension = Build(
        (DownloadKind.Images, "jpg jpeg png gif webp bmp svg ico tif tiff heic heif avif raw"),
        (DownloadKind.Videos, "mp4 mov m4v webm mkv avi wmv flv mpg mpeg 3gp"),
        (DownloadKind.Audio, "mp3 m4a wav flac aac ogg oga opus wma aiff"),
        (DownloadKind.Documents, "pdf doc docx odt rtf txt md csv tsv xls xlsx ods ppt pptx odp epub json xml html htm pages numbers key"),
        (DownloadKind.Archives, "zip rar 7z tar gz tgz bz2 xz zst iso dmg cab"),
        (DownloadKind.Apps, "exe msi msix msixbundle appx appxbundle bat cmd ps1 apk"));

    private static Dictionary<string, DownloadKind> Build(params (DownloadKind Kind, string Extensions)[] kinds)
    {
        var map = new Dictionary<string, DownloadKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var (kind, extensions) in kinds)
            foreach (var extension in extensions.Split(' ')) map["." + extension] = kind;
        return map;
    }

    public static DownloadKind Of(string fileName) =>
        ByExtension.TryGetValue(Path.GetExtension(fileName), out var kind) ? kind : DownloadKind.Other;

    /// <summary>As the Library button's menu lists it.</summary>
    public static string Name(DownloadKind kind) => kind switch
    {
        DownloadKind.Images => "Images",
        DownloadKind.Videos => "Videos",
        DownloadKind.Audio => "Audio",
        DownloadKind.Documents => "Documents",
        DownloadKind.Archives => "Archives",
        DownloadKind.Apps => "Apps",
        _ => "Other files",
    };
}

/// <summary>
/// As in Arc, finished downloads leave the list by themselves after a while (the files stay where they are).
/// </summary>
public static class DownloadArchive
{
    /// <summary>The choices Settings offers, in days; 0 keeps them.</summary>
    public static IReadOnlyList<int> Choices { get; } = [1, 7, 30, 0];

    public const int DefaultDays = 30;

    public static bool Leaves(bool running, DateTime finished, DateTime now, int keepDays) =>
        !running && keepDays > 0 && now - finished >= TimeSpan.FromDays(keepDays);
}
