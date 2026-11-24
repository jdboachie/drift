using System;

namespace Drift;

/// <summary>
/// Whether a link opening in a new tab comes to the front (Settings: SwitchToNewTabs), and whether a page's new
/// window is one it fills in itself.
/// </summary>
public static class NewTabChoice
{
    /// <summary>
    /// A page opening a window with no address (window.open(), then where it goes or what's in it): it fills it in
    /// itself, so the window has to be handed back to it, or it stays blank.
    /// </summary>
    public static bool PageFillsItIn(string? uri) =>
        string.IsNullOrEmpty(uri) || uri.Equals("about:blank", StringComparison.OrdinalIgnoreCase);

    /// <summary>How long after a Ctrl+click or middle-click the new tab it opens counts as chosen.</summary>
    public const long PressLastsMs = 3000;

    /// <param name="chosen">You chose a new tab (Open link in new tab, Ctrl+click, middle-click); false when the page opened one itself.</param>
    /// <param name="shift">Shift was held, which turns the setting round for that click.</param>
    public static bool ComesToFront(bool chosen, bool switchToNewTabs, bool shift) =>
        !chosen || switchToNewTabs != shift;

    /// <param name="pressedAt">From Environment.TickCount64; 0 for none.</param>
    public static bool PressedJustNow(long pressedAt, long now) =>
        pressedAt != 0 && now - pressedAt < PressLastsMs;
}
