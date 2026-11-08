namespace Drift;

/// <summary>
/// Chromium's own floating video window is a top-level window of WebView2's browser process, and
/// Windows gives it a taskbar button of its own. Of that process's windows it's the one shown always
/// on top; that's how it's found (its title is translated).
/// </summary>
public static class BrowserPip
{
    public static bool IsPlayer(bool visible, bool alwaysOnTop) => visible && alwaysOnTop;
}
