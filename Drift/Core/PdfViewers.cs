using System;

namespace Drift;

/// <summary>What a DevTools target is, as far as the PDF tools go (PdfTools.cs).</summary>
public enum PdfTarget
{
    /// <summary>Nothing to do with PDFs.</summary>
    None,
    /// <summary>A target with no address yet, which is how a viewer starts.</summary>
    Blank,
    /// <summary>Edge's PDF viewer.</summary>
    Viewer,
}

/// <summary>Telling Edge's PDF viewer apart from the engine's other targets, and its unsaved edits.</summary>
public static class PdfViewers
{
    /// <summary>A target from Target.targetCreated or Target.targetInfoChanged, by its type and address.</summary>
    public static PdfTarget Classify(string? type, string? url) => (type, url ?? "") switch
    {
        ("webview", var u) when u.Contains("/edge_pdf/", StringComparison.Ordinal) => PdfTarget.Viewer,
        ("other", "") => PdfTarget.Blank,
        _ => PdfTarget.None,
    };

    /// <summary>The viewer puts a * before the tab's title while it has edits that aren't saved.</summary>
    public static bool HasUnsavedEdits(string title) => title.StartsWith('*');
}
