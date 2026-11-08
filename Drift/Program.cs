using System;
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
        WinRT.ComWrappersSupport.InitializeComWrappers();

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

        Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
        return 0;
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
