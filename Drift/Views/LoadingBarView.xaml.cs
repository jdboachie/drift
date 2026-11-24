using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace Drift;

/// <summary>
/// The page's loading bar (LoadingBarView.xaml, LoadingBar.cs). Arc's since February 2023 ("direct your eyes to the
/// top center of the Arc window", its release notes); its measures, taken frame by frame from Arc 1.171 by an Arc
/// clone's makers (github.com/VictorKildahl/cra, docs/arc-reverse-engineering.md, "The loading bar above the card"):
/// waiting, a 48-wide pill at 75% fades in over 0.7s, out over 0.65s, and stays off 0.25s; once the page comes, it
/// widens to a 130-wide track at 18% in 0.35s, with a 76% piece sweeping it every 1.12s; loaded, the whole track
/// lights up, then shrinks towards its middle to 75 and fades in 0.3s. Arc's is white over its tinted window; Drift's
/// is the icons' colour, which follows light and dark. With Windows' animation effects off, it holds still.
/// </summary>
public sealed partial class LoadingBarView : UserControl
{
    private const double PillWidth = 48;
    private const double TrackWidth = 130;
    private const double DoneWidth = 75;
    private const double PillOpacity = 0.75;
    private const double TrackOpacity = 0.18;
    private const double LitOpacity = 0.76;
    private const double SweepWidth = 40;

    private static readonly UISettings Settings = new();

    private Storyboard? _running;
    private LoadPhase _phase;

    public LoadingBarView() => InitializeComponent();

    /// <summary>Showing, or on its way out.</summary>
    public bool Showing => Visibility == Visibility.Visible;

    /// <summary>It's gone: what made way for it can come back.</summary>
    public event Action? Gone;

    /// <summary>
    /// Shows a load's phase. <paramref name="animate"/>: as it happens to the page in front; otherwise (another
    /// tab come to the front) straight to how that phase looks.
    /// </summary>
    public void Show(LoadPhase phase, bool animate)
    {
        if (phase == _phase && (phase != LoadPhase.None || Visibility == Visibility.Collapsed)) return;
        var was = _phase;
        _phase = phase;
        switch (phase)
        {
            case LoadPhase.Waiting: Wait(); break;
            case LoadPhase.Drawing: Draw(animate); break;
            default:
                if (animate && was == LoadPhase.Drawing && Settings.AnimationsEnabled) Done();
                else Hide();
                break;
        }
    }

    private void Wait()
    {
        Begin(null);
        Visibility = Visibility.Visible;
        SweepRoom.Visibility = Visibility.Collapsed;
        Bar.Width = PillWidth;
        if (!Settings.AnimationsEnabled)
        {
            Bar.Opacity = PillOpacity;
            return;
        }
        Bar.Opacity = 0;
        var board = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        var pulse = Track(board, Bar, "Opacity", dependent: false);
        Key(pulse, 0, 0);
        Key(pulse, 0.7, PillOpacity, easeInOut: true);
        Key(pulse, 1.35, 0, easeInOut: true);
        Key(pulse, 1.6, 0);
        Begin(board);
    }

    private void Draw(bool animate)
    {
        var from = Bar.Opacity;
        var width = Bar.Width;
        Begin(null);
        Visibility = Visibility.Visible;
        SweepRoom.Visibility = Visibility.Visible;
        Bar.Width = TrackWidth;
        Bar.Opacity = TrackOpacity;
        if (!Settings.AnimationsEnabled)
        {
            // Holding still: the piece rests in the middle.
            SweepAt.X = (TrackWidth - SweepWidth) / 2;
            return;
        }
        var board = new Storyboard();
        if (animate)
        {
            var widen = Track(board, Bar, "Width", dependent: true);
            Key(widen, 0, width);
            Key(widen, 0.35, TrackWidth, easeOut: true);
            var dim = Track(board, Bar, "Opacity", dependent: false);
            Key(dim, 0, from);
            Key(dim, 0.35, TrackOpacity, easeOut: true);
        }
        var sweep = Track(board, SweepAt, "X", dependent: false);
        sweep.RepeatBehavior = RepeatBehavior.Forever;
        Key(sweep, 0, -SweepWidth);
        Key(sweep, 1.12, TrackWidth);
        Begin(board);
    }

    private void Done()
    {
        Begin(null);
        SweepRoom.Visibility = Visibility.Collapsed;
        var board = new Storyboard();
        var light = Track(board, Bar, "Opacity", dependent: false);
        Key(light, 0, Bar.Opacity);
        Key(light, 0.1, LitOpacity, easeOut: true);
        Key(light, 0.4, 0, easeOut: true);
        var shrink = Track(board, Bar, "Width", dependent: true);
        Key(shrink, 0, Bar.Width);
        Key(shrink, 0.1, Bar.Width);
        Key(shrink, 0.4, DoneWidth, easeOut: true);
        board.Completed += (_, _) =>
        {
            if (_running != board) return;
            Hide();
        };
        Begin(board);
    }

    private void Hide()
    {
        Begin(null);
        var was = Showing;
        Visibility = Visibility.Collapsed;
        Bar.Opacity = 0;
        Bar.Width = PillWidth;
        SweepRoom.Visibility = Visibility.Collapsed;
        if (was) Gone?.Invoke();
    }

    /// <summary>Stops what's running, and starts <paramref name="board"/>, if there's one.</summary>
    private void Begin(Storyboard? board)
    {
        var old = _running;
        _running = board;
        old?.Stop();
        board?.Begin();
    }

    private static DoubleAnimationUsingKeyFrames Track(Storyboard board, DependencyObject target, string property, bool dependent)
    {
        var track = new DoubleAnimationUsingKeyFrames { EnableDependentAnimation = dependent };
        Storyboard.SetTarget(track, target);
        Storyboard.SetTargetProperty(track, property);
        board.Children.Add(track);
        return track;
    }

    /// <summary>A key: at a time (seconds), a value, on the strong ease-out, an ease-in-out, or straight.</summary>
    private static void Key(DoubleAnimationUsingKeyFrames track, double seconds, double value, bool easeOut = false, bool easeInOut = false)
    {
        var (a, b) = easeOut ? (new Point(0.23, 1), new Point(0.32, 1))
            : easeInOut ? (new Point(0.42, 0), new Point(0.58, 1))
            : (new Point(0, 0), new Point(1, 1));
        track.KeyFrames.Add(new SplineDoubleKeyFrame
        {
            KeyTime = TimeSpan.FromSeconds(seconds),
            Value = value,
            KeySpline = new KeySpline { ControlPoint1 = a, ControlPoint2 = b },
        });
    }
}
