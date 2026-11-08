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
        UnhandledException += (_, e) => LogCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject as Exception);
    }

    internal static void LogCrash(Exception? error)
    {
        try
        {
            var folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Drift");
            System.IO.Directory.CreateDirectory(folder);
            System.IO.File.AppendAllText(System.IO.Path.Combine(folder, "crash.log"),
                $"{DateTime.Now:O}{Environment.NewLine}{error}{Environment.NewLine}{Environment.NewLine}");
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
