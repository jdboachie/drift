using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace Drift;

/// <summary>
/// DRIFT_STARTUP_TRACE=1: when each step of starting happened, in milliseconds since the process
/// started, up to the first page shown, appended to %LOCALAPPDATA%\Drift\startup.log (written on
/// another thread, so tracing hardly slows what it measures). Off, each step costs one check.
/// </summary>
internal static class StartupTrace
{
    private static readonly bool On = Environment.GetEnvironmentVariable("DRIFT_STARTUP_TRACE") == "1";
    private static readonly ConcurrentQueue<string> Lines = new();
    private static readonly object Writing = new();
    private static DateTime _start;
    private static bool _done, _headed;

    public static void Mark(string step)
    {
        if (!On || _done) return;
        if (_start == default) _start = Process.GetCurrentProcess().StartTime.ToUniversalTime();
        Lines.Enqueue($"{(DateTime.UtcNow - _start).TotalMilliseconds,6:0} {step}");
        ThreadPool.QueueUserWorkItem(_ => Write());
    }

    /// <summary>The last step: the first page is showing.</summary>
    public static void Done(string step)
    {
        Mark(step);
        _done = true;
    }

    /// <summary>Notes when the window's first frame is drawn.</summary>
    public static void MarkFirstFrame()
    {
        if (!On) return;
        void Rendered(object? sender, object e)
        {
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= Rendered;
            Mark("first frame");
        }
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += Rendered;
    }

    private static void Write()
    {
        lock (Writing)
        {
            var text = new StringBuilder();
            if (!_headed) text.AppendLine($"--- {DateTime.Now:O}");
            var any = false;
            while (Lines.TryDequeue(out var line))
            {
                text.AppendLine(line);
                any = true;
            }
            if (!any) return;
            try
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Drift");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "startup.log"), text.ToString());
                _headed = true;
            }
            catch
            {
                // Nowhere to write it.
            }
        }
    }
}
