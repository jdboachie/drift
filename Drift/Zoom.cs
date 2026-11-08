using System;
using System.Linq;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Drift.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Drift;

/// <summary>
/// Zoom. Zooming itself is Chromium's own: Ctrl+plus, Ctrl+minus and Ctrl+0 on a page, and
/// Ctrl+wheel. WinUI's WebView2 neither reports it nor lets the app set it, so each page reports
/// its pixel ratio as it loads and as it changes (InputScript), and PageZoom turns that into the
/// level. While the tab in front isn't at 100%, the level shows at the end of the address pill;
/// clicking it puts the page back to 100%, by pressing Ctrl+0 in it.
/// </summary>
public sealed partial class MainWindow
{
    // Peek's page has a zoom of its own, like a tab's (carried into the tab it becomes).
    private double _peekZoom = 1;

    /// <param name="source">The tab the page is in; null for Peek's page.</param>
    /// <param name="args">The page's pixel ratio.</param>
    private void OnZoomMessage(BrowserTab? source, string args)
    {
        if (!double.TryParse(args, NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio)) return;
        var zoom = PageZoom.Of(ratio, Root.XamlRoot?.RasterizationScale ?? 1);
        if (source is null)
        {
            _peekZoom = zoom;
            return;
        }
        source.Zoom = zoom;
        if (source == _active) ShowZoomChip();
    }

    /// <summary>The level at the pill's end, for the tab in front, unless it's at 100%.</summary>
    private void ShowZoomChip()
    {
        var zoom = _active?.Zoom ?? 1;
        if (!PageZoom.IsZoomed(zoom))
        {
            ZoomChip.Visibility = Visibility.Collapsed;
            return;
        }
        var label = PageZoom.Label(zoom);
        ZoomChipText.Text = label;
        ToolTipService.SetToolTip(ZoomChip, $"Zoom {label}: click to reset to 100%");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ZoomChip, $"Zoom {label}, reset to 100%");
        ZoomChip.Visibility = Visibility.Visible;
    }

    private void ZoomChip_Click(object sender, RoutedEventArgs e) => ResetZoom();

    private const ushort Vk0 = 0x30, VkControl = 0x11, VkF = 0x46, VkPlus = 0xBB, VkMinus = 0xBD;

    /// <summary>Back to 100%: Ctrl+0 in the page (SendPageKeys).</summary>
    private void ResetZoom() => SendPageKeys(VkControl, Vk0);

    /// <summary>
    /// Keys pressed in the tab's page, the way the keyboard does it (held in order, let go in reverse):
    /// focus into the page, then the keys, so it's Chromium's own zoom or find bar, and the page reports
    /// a new zoom level as usual. Only while Drift is the window in front, so the keys can't land in
    /// another app.
    /// </summary>
    private async void SendPageKeys(params ushort[] keys)
    {
        if (_active?.View is not { CoreWebView2: not null } view) return;
        var window = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (GetForegroundWindow() != window) return;
        view.Focus(FocusState.Programmatic);
        // Focus reaches the engine a moment later; keys sent before that go to the button.
        await Task.Delay(40);
        if (GetForegroundWindow() != window || _active?.View != view) return;
        var inputs = keys.Select(k => Key(k, up: false)).Concat(keys.Reverse().Select(k => Key(k, up: true))).ToArray();
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    private static Input Key(ushort key, bool up) => new()
    {
        Type = 1, // keyboard
        Union = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = key, Flags = up ? 2u : 0u } },
    };

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    // Sized by its largest member, the mouse input, as SendInput expects.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X, Y;
        public uint Data, Flags, Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey, ScanCode;
        public uint Flags, Time;
        public IntPtr ExtraInfo;
    }
}
