namespace Drift;

/// <summary>
/// Drift opening when you sign in to Windows, as Windows keeps it: a line in the account's Run key
/// (the command), and maybe a mark from Task Manager's Startup apps saying it's been turned off there.
/// No UI or registry here, so it can be tested; StartAtSignIn reads and writes them.
/// </summary>
public static class StartupEntry
{
    public static string Command(string exe) => $"\"{exe}\"";

    /// <param name="command">Drift's line in the Run key, if there is one.</param>
    /// <param name="approved">Task Manager's mark for it, if any: an odd first byte is off, an even one on.</param>
    public static bool IsOn(string? command, byte[]? approved) =>
        !string.IsNullOrEmpty(command) && !(approved is { Length: > 0 } && approved[0] % 2 == 1);
}
