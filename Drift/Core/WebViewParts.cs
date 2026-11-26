using System;
using System.Collections.Generic;

namespace Drift;

/// <summary>
/// Parts of WebView2 itself that it lists among a profile's extensions. They aren't yours, so
/// Settings › Extensions doesn't show them, and removing one would break what it does. The
/// runtime ships some (copying, PDFs) and downloads others into the profile the first time
/// DevTools opens; Chromium marks them component or external-component, never unpacked.
/// </summary>
public static class WebViewParts
{
    private static readonly HashSet<string> Ids = new(StringComparer.Ordinal)
    {
        "dgiklkfkllikcanfonkcabmbdfmgleag", // Microsoft Clipboard Extension
        "mhjfbmdgcfjbbpaeojofohoefgiehjai", // Microsoft Edge PDF Viewer
        "kfbdpdaobnofkbopebjglnaadopfikhh", // Microsoft Edge DevTools Enhancements
        "cgjgjfacjflmgphhhepmbhhbgjieaecn", // Microsoft Edge Unminification Extension (DevTools)
    };

    public static bool IsBuiltIn(string id) => Ids.Contains(id);
}
