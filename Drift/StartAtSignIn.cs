using System;
using System.IO;
using Microsoft.Win32;

namespace Drift;

/// <summary>
/// Drift opens when you sign in to Windows (Settings › General): a line in your account's Run key
/// (HKEY_CURRENT_USER, no admin needed), the one Task Manager's Startup apps lists. Turned off there,
/// Windows leaves the line and marks it in StartupApproved; turning it on here clears that mark.
/// <para>The line isn't moved to follow Drift if it's run from somewhere else, as DefaultBrowser's
/// registration is: a second copy (the demo) would take it over just by opening.</para>
/// </summary>
internal static class StartAtSignIn
{
    private const string Name = "Drift";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private static string Exe => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Drift.exe");

    public static bool IsOn
    {
        get
        {
            try
            {
                using var run = Registry.CurrentUser.OpenSubKey(RunKey);
                using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
                return StartupEntry.IsOn(run?.GetValue(Name) as string, approved?.GetValue(Name) as byte[]);
            }
            catch
            {
                return false;
            }
        }
    }

    public static void Set(bool on)
    {
        using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (on) run.SetValue(Name, StartupEntry.Command(Exe));
            else run.DeleteValue(Name, throwOnMissingValue: false);
        }
        // Without a mark, Windows runs it.
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
        approved?.DeleteValue(Name, throwOnMissingValue: false);
    }
}
