using System;
using System.Collections.Generic;
using System.Numerics;
using Drift.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Drift;

/// <summary>
/// What you can do with a page, as Arc shows it: hover the dots on top of a page (SplitHandles.cs) and
/// a pill of round buttons drops down under them. A page on its own: add a split. A page in a split:
/// put another page in its place, swap sides, show it alone, close it. Drift's own icons, the ones
/// its menus had. It goes when the pointer has left both the dots and the pill, or a button's been used.
/// </summary>
public sealed partial class MainWindow
{
    // Long enough to cross from the dots to the pill, or back, without it going.
    private const int PageControlsGraceMs = 300;

    private Border? _pageControls;
    private Button? _controlsFor;
    private bool _overControls;
    private DispatcherQueueTimer? _controlsHide;

    private void ShowPageControls(Button handle)
    {
        if (PageOf(handle) is not { } page || IsPeekShown || _handleDragging) return;
        _controlsHide?.Stop();
        var pill = _pageControls ??= NewPageControls();
        if (_controlsFor != handle || pill.Opacity == 0)
        {
            var row = (StackPanel)pill.Child;
            row.Children.Clear();
            foreach (var button in PageControlButtons(page)) row.Children.Add(button);
            Grid.SetColumn(pill, Grid.GetColumn(handle));
            // Just under the dots, over the top of the page.
            pill.Margin = new Thickness(0, handle.Margin.Top + HandleDrop + handle.Height + 4, 0, 0);
            if (pill.Opacity == 0)
            {
                pill.UpdateLayout();
                pill.CenterPoint = new Vector3((float)pill.ActualWidth / 2, 0, 0);
                if (_uiSettings.AnimationsEnabled)
                    Spring.To(pill, "Scale", Vector3.One, duration: 0.2, bounce: 0, from: new Vector3(0.95f, 0.95f, 1));
            }
        }
        _controlsFor = handle;
        _menuHandle = handle;
        pill.Opacity = 1;
        pill.IsHitTestVisible = true;
        ShowPageFocus();
        DispatcherQueue.TryEnqueue(UpdateDragRegions);
    }

    private void HidePageControls()
    {
        _controlsHide?.Stop();
        if (_pageControls is not { } pill || pill.Opacity == 0) return;
        pill.Opacity = 0;
        pill.IsHitTestVisible = false;
        _overControls = false;
        if (_menuHandle == _controlsFor) _menuHandle = null;
        _controlsFor = null;
        ShowPageFocus();
        DispatcherQueue.TryEnqueue(UpdateDragRegions);
    }

    /// <summary>Soon, unless the pointer comes back to the dots or the pill.</summary>
    private void HidePageControlsSoon()
    {
        if (_pageControls is not { Opacity: > 0 }) return;
        if (_controlsHide is null)
        {
            _controlsHide = DispatcherQueue.CreateTimer();
            _controlsHide.Interval = TimeSpan.FromMilliseconds(PageControlsGraceMs);
            _controlsHide.IsRepeating = false;
            _controlsHide.Tick += (_, _) =>
            {
                if (_overControls || (_controlsFor is { } handle && handle.IsPointerOver)) return;
                // Opened from the keyboard, it stays while the dots or a button have focus.
                if (_controlsFor?.FocusState is FocusState.Keyboard) return;
                HidePageControls();
            };
        }
        _controlsHide.Stop();
        _controlsHide.Start();
    }

    /// <summary>The pointer's somewhere over the window (CaptionBar.cs asks): off the dots and the pill, it goes.</summary>
    private void PageControlsPointerAt((double X, double Y)? pointer)
    {
        if (_pageControls is not { Opacity: > 0 } pill || _controlsFor is not { } handle) return;
        if (pointer is var (x, y) && (Near(BoundsOf(pill), x, y) || Near(BoundsOf(handle), x, y))) return;
        HidePageControlsSoon();

        static bool Near(Windows.Foundation.Rect at, double x, double y) =>
            x >= at.X - 6 && x < at.X + at.Width + 6 && y >= at.Y - 6 && y < at.Y + at.Height + 6;
    }

    private Border NewPageControls()
    {
        var pill = new Border
        {
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(4),
            BorderThickness = new Thickness(1),
            Background = (Brush)Application.Current.Resources["AcrylicInAppFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["DriftFolderCardBorderBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Opacity = 0,
            IsHitTestVisible = false,
            OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(120) },
            Shadow = new ThemeShadow(),
            Translation = new Vector3(0, 0, 16),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 },
        };
        Canvas.SetZIndex(pill, 3);
        pill.PointerEntered += (_, _) =>
        {
            _overControls = true;
            _controlsHide?.Stop();
        };
        pill.PointerExited += (_, _) =>
        {
            _overControls = false;
            HidePageControlsSoon();
        };
        PageColumns.Children.Add(pill);
        return pill;
    }

    private IEnumerable<Button> PageControlButtons(BrowserTab page)
    {
        if (page.Split is not { } split)
        {
            yield return ControlButton("Add split view", Glyph("\uF57C"), () => OpenSplitCommandBar(page));
            yield break;
        }
        var left = split.Left == page;
        yield return ControlButton("Replace page", Glyph("\uE70F"), () =>
        {
            if (_active != page) Activate(page);
            OpenCommandBar(newTab: false, animate: true);
        });
        yield return ControlButton("Swap sides", Glyph("\uE8AB"), () => SwapPages(split, left, 0, 0, 0));
        yield return ControlButton("Show alone", Glyph("\uE740"), () => ShowPageAlone(page));
        yield return ControlButton("Close page", Glyph("\uE711"), () => CloseSplitPage(page));
    }

    private Button ControlButton(string name, FrameworkElement icon, Action act)
    {
        var button = new Button
        {
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(0),
            Content = icon,
        };
        var hover = (Brush)Application.Current.Resources["DriftHoverBrush"];
        var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        button.Resources["ButtonBackground"] = clear;
        button.Resources["ButtonBackgroundPointerOver"] = hover;
        button.Resources["ButtonBackgroundPressed"] = hover;
        button.Resources["ButtonBorderBrush"] = clear;
        button.Resources["ButtonBorderBrushPointerOver"] = clear;
        button.Resources["ButtonBorderBrushPressed"] = clear;
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, new ToolTip { Content = name });
        ToolTipService.SetPlacement(button, PlacementMode.Bottom);
        button.Click += (_, _) =>
        {
            HidePageControls();
            act();
        };
        return button;
    }

    private static FontIcon Glyph(string glyph) => new()
    {
        Glyph = glyph,
        FontSize = 14,
        Foreground = (Brush)Application.Current.Resources["DriftIconBrush"],
    };
}
