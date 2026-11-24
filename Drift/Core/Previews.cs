namespace Drift;

/// <summary>
/// The previews hovering a pinned tab shows, each with its switch in Settings › General › Previews. A folder's (its
/// tabs) is always on.
/// </summary>
public enum PreviewKind { Gmail, GoogleCalendar, OutlookMail, OutlookCalendar }

/// <summary>Where a site's preview has got to.</summary>
public enum PreviewState { Loading, Ready, SignedOut, Failed }

public static class Previews
{
    /// <summary>The service a preview is fetched from (Outlook's mail and calendar are one sign-in).</summary>
    private static string Service(PreviewKind kind) => kind switch
    {
        PreviewKind.Gmail => "Gmail",
        PreviewKind.GoogleCalendar => "Google Calendar",
        _ => "Outlook",
    };

    /// <summary>
    /// What needs you about a preview, for Settings: signed out, or not reached. Null when all's well,
    /// still loading, or nothing's pinned for it — Settings only speaks up when there's something to do.
    /// </summary>
    public static string? Trouble(PreviewKind kind, bool hasTab, PreviewState state) =>
        !hasTab ? null : state switch
        {
            PreviewState.SignedOut => $"You're signed out of {Service(kind)}",
            PreviewState.Failed => $"We couldn't reach {Service(kind)}",
            _ => null,
        };
}
