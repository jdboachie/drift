namespace Drift;

/// <summary>
/// A list opened beside something (a closed folder's preview, Folders.cs) stays while the pointer's
/// on its way to it, not only while it's on it: the "safe triangle" menus use (Amazon's menu aim).
/// Heading for the list's last item from the header cuts across the sidebar below the header, off
/// both, and the list closed on the way.
/// </summary>
public static class MenuAim
{
    /// <summary>
    /// Whether (x, y) is in the triangle from where the pointer left (from) to the list's near edge
    /// (edgeX, from topY to bottomY): on its way there, from wherever on the edge it's aiming.
    /// </summary>
    public static bool Toward(double x, double y, double fromX, double fromY, double edgeX, double topY, double bottomY)
    {
        if (edgeX == fromX) return false;
        // How far across: 0 where it left, 1 at the list's edge.
        var across = (x - fromX) / (edgeX - fromX);
        if (across < 0 || across > 1) return false;
        var top = fromY + (topY - fromY) * across;
        var bottom = fromY + (bottomY - fromY) * across;
        return y >= top && y <= bottom;
    }
}
