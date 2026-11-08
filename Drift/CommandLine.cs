using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Windows.AppLifecycle;

namespace Drift;

/// <summary>
/// The pages Drift is asked to open when it starts: what Windows passes it as the default browser
/// ("Drift.exe" "https://…", or a file path for an .html file), or what you type after Drift.exe.
/// </summary>
internal static class CommandLine
{
    public static IReadOnlyList<Uri> UrisIn(IEnumerable<string> arguments) =>
        arguments.Select(ToUri).OfType<Uri>().ToList();

    /// <summary>The links in another Drift's start-up, handed over to this one.</summary>
    public static IReadOnlyList<Uri> UrisIn(AppActivationArguments activation)
    {
        if (activation.Data is not Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch) return [];
        var arguments = Split(launch.Arguments ?? "");
        // Unpackaged apps get their whole command line, program first.
        if (arguments.Count > 0 && arguments[0].EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) arguments.RemoveAt(0);
        return UrisIn(arguments);
    }

    private static Uri? ToUri(string argument)
    {
        var text = argument.Trim();
        if (text.Length == 0 || text.StartsWith('-')) return null;
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") return uri;
        try
        {
            var path = Path.GetFullPath(text);
            if (File.Exists(path)) return new Uri(path);
        }
        catch
        {
            // Not a path.
        }
        // A bare address (example.com), as you'd type it in the address bar.
        if (!text.Contains('\\') && MainWindow.TryParseAddress(text, out var address)
            && address.Scheme is "http" or "https") return address;
        return null;
    }

    /// <summary>Splits a command line the way Windows does for any program.</summary>
    private static List<string> Split(string commandLine)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(commandLine)) return result;
        var argv = CommandLineToArgvW(commandLine, out var count);
        if (argv == IntPtr.Zero) return result;
        try
        {
            for (var i = 0; i < count; i++)
                result.Add(Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size)) ?? "");
        }
        finally
        {
            LocalFree(argv);
        }
        return result;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
