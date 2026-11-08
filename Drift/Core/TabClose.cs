namespace Drift;

/// <summary>What a tab's × (or "−", or Ctrl+W) does.</summary>
public enum TabClose
{
    /// <summary>The page it has open in Peek closes; the tab stays.</summary>
    ClosePeek,
    /// <summary>An Essential, pinned or folder tab lets its page go and stays in the sidebar.</summary>
    Unload,
    /// <summary>A regular tab closes.</summary>
    Remove,
}

/// <summary>A tab's Peek goes before the tab, pinned or not. No UI here, so it can be tested.</summary>
public static class TabCloses
{
    public static TabClose First(bool hasPeek, bool pinned) =>
        hasPeek ? TabClose.ClosePeek : pinned ? TabClose.Unload : TabClose.Remove;
}
