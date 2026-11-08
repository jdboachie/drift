namespace Drift;

/// <summary>
/// The site settings button at the end of the address pill, like Arc's: out of sight until you're
/// in the sidebar, so the pill is just the address.
/// </summary>
public static class SiteSettingsButton
{
    /// <param name="hasSite">The tab in front shows a site (not one of Drift's own pages or a crash).</param>
    /// <param name="panelOpen">Its panel is open; the pointer leaves the sidebar to reach it.</param>
    /// <param name="focused">It has keyboard focus, so Tab can reach it.</param>
    public static bool Shows(bool hasSite, bool sidebarHovered, bool panelOpen, bool focused) =>
        hasSite && (sidebarHovered || panelOpen || focused);
}
