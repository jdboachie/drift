using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Drift;

/// <summary>
/// Drift as a browser Windows knows about: registered for http, https and .html files for your
/// Windows account (HKEY_CURRENT_USER, no admin needed), so it shows in Settings › Default apps.
/// Windows doesn't let apps make themselves the default; you pick Drift there, and Settings opens
/// on Drift's page to make that one click.
/// </summary>
internal static class DefaultBrowser
{
    private const string AppName = "Drift";
    private const string UrlProgId = "DriftURL";
    private const string HtmlProgId = "DriftHTML";
    private const string ClientKey = @"Software\Clients\StartMenuInternet\Drift";
    private static readonly string[] FileTypes = { ".htm", ".html", ".xhtml", ".shtml" };

    private static string Exe => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Drift.exe");
    private static string OpenCommand => $"\"{Exe}\" \"%1\"";

    public static bool IsRegistered =>
        Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications")?.GetValue(AppName) is string;

    /// <summary>Whether web links open in Drift (Windows' choice for http and https).</summary>
    public static bool IsDefault => ProgIdFor("http") == UrlProgId && ProgIdFor("https") == UrlProgId;

    private static string? ProgIdFor(string scheme)
    {
        var path = $@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\{scheme}";
        // Newer Windows 11 builds keep the choice in UserChoiceLatest; older ones in UserChoice.
        using var latest = Registry.CurrentUser.OpenSubKey(path + @"\UserChoiceLatest");
        if (latest?.GetValue("ProgId") is string id) return id;
        using var latestId = Registry.CurrentUser.OpenSubKey(path + @"\UserChoiceLatest\ProgId");
        if (latestId?.GetValue("ProgId") is string nested) return nested;
        using var choice = Registry.CurrentUser.OpenSubKey(path + @"\UserChoice");
        return choice?.GetValue("ProgId") as string;
    }

    /// <summary>Keeps the registration pointing at this copy of Drift, if Drift is registered at all.</summary>
    public static void RefreshIfRegistered()
    {
        try
        {
            if (!IsRegistered) return;
            using var command = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{UrlProgId}\shell\open\command");
            if (command?.GetValue("") as string != OpenCommand) Register();
        }
        catch
        {
            // Leave it as it was.
        }
    }

    public static void Register()
    {
        using (var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes"))
        {
            WriteProgId(classes, UrlProgId, "Drift URL");
            WriteProgId(classes, HtmlProgId, "Drift HTML Document");
        }

        using (var client = Registry.CurrentUser.CreateSubKey(ClientKey))
        {
            client.SetValue("", AppName);
            using (var icon = client.CreateSubKey("DefaultIcon")) icon.SetValue("", $"{Exe},0");
            using (var open = client.CreateSubKey(@"shell\open\command")) open.SetValue("", $"\"{Exe}\"");
            using var capabilities = client.CreateSubKey("Capabilities");
            capabilities.SetValue("ApplicationName", AppName);
            capabilities.SetValue("ApplicationDescription", "A calm, sidebar-first browser.");
            capabilities.SetValue("ApplicationIcon", $"{Exe},0");
            using (var menu = capabilities.CreateSubKey("StartMenu")) menu.SetValue("StartMenuInternet", AppName);
            using (var urls = capabilities.CreateSubKey("URLAssociations"))
            {
                urls.SetValue("http", UrlProgId);
                urls.SetValue("https", UrlProgId);
            }
            using var files = capabilities.CreateSubKey("FileAssociations");
            foreach (var type in FileTypes) files.SetValue(type, HtmlProgId);
        }

        using (var registered = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            registered.SetValue(AppName, ClientKey + @"\Capabilities");

        // Tell Explorer and Settings the associations changed.
        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
    }

    private static void WriteProgId(RegistryKey classes, string progId, string description)
    {
        using var key = classes.CreateSubKey(progId);
        key.SetValue("", description);
        key.SetValue("FriendlyTypeName", description);
        using (var app = key.CreateSubKey("Application"))
        {
            app.SetValue("ApplicationName", AppName);
            app.SetValue("ApplicationIcon", $"{Exe},0");
        }
        using (var icon = key.CreateSubKey("DefaultIcon")) icon.SetValue("", $"{Exe},0");
        using var command = key.CreateSubKey(@"shell\open\command");
        command.SetValue("", OpenCommand);
    }

    /// <summary>Settings › Default apps, on Drift's page, where "Set default" makes it the browser.</summary>
    public static void OpenSystemSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo($"ms-settings:defaultapps?registeredAppUser={AppName}") { UseShellExecute = true });
        }
        catch
        {
            Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
        }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
