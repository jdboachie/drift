using System;
using System.Numerics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace Drift;

/// <summary>
/// The window buttons (minimize, maximize, close), as in Zen: hidden until the pointer rests at the
/// top of the page area, then down from the window's top-right corner, where Windows keeps them.
/// When they come and go is CaptionReveal's (tested); this is the window's side of it.
/// </summary>
public sealed partial class MainWindow
{
    // Three 46×32 buttons, as Windows has them, and the card's 1px left border.
    private const double CaptionBarWidth = 3 * 46 + 1;
    private const double CaptionBarHeight = 32;
    // Up out of sight, shadow and all; at rest, raised for its shadow.
    private static readonly Vector3 CaptionBarAway = new(0, -(float)CaptionBarHeight - 24, 32);
    private static readonly Vector3 CaptionBarHome = new(0, 0, 32);

    private readonly CaptionReveal _captionReveal = new();
    private DispatcherQueueTimer? _captionWatch;
    private DispatcherQueueTimer? _captionCollapse;

    /// <summary>
    /// Asks Windows where the pointer is every 25ms, for as long as the window's open. Pointer
    /// events can't do it: the window's top margin is its drag area, which XAML gets no events
    /// from, and the page's web view takes the pointer over the page.
    /// </summary>
    private void StartCaptionWatch()
    {
        Spring.Jump(CaptionBar, "Translation", CaptionBarAway);
        _captionWatch = DispatcherQueue.CreateTimer();
        _captionWatch.Interval = TimeSpan.FromMilliseconds(25);
        _captionWatch.Tick += (_, _) => CaptionWatch_Tick();
        _captionWatch.Start();
        _captionCollapse = DispatcherQueue.CreateTimer();
        _captionCollapse.Interval = TimeSpan.FromMilliseconds(220);
        _captionCollapse.IsRepeating = false;
        _captionCollapse.Tick += (_, _) =>
        {
            if (!_captionReveal.Shown) CaptionBar.Visibility = Visibility.Collapsed;
        };
        Closed += (_, _) => _captionWatch.Stop();
    }

    private void CaptionWatch_Tick()
    {
        if (Root.XamlRoot is null) return;
        var layout = new CaptionLayout(Root.ActualWidth, SidebarColumn.ActualWidth + PageArea.Margin.Left,
            PageArea.Margin.Top, CaptionBarWidth, CaptionBarHeight);
        var pointer = PointerOverWindow();
        // The split page under the pointer shows its handle (SplitHandles.cs).
        PagePointerAt(pointer);
        var at = layout.Locate(pointer is { } p ? (p.X, p.Y) : null, _captionReveal.Shown);
        // Over Peek's grabber, the pointer's there for the grabber.
        if (pointer is { } q && PeekGrabberArea is { } grabber
            && q.X >= grabber.X && q.X < grabber.Right && q.Y < grabber.Bottom) at = PointerAt.Away;
        // Nor over a split page's handle.
        if (pointer is { } h)
            foreach (var handle in PageHandleAreas())
                if (h.X >= handle.X && h.X < handle.Right && h.Y >= handle.Y && h.Y < handle.Bottom) at = PointerAt.Away;
        var buttonDown = IsKeyDown(0x01) || IsKeyDown(0x02);
        var blocked = _commandBarOpen || AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;

        switch (_captionReveal.Update(Environment.TickCount64, at, buttonDown, blocked))
        {
            case CaptionChange.Show: ShowCaptionBar(); break;
            case CaptionChange.Hide: HideCaptionBar(); break;
        }
    }

    /// <summary>
    /// Down from the top edge on a spring with no bounce (an overshoot would open a gap above it),
    /// pushed off so it moves at once, like an ease-out: about 250ms.
    /// </summary>
    private void ShowCaptionBar()
    {
        _captionCollapse?.Stop();
        var maximized = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
        CaptionMaximizeGlyph.Glyph = maximized ? "" : "";
        if (CaptionMaximizeGlyph.Parent is FrameworkElement button)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, maximized ? "Restore" : "Maximize");
        if (!_uiSettings.AnimationsEnabled)
        {
            // Windows' animation effects are off: a fade where it stands, no slide.
            Spring.Jump(CaptionBar, "Translation", CaptionBarHome);
            if (CaptionBar.Visibility == Visibility.Collapsed) CaptionBar.Opacity = 0;
            CaptionBar.Visibility = Visibility.Visible;
            Ui.Fade(CaptionBar, 1);
        }
        else if (CaptionBar.Visibility == Visibility.Collapsed)
        {
            CaptionBar.Visibility = Visibility.Visible;
            Spring.To(CaptionBar, "Translation", CaptionBarHome, duration: 0.25, bounce: 0, from: CaptionBarAway, kick: 0.5);
        }
        // Back while it was still going: the same spring turns round from wherever it got to.
        else Spring.To(CaptionBar, "Translation", CaptionBarHome, duration: 0.25, bounce: 0);
        UpdateDragRegions();
    }

    /// <summary>Back up, quicker than it came; collapsed once it's out of sight.</summary>
    private void HideCaptionBar()
    {
        UpdateDragRegions();
        if (!_uiSettings.AnimationsEnabled)
        {
            Ui.Fade(CaptionBar, 0, () =>
            {
                if (_captionReveal.Shown) return;
                CaptionBar.Visibility = Visibility.Collapsed;
                CaptionBar.Opacity = 1;
                Spring.Jump(CaptionBar, "Translation", CaptionBarAway);
            });
            return;
        }
        Spring.To(CaptionBar, "Translation", CaptionBarAway, duration: 0.2, bounce: 0);
        _captionCollapse?.Start();
    }

    /// <summary>The bar's corner, while it's down: cut out of the drag area so its buttons get clicks.</summary>
    private Area? CaptionBarArea =>
        _captionReveal.Shown ? new Area(Root.ActualWidth - CaptionBarWidth, 0, CaptionBarWidth, CaptionBarHeight) : null;
}
