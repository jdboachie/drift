using System;
using System.Linq;

namespace Drift;

/// <summary>
/// The page's zoom, as shown by Drift. Zooming itself is Chromium's own (Ctrl+plus/minus/0,
/// Ctrl+wheel); WinUI's WebView2 doesn't tell the app about it, so the page script reports its
/// pixel ratio, which zoom multiplies, and this works out the level from that.
/// </summary>
public static class PageZoom
{
    // Chromium's zoom steps, which Ctrl+plus/minus and Ctrl+wheel move between.
    private static readonly double[] Steps = [0.25, 0.33, 0.5, 0.67, 0.75, 0.8, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4, 5];

    /// <param name="devicePixelRatio">The page's window.devicePixelRatio.</param>
    /// <param name="screenScale">The screen's own scale (its XamlRoot.RasterizationScale).</param>
    public static double Of(double devicePixelRatio, double screenScale)
    {
        var zoom = devicePixelRatio / screenScale;
        var step = Steps.MinBy(s => Math.Abs(s - zoom));
        return Math.Abs(step - zoom) < 0.005 ? step : Math.Round(zoom, 2);
    }

    public static string Label(double zoom) => $"{Math.Round(zoom * 100)}%";

    public static bool IsZoomed(double zoom) => Math.Abs(zoom - 1) >= 0.005;
}
