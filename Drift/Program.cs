using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Drift;

/// <summary>
/// Drift's entry point (in place of the one XAML generates; see DISABLE_XAML_GENERATED_MAIN).
/// Only one Drift runs: started again (a link opened from another app, now that Drift can be the
/// default browser, or its icon clicked), the new process hands its arguments to the running one,
/// which opens them, and quits.
/// </summary>
public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        StartupTrace.Mark("main");
        WinRT.ComWrappersSupport.InitializeComWrappers();
        WaitForRestart(args);

        var main = AppInstance.FindOrRegisterForKey("Drift");
        if (!main.IsCurrent)
        {
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            // So the running Drift may come to the front when it opens the link.
            AllowSetForegroundWindow(main.ProcessId);
            // Off this STA thread, which has no message loop to wait on.
            Task.Run(() => main.RedirectActivationToAsync(activation).AsTask()).Wait();
            return 0;
        }
        main.Activated += (_, activation) => App.OnRedirected(activation);

        // What the window starts from, read on other threads while XAML starts up: on the UI thread, the
        // three files held the window's first frame up. (After WaitForRestart: a Drift restarting saves
        // its tabs on its way out.)
        _ = Task.Run(() => Settings.Current);
        History.ReadSoon();
        Session.ReadSoon();

        Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
        return 0;
    }

    /// <summary>The argument a restarting Drift starts its successor with (MicrosoftSignIn.cs).</summary>
    internal const string RestartArgument = "--restart-after=";

    /// <summary>
    /// Started by a Drift restarting: waits for it, and its web engine, to be gone first. Otherwise
    /// this one would hand itself to the old one and quit, and a new engine with different options
    /// can't open pages on a profile the old one still has.
    /// </summary>
    private static void WaitForRestart(string[] args)
    {
        var after = args.FirstOrDefault(a => a.StartsWith(RestartArgument, StringComparison.Ordinal));
        if (after is null) return;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        foreach (var id in after[RestartArgument.Length..].Split(','))
        {
            if (!int.TryParse(id, out var pid)) continue;
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                var left = deadline - DateTime.UtcNow;
                if (left > TimeSpan.Zero) process.WaitForExit(left);
            }
            catch (ArgumentException)
            {
                // Gone already.
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
