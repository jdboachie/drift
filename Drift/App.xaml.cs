using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Drift;

public partial class App : Application
{
    private static readonly object Gate = new();
    private static MainWindow? _window;

    /// <summary>Drift's main window (not a private one).</summary>
    internal static MainWindow? Main => _window;
    private static DispatcherQueue? _queue;
    // Links handed over by another Drift before this one's window was up.
    private static readonly List<Uri> Early = new();

    public App()
    {
        InitializeComponent();
        // What went wrong, for the next look: %LOCALAPPDATA%\Drift\crash.log.
        UnhandledException += (_, e) =>
        {
            // XAML's own message too: for its errors it often says what failed when the exception doesn't.
            LogCrash(e.Exception, e.Message);
            // A button or menu item that failed shouldn't take every open tab down with it: noted, and
            // Drift carries on.
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject as Exception);
        // DRIFT_TRACE=1: every exception as it's thrown, caught or not, with where it came from, in
        // first-chance.log. A crash XAML reports only as "Unspecified error" often starts as one of these.
        if (Environment.GetEnvironmentVariable("DRIFT_TRACE") == "1")
            AppDomain.CurrentDomain.FirstChanceException += (_, e) => LogFirstChance(e.Exception);
    }

    [ThreadStatic] private static bool _loggingFirstChance;

    private static void LogFirstChance(Exception error)
    {
        // Writing the log may throw too: not again from inside itself.
        if (_loggingFirstChance) return;
        _loggingFirstChance = true;
        try
        {
            var folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Drift");
            System.IO.Directory.CreateDirectory(folder);
            System.IO.File.AppendAllText(System.IO.Path.Combine(folder, "first-chance.log"),
                $"{DateTime.Now:O} [{Environment.CurrentManagedThreadId}] {error.GetType().Name}: {error.Message}{Environment.NewLine}{new System.Diagnostics.StackTrace(1, false)}{Environment.NewLine}");
        }
        catch
        {
            // Nowhere to write it.
        }
        finally
        {
            _loggingFirstChance = false;
        }
    }

    internal static void LogCrash(Exception? error, string? message = null)
    {
        try
        {
            var folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Drift");
            System.IO.Directory.CreateDirectory(folder);
            var said = string.IsNullOrWhiteSpace(message) ? "" : $"{message}{Environment.NewLine}";
            System.IO.File.AppendAllText(System.IO.Path.Combine(folder, "crash.log"),
                $"{DateTime.Now:O}{Environment.NewLine}{said}{error}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Nowhere to write it.
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Registered as a browser from somewhere else (the app moved, or was rebuilt elsewhere):
        // point Windows at this copy.
        DefaultBrowser.RefreshIfRegistered();

        var uris = CommandLine.UrisIn(Environment.GetCommandLineArgs().Skip(1)).ToList();
        lock (Gate)
        {
            uris.AddRange(Early);
            Early.Clear();
            _queue = DispatcherQueue.GetForCurrentThread();
            _window = new MainWindow(uris);
        }
        _window.Activate();
    }

    /// <summary>
    /// Another Drift was started and handed its arguments over (Program.Main): open its links here.
    /// Raised on a background thread.
    /// </summary>
    internal static void OnRedirected(AppActivationArguments activation)
    {
        var uris = CommandLine.UrisIn(activation);
        lock (Gate)
        {
            if (_window is null || _queue is null)
            {
                Early.AddRange(uris);
                return;
            }
            var window = _window;
            _queue.TryEnqueue(() => window.OpenFromLaunch(uris));
        }
    }
}
