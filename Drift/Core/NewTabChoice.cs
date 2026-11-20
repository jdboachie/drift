namespace Drift;

/// <summary>Whether a link opening in a new tab comes to the front (Settings: SwitchToNewTabs).</summary>
public static class NewTabChoice
{
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
