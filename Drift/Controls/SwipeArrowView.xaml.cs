using System;
using System.Numerics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace Drift;

/// <summary>
/// The arrow a two-finger swipe brings in from a page's edge (SwipeArrowView.xaml, Swipe.cs): it follows the swipe,
/// then either bursts and fades as the page goes back or forward (Chromium's 200ms), or springs back out from
/// wherever it is, carrying on at the fingers' speed. A new swipe catches it on its way out. With Windows'
/// animation effects off, it fades instead of moving.
/// </summary>
public sealed partial class SwipeArrowView : UserControl
{
    /// <summary>The arrow's room: the ring's widest.</summary>
    private const double Room = 96;

    /// <summary>The ring at rest: the disc's own size.</summary>
    private static readonly Vector3 RingSmall = new(0.5f, 0.5f, 1);

    private static readonly UISettings Settings = new();

    private int _direction = -1;
    private DispatcherQueueTimer? _gone;

    public SwipeArrowView() => InitializeComponent();

    private void Stage_SizeChanged(object sender, SizeChangedEventArgs e) =>
        Stage.Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };

    /// <summary>
    /// At the left edge for back (-1), the right for forward (1), as far in as the swipe has brought it, and filled
    /// with the accent while letting go would go back or forward.
    /// </summary>
    /// <param name="closes">Going back closes the tab (its first page, opened by a link): a cross, not an arrow.</param>
    public void Follow(int direction, double progress, bool ready, bool closes = false)
    {
        _gone?.Stop();
        Ui.StopFade(Bubble, 1);
        if (direction != _direction)
        {
            _direction = direction;
            Bubble.HorizontalAlignment = direction < 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        }
        Arrow.Glyph = closes ? "" : direction < 0 ? "" : "";
        Visibility = Visibility.Visible;
        Spring.Jump(Bubble, "Translation", new Vector3((float)At(SwipeNav.ArrowOffset(progress)), 0, 0));
        // The ring: the disc's size (20 across the middle) at the start, twice that where it's all the way in.
        var ring = (float)(0.5 + 0.5 * Math.Clamp(progress, 0, 1));
        Spring.Jump(Ripple, "Scale", new Vector3(ring, ring, 1));
        VisualStateManager.GoToState(this, ready ? "Ready" : "Waiting", false);
    }

    /// <summary>The page is going back or forward: the ring bursts to 48 across the middle as the arrow fades.</summary>
    public void GoThrough()
    {
        if (Visibility != Visibility.Visible) return;
        _gone?.Stop();
        Spring.To(Ripple, "Scale", new Vector3(1.2f, 1.2f, 1), duration: 0.2, bounce: 0);
        Ui.Fade(Bubble, 0, Hide, milliseconds: 200);
    }

    /// <summary>
    /// Let go short of going through: it springs back out from where it is, at the arrow's speed when let go
    /// (DIPs a second, more than 0 coming in), if that's towards the edge.
    /// </summary>
    public void Retract(double arrowSpeed)
    {
        if (Visibility != Visibility.Visible) return;
        if (!Settings.AnimationsEnabled)
        {
            Ui.Fade(Bubble, 0, Hide);
            return;
        }
        var now = Bubble.Translation;
        var hidden = (float)At(0);
        // Coming in is towards the page's middle: right for back, left for forward.
        var speed = arrowSpeed * -_direction;
        var towards = hidden - now.X;
        var carry = speed * towards <= 0 ? 0 : Math.Clamp(speed, -Math.Abs(towards) * LibrarySwipe.CarryPerPixel, Math.Abs(towards) * LibrarySwipe.CarryPerPixel);
        Spring.To(Bubble, "Translation", new Vector3(hidden, 0, 0), duration: 0.3, bounce: 0,
            from: now, velocity: new Vector3((float)carry, 0, 0));
        Spring.To(Ripple, "Scale", RingSmall, duration: 0.3, bounce: 0);
        // A spring has no end to wait for; by now it's out of sight.
        if (_gone is null)
        {
            _gone = DispatcherQueue.CreateTimer();
            _gone.IsRepeating = false;
            _gone.Interval = TimeSpan.FromMilliseconds(400);
            _gone.Tick += (_, _) => Hide();
        }
        _gone.Start();
    }

    private double At(double offset) => _direction < 0 ? offset - Room : Room - offset;

    private void Hide()
    {
        _gone?.Stop();
        Visibility = Visibility.Collapsed;
        Spring.Jump(Bubble, "Translation", new Vector3((float)At(0), 0, 0));
        Spring.Jump(Ripple, "Scale", RingSmall);
        Ui.StopFade(Bubble, 1);
    }
}
