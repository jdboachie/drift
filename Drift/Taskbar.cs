using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Drift;

/// <summary>
/// Keeps floating players off the taskbar: Drift's own (PlayerWindow) and Chromium's video window,
/// which belongs to WebView2's browser process and would otherwise get a taskbar button of its own.
/// </summary>
internal static class Taskbar
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000;

    /// <summary>
    /// A tool window never gets a taskbar button or an Alt+Tab entry; one already showing also has
    /// its button taken away now, since Windows only reads the style when a window appears.
    /// </summary>
    public static void Hide(IntPtr hwnd)
    {
        try
        {
            var style = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            if ((style & WS_EX_TOOLWINDOW) == 0 || (style & WS_EX_APPWINDOW) != 0)
                SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)((style | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW));
            var list = (ITaskbarList)new TaskbarList();
            list.HrInit();
            list.DeleteTab(hwnd);
        }
        catch
        {
            // It keeps its button, then.
        }
    }

    /// <summary>A video just floated: Chromium's window shows up a moment later, so it's looked for a few times.</summary>
    public static async void HideBrowserPlayer(uint browserProcessId)
    {
        foreach (var wait in new[] { 0, 250, 750, 2000 })
        {
            await Task.Delay(wait);
            foreach (var hwnd in WindowsOf(browserProcessId))
            {
                var style = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
                if (BrowserPip.IsPlayer(IsWindowVisible(hwnd), (style & WS_EX_TOPMOST) != 0)) Hide(hwnd);
            }
        }
    }

    private static List<IntPtr> WindowsOf(uint processId)
    {
        var found = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var owner);
            if (owner == processId) found.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private delegate bool EnumProc(IntPtr hwnd, IntPtr param);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr param);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    private class TaskbarList { }

    [ComImport, Guid("56FDF342-FD6D-11d0-958A-006097C9A090"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
    }
}
