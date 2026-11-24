namespace Drift;

/// <summary>
/// Pressing a tab's row in the sidebar. A plain press of the left button switches to the tab at once, as Chrome's tab
/// strip does (tab.cc, Tab::OnMousePressed) and Arc's sidebar does, rather than when it's let go: every switch by
/// click comes as long as a click is held sooner. A press that goes on to drag puts back the tab you were on
/// (MainWindow.Tabs_DragItemsStarting), as Arc keeps one to revert to. Ctrl and Shift pick tabs on release (Picks.cs),
/// the row's own buttons are theirs, and a finger still switches on lifting, so the list can be scrolled.
/// </summary>
public static class TabPress
{
    public static bool SwitchesOnPress(bool mouse, bool leftButton, bool ctrl, bool shift, bool onControl) =>
        mouse && leftButton && !ctrl && !shift && !onControl;
}
