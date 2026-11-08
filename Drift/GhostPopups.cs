using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Drift;

/// <summary>
/// Keeps WinUI's menus as their own little windows (with the system's frosted look) without the
/// "ghost" they can leave. If the window is minimized while a menu is open, or still closing (its
/// own Minimize item, Win+Down, the taskbar), Windows remembers the menu's window as shown and shows
/// it again on restore, empty and invisible but still taking every click and hover where the menu
/// was: the top of the sidebar went dead. (microsoft/microsoft-ui-xaml#11068, open.) So:
/// <list type="bullet">
/// <item>menus close before the window minimizes (the window's minimize message is caught first), and</item>
/// <item>once it's restored, any menu window still showing with no menu open is hidden.</item>
/// </list>
/// </summary>
internal sealed class GhostPopups
{
    private const uint WM_SYSCOMMAND = 0x0112;
    private const nuint SC_MINIMIZE = 0xF020;
    private const string PopupWindowClass = "Microsoft.UI.Content.PopupWindowSiteBridge";

    private readonly Window _window;
    private readonly IntPtr _hwnd;
    private readonly SubclassProc _proc; // kept alive as long as the window is subclassed
    private bool _minimized;

    public GhostPopups(Window window)
    {
        _window = window;
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _proc = WindowProc;
        SetWindowSubclass(_hwnd, _proc, 1, 0);
        window.AppWindow.Changed += (sender, _) =>
        {
            var minimized = sender.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized };
            if (_minimized && !minimized) LookForGhosts();
            _minimized = minimized;
        };
        // And whenever the window comes to the front: a ghost left some other way is gone by the time you point at it.
        window.Activated += (_, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated) HideGhosts();
        };
        window.Closed += (_, _) => RemoveWindowSubclass(_hwnd, _proc, 1);
    }

    /// <summary>Minimizes the way the system does (Drift's menu item), so menus get closed first here too.</summary>
    public void Minimize() => PostMessage(_hwnd, WM_SYSCOMMAND, SC_MINIMIZE, 0);

    private nint WindowProc(IntPtr hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == WM_SYSCOMMAND && (wParam & 0xFFF0) == SC_MINIMIZE) CloseMenus();
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    /// <summary>Open menus and flyouts close (not dialogs, which live in popups too).</summary>
    private void CloseMenus()
    {
        if (_window.Content?.XamlRoot is not { } root) return;
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
            if (popup.Child is MenuFlyoutPresenter or FlyoutPresenter) popup.IsOpen = false;
    }

    /// <summary>
    /// After a restore, a few looks over the next few seconds: Windows puts the remembered windows
    /// back as part of restoring, and a menu that was open when the window was minimized from
    /// outside (no minimize message to catch) only finishes closing a moment later.
    /// </summary>
    private void LookForGhosts()
    {
        foreach (var old in _timers) old.Stop();
        _timers.Clear();
        HideGhosts();
        foreach (var ms in new[] { 150, 500, 1200, 2500, 5000 })
        {
            var timer = _window.DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(ms);
            timer.IsRepeating = false;
            timer.Tick += (t, _) => { t.Stop(); HideGhosts(); };
            timer.Start();
            _timers.Add(timer); // kept alive until they've run
        }
    }

    private readonly System.Collections.Generic.List<Microsoft.UI.Dispatching.DispatcherQueueTimer> _timers = [];

    /// <summary>With no menu open, a menu window that's showing is a ghost: hidden.</summary>
    private void HideGhosts()
    {
        if (_window.Content?.XamlRoot is not { } root || VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Count > 0) return;
        GetWindowThreadProcessId(_hwnd, out var process);
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var owner);
            if (owner == process && GetWindow(hwnd, 4 /* GW_OWNER */) == _hwnd && IsWindowVisible(hwnd) && IsMenuWindow(hwnd))
                ShowWindow(hwnd, 0 /* SW_HIDE */);
            return true;
        }, 0);
    }

    // A menu's window: WinUI's popup window class, titled "PopupHost".
    private static bool IsMenuWindow(IntPtr hwnd)
    {
        var text = new StringBuilder(128);
        GetClassName(hwnd, text, text.Capacity);
        if (text.ToString() == PopupWindowClass) return true;
        text.Clear();
        GetWindowText(hwnd, text, text.Capacity);
        return text.ToString() == "PopupHost";
    }

    private delegate nint SubclassProc(IntPtr hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    private delegate bool EnumWindowsProc(IntPtr hwnd, nint data);

    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc proc, nuint id, nuint data);

    [DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc proc, nuint id);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(IntPtr hwnd, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc proc, nint data);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int size);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint relation);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);
}
