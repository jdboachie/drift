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
/// add another beside it (while there's room), put another page in its place, swap sides (with more
/// than two, move it left or right), show it alone, close it. Drift's own icons, the ones
/// its menus had. It goes when the pointer has left both the dots and the pill, or a button's been used.
/// <para>It's drawn in Drift's window, a solid capsule (PageControlsPillStyle). Real
/// acrylic would need a window of its own (a Flyout with a SystemBackdrop), and Windows rounds a
/// window's corners by 8px at most, so it couldn't be a pill; acrylic drawn in the window can't see
/// the web page under it, so it'd be flat anyway.</para>
/// </summary>
public sealed partial class MainWindow
{
    // A beat before it goes once the pointer's off the dots and the pill (either one, with a few pixels
    // round it, keeps it, so crossing between them never trips this).
    private const int PageControlsGraceMs = 150;
    // In and out: going is the coming in reverse, quicker (as the dots' growing, SplitHandles.cs).
    private static readonly TimeSpan PillFadeIn = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan PillFadeOut = TimeSpan.FromMilliseconds(80);
    private const double PillScaleIn = 0.2;
    private const double PillScaleOut = 0.13;
    private static readonly Vector3 PillSmall = new(0.95f, 0.95f, 1);

    private Border? _pageControls;

    private bool PageControlsShown => _pageControls is { Opacity: > 0 };
    private Button? _controlsFor;
    private bool _overControls;
    private DispatcherQueueTimer? _controlsHide;

    private void ShowPageControls(Button handle)
    {
        if (PageOf(handle) is not { } page || IsPeekShown || _handleDragging) return;
        _controlsHide?.Stop();
        var pill = _pageControls ??= NewPageControls();
        pill.Visibility = Visibility.Visible;
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
            }
        }
        _controlsFor = handle;
        if (pill.Opacity < 1)
        {
            // From where it is: caught on its way out, it comes back from there.
            if (_uiSettings.AnimationsEnabled) Spring.To(pill, "Scale", Vector3.One, duration: PillScaleIn, bounce: 0);
            else Spring.Jump(pill, "Scale", Vector3.One);
            pill.OpacityTransition!.Duration = PillFadeIn;
        }
        pill.Opacity = 1;
        pill.IsHitTestVisible = true;
        _menuHandle = handle;
        ShowPageFocus();
        DispatcherQueue.TryEnqueue(UpdateDragRegions);
    }

    private void HidePageControls()
    {
        _controlsHide?.Stop();
        if (_pageControls is not { Opacity: > 0 } pill) return;
        // Back the way it came: down to where it grew from as it fades, quicker.
        if (_uiSettings.AnimationsEnabled) Spring.To(pill, "Scale", PillSmall, duration: PillScaleOut, bounce: 0);
        else Spring.Jump(pill, "Scale", PillSmall);
        pill.OpacityTransition!.Duration = PillFadeOut;
        pill.Opacity = 0;
        pill.IsHitTestVisible = false;
        _overControls = false;
        CollapsePillSoon(pill);
        if (_menuHandle == _controlsFor) _menuHandle = null;
        _controlsFor = null;
        ShowPageFocus();
        DispatcherQueue.TryEnqueue(UpdateDragRegions);
    }

    /// <summary>
    /// Faded out, it's taken out of the tree: see-through, its buttons could still be reached with Tab or
    /// a screen reader, and would act on the page they were made for.
    /// </summary>
    private async void CollapsePillSoon(Border pill)
    {
        await System.Threading.Tasks.Task.Delay(160);
        if (pill.Opacity == 0) pill.Visibility = Visibility.Collapsed;
    }

    /// <summary>Soon, unless the pointer comes back to the dots or the pill.</summary>
    private void HidePageControlsSoon()
    {
        if (!PageControlsShown) return;
        if (_controlsHide is null)
        {
            _controlsHide = DispatcherQueue.CreateTimer();
            _controlsHide.Interval = TimeSpan.FromMilliseconds(PageControlsGraceMs);
            _controlsHide.IsRepeating = false;
            _controlsHide.Tick += (_, _) =>
            {
                if (_overControls || (_controlsFor is { } handle && (handle.IsPointerOver || PointerOn(handle)))) return;
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
        if (!PageControlsShown || _pageControls is not { } pill || _controlsFor is not { } handle) return;
        // Within reach of the dots (the reach that opened it, PointerOn), or on the pill.
        if (pointer is var (x, y) && (Near(BoundsOf(pill), x, y) || PointerOn(handle))) return;
        HidePageControlsSoon();

        static bool Near(Windows.Foundation.Rect at, double x, double y) =>
            x >= at.X - 6 && x < at.X + at.Width + 6 && y >= at.Y - 6 && y < at.Y + at.Height + 6;
    }

    private Border NewPageControls()
    {
        var pill = new Border
        {
            Style = (Style)Application.Current.Resources["PageControlsPillStyle"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Opacity = 0,
            IsHitTestVisible = false,
            OpacityTransition = new ScalarTransition { Duration = PillFadeIn },
            // Where it grows from, the first time too.
            Scale = PillSmall,
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
            yield return ControlButton("Add split view", AddSplitGlyph(), () => OpenSplitCommandBar(page));
            yield break;
        }
        // Another page to its right, while there's room for one (up to four): first, as on a page on its own.
        if (!split.IsFull) yield return ControlButton("Add split view", AddSplitGlyph(), () => OpenSplitCommandBar(page));
        var at = split.IndexOf(page);
        yield return ControlButton("Replace page", Glyph("\uE70F"), () =>
        {
            if (_active != page) Activate(page);
            OpenCommandBar(newTab: false, animate: true);
        });
        // Two pages swap; with more, a page moves one place over (or is dragged there by its dots).
        if (split.Count == 2) yield return ControlButton("Swap sides", Glyph("\uE8AB"), () => MovePage(split, at, 1 - at, _ => 0, 0));
        else
        {
            // An arrow inside a frame: the page moves within the split (not back and forward, which are the page's history).
            if (at > 0) yield return ControlButton("Move left", Glyph("\uE8A0"), () => MovePage(split, at, at - 1, _ => 0, 0));
            if (at < split.Count - 1) yield return ControlButton("Move right", Glyph("\uE89F"), () => MovePage(split, at, at + 1, _ => 0, 0));
        }
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

    /// <summary>
    /// Segoe has no "add a page beside". Move left and Move right are a page with a side pane (E90D's
    /// frame) and an arrow in the page: this is that frame with a + where their arrow is, as big, so the
    /// three read alike.
    /// </summary>
    private static FrameworkElement AddSplitGlyph()
    {
        // Drawn, not the + glyph shrunk to fit: that came out a faint hairline beside the arrows' full
        // 1px strokes. Two whole-pixel bars, centred where the move icons' arrows are.
        var ink = (Brush)Application.Current.Resources["DriftIconBrush"];
        Microsoft.UI.Xaml.Shapes.Rectangle Bar(double left, double top, double width, double height) => new()
        {
            Width = width,
            Height = height,
            Fill = ink,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(left, top, 0, 0),
        };
        return new Grid
        {
            Width = 16,
            Height = 16,
            UseLayoutRounding = true,
            Children = { Glyph("\uE90D"), Bar(3, 7, 5, 1), Bar(5, 5, 1, 5) },
        };
    }

    private static FontIcon Glyph(string glyph) => new()
    {
        Glyph = glyph,
        FontSize = 14,
        Foreground = (Brush)Application.Current.Resources["DriftIconBrush"],
    };
}
