using System;
using System.Collections.Generic;
using System.Linq;

namespace Drift;

/// <summary>
/// The engine (WebView2) profiles your Profiles run in, one each, so each keeps its own logins, cookies and history, as
/// Dia's and Safari's do. No UI here, so it can be tested.
/// </summary>
public static class ProfileIds
{
    /// <summary>The engine's own profile: your default Profile's, the data you had before there were Profiles.</summary>
    public const string Default = "Default";

    /// <summary>What your default Profile's called till you rename it: your setup from before there were Profiles.</summary>
    public const string DefaultName = "Default";

    /// <summary>The private window's (MainWindow.PrivateOptions): never a profile's.</summary>
    private const string Private = "Private";

    public static bool IsEngineDefault(string id) => string.Equals(id, Default, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The id for a new profile: "Profile 2", "Profile 3"…, the first not taken. It names the profile's folder of data,
    /// so it stays as it is when the profile's renamed; the engine takes letters, digits and spaces in it.
    /// </summary>
    /// <param name="deleted">Profiles deleted since the engine started: their data goes when it stops, so not till then.</param>
    public static string Next(IEnumerable<string> taken, IEnumerable<string>? deleted = null)
    {
        var used = new HashSet<string>(taken.Concat(deleted ?? []), StringComparer.OrdinalIgnoreCase) { Private };
        for (var n = 2; ; n++)
            if (!used.Contains($"Profile {n}")) return $"Profile {n}";
    }

    /// <summary>A name as typed, trimmed; null if there's nothing to it.</summary>
    public static string? CleanName(string? name) => string.IsNullOrWhiteSpace(name) ? null : name.Trim();

    /// <summary>Another profile's already called that (Arc: "A Profile with that name already exists.").</summary>
    public static bool NameTaken(string name, IEnumerable<string> names) =>
        names.Any(n => string.Equals(n.Trim(), name.Trim(), StringComparison.CurrentCultureIgnoreCase));

    /// <summary>Deleting one, as Safari allows it: any but your default Profile.</summary>
    public static bool CanDelete(string id) => !IsEngineDefault(id);

    /// <summary>The tooltip on the default Profile's Delete.</summary>
    public const string DefaultCantBeDeleted = "Your default Profile can’t be deleted";

    /// <summary>
    /// Each Profile's engine profile, from what a session saved (null for Default). Before, several Profiles could share
    /// one; now each Profile has its own: the first to use one keeps it, and each of the others starts fresh in a new one.
    /// </summary>
    public static IReadOnlyList<string> Unshare(IReadOnlyList<string?> saved)
    {
        var ids = saved.Select(id => id ?? Default).ToList();
        var kept = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < ids.Count; i++)
        {
            if (IsEngineDefault(ids[i])) ids[i] = Default;
            if (!kept.Add(ids[i]))
            {
                ids[i] = Next(ids);
                kept.Add(ids[i]);
            }
        }
        return ids;
    }
}
