using Drift.Models;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Drift;

/// <summary>A tab's row pressed: a plain press switches to it at once (Core/TabPress.cs).</summary>
public sealed partial class MainWindow
{
    /// <summary>The tab in front before a press switched away from it: a drag from that press puts it back.</summary>
    private BrowserTab? _pressSwitchedFrom;

    private void Row_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _pressSwitchedFrom = null;
        if (sender is not SelectorItem { Content: BrowserTab tab } row) return;
        var point = e.GetCurrentPoint(row).Properties;
        var keys = e.KeyModifiers;
        var (onControl, half) = WhatsPressed(e.OriginalSource as DependencyObject, row);
        if (!TabPress.SwitchesOnPress(
                mouse: e.Pointer.PointerDeviceType == PointerDeviceType.Mouse,
                leftButton: point.IsLeftButtonPressed && !point.IsRightButtonPressed && !point.IsMiddleButtonPressed,
                ctrl: keys.HasFlag(VirtualKeyModifiers.Control),
                shift: keys.HasFlag(VirtualKeyModifiers.Shift),
                onControl: onControl))
            return;
        // A split's row: the page pressed in it (SplitHalf_Tapped did this on release); the row itself, its page in front.
        var target = half ?? tab;
        if (target is { IsSplitRow: true, Split: { } split }) target = FrontOf(split);
        if (target == _active) return;
        // Plain, so any tabs picked out are let go, as a plain click's selection does (Picks.cs).
        ClearPicks();
        _pressSwitchedFrom = _active;
        Activate(target);
    }

    /// <summary>Whether the press is on a button or a text box of the row's own, and which page of a split's row it's on.</summary>
    private static (bool OnControl, BrowserTab? Half) WhatsPressed(DependencyObject? at, DependencyObject row)
    {
        BrowserTab? half = null;
        for (; at is not null && at != row; at = VisualTreeHelper.GetParent(at))
        {
            if (at is ButtonBase or TextBox) return (true, null);
            if (at is SplitPageView { Tag: BrowserTab page }) half ??= page;
        }
        return (false, half);
    }
}
