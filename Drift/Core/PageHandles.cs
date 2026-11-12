namespace Drift;

/// <summary>
/// Which of a split's pages show their handle (the tab on their top edge): only the page under the
/// pointer, so the handles are there as you reach for them and out of sight otherwise.
/// </summary>
public static class PageHandles
{
    /// <param name="pointer">Where the pointer is in the window, or null when it isn't over it.</param>
    /// <param name="left">The left page's bounds in the window; <paramref name="right"/>, the right's.</param>
    public static (bool Left, bool Right) Shown((double X, double Y)? pointer, Area left, Area right) =>
        pointer is var (x, y) ? (Over(left, x, y), Over(right, x, y)) : (false, false);

    /// <summary>On a page, or in the gap above it up to the window's top, where its handle sits.</summary>
    private static bool Over(Area page, double x, double y) => x >= page.X && x < page.Right && y < page.Bottom;
}
