using System;
using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Drift;

/// <summary>
/// A page's card (PageCardView.xaml): <see cref="Body"/>, what's written inside the tag, sits in its face. The card
/// itself is what moves (a split's springs, a drag), shadow and all. Inside the face, a picture can show under the
/// page or over it: a two-finger swipe moving the page aside as Safari's does (SwipePage.cs), or the page as it last
/// looked while it loads again.
/// </summary>
[ContentProperty(Name = nameof(Body))]
public sealed partial class PageCardView : UserControl
{
    public PageCardView()
    {
        InitializeComponent();
        BodyEdge.Fill = EdgeShadow();
        PictureEdge.Fill = EdgeShadow();
    }

    public static readonly DependencyProperty BodyProperty = DependencyProperty.Register(
        nameof(Body), typeof(UIElement), typeof(PageCardView),
        new PropertyMetadata(null, (d, e) =>
        {
            // Under its shade and its edge's shadow, in the face.
            var holder = ((PageCardView)d).BodyHolder;
            if (e.OldValue is UIElement old) holder.Children.Remove(old);
            if (e.NewValue is UIElement body) holder.Children.Insert(0, body);
        }));

    public UIElement? Body { get => (UIElement?)GetValue(BodyProperty); set => SetValue(BodyProperty, value); }

    public static readonly DependencyProperty LiftProperty = DependencyProperty.Register(
        nameof(Lift), typeof(double), typeof(PageCardView),
        new PropertyMetadata(10.0, (d, e) => ((PageCardView)d).Caster.Translation = new Vector3(0, 0, (float)(double)e.NewValue)));

    /// <summary>How high the card floats, so how far its shadow falls: 10 for a page in the window, more over a page.</summary>
    public double Lift { get => (double)GetValue(LiftProperty); set => SetValue(LiftProperty, value); }

    // ───────────────────────── A swipe moving the page ─────────────────────────
    // WebKit's (ViewGestureControllerMac.mm): going back, the page moves off to the right over a still picture of the
    // one before, dimmed 12% at first and less as it's uncovered, with an 81-wide shadow along the page's edge at 6%
    // (fading over its last 81). Going forward, the picture of the next page comes in from the right over a still page,
    // which dims to 12%, with the shadow along the picture's edge. No parallax.

    private const double Dim = 0.12;
    private const double EdgeWidth = 81;
    private const double EdgeOpacity = 0.06;

    /// <summary>Let go, how long the page takes to get where it's going: a spring of about this long, without bounce.</summary>
    private const double LetGoSeconds = 0.3;

    /// <summary>WebKit's shadow: black, darkest at the edge, in 16 steps.</summary>
    private static LinearGradientBrush EdgeShadow()
    {
        double[] alpha = [1, .99, .98, .95, .92, .82, .71, .46, .35, .25, .17, .11, .07, .04, .01, 0];
        double[] at = [0, .03125, .0625, .0938, .125, .1875, .25, .375, .4375, .5, .5625, .625, .6875, .75, .875, 1];
        // From the edge (right) outwards (left).
        var brush = new LinearGradientBrush { StartPoint = new Point(1, 0), EndPoint = new Point(0, 0) };
        for (var i = 0; i < alpha.Length; i++)
            brush.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb((byte)Math.Round(alpha[i] * 255), 0, 0, 0), Offset = at[i] });
        return brush;
    }

    private int _swipeWay;
    private DispatcherQueueTimer? _lastLookGone;

    /// <summary>A swipe has the picture: a tab's last look leaves it be.</summary>
    internal bool Swiping => _swipeWay != 0;

    /// <summary>The width the page moves across.</summary>
    internal double PageWidth => BodyHolder.ActualWidth;

    /// <summary>A swipe back (-1) or forward (1) starts moving the page; its picture may come a moment later.</summary>
    internal void BeginSwipe(int way, ImageSource? picture)
    {
        _lastLookGone?.Stop();
        _swipeWay = way;
        Picture.Source = picture;
        Ui.StopFade(PictureLayer, 1);
        Canvas.SetZIndex(BodyHolder, 1);
        Canvas.SetZIndex(PictureLayer, way < 0 ? 0 : 2);
        PictureLayer.Visibility = Visibility.Visible;
        PictureShade.Visibility = Ui.Shown(way < 0);
        BodyShade.Visibility = Ui.Shown(way > 0);
        BodyEdge.Visibility = Ui.Shown(way < 0);
        PictureEdge.Visibility = Ui.Shown(way > 0);
        FollowSwipe(0);
    }

    internal void SwipePictureReady(ImageSource picture)
    {
        if (Swiping) Picture.Source = picture;
    }

    /// <summary>The page has moved this far with the fingers (DIPs, from nothing to its width).</summary>
    internal void FollowSwipe(double offset)
    {
        var width = Math.Max(1, PageWidth);
        offset = Math.Clamp(offset, 0, width);
        var edge = EdgeOpacity * Math.Min(1, (width - offset) / EdgeWidth);
        if (_swipeWay < 0)
        {
            Spring.Jump(BodyHolder, "Translation", new Vector3((float)offset, 0, 0));
            Spring.Jump(PictureLayer, "Translation", Vector3.Zero);
            Ui.StopFade(PictureShade, Dim * (1 - offset / width));
            Ui.StopFade(BodyEdge, edge);
        }
        else
        {
            Spring.Jump(PictureLayer, "Translation", new Vector3((float)(width - offset), 0, 0));
            Spring.Jump(BodyHolder, "Translation", Vector3.Zero);
            Ui.StopFade(BodyShade, Dim * offset / width);
            Ui.StopFade(PictureEdge, edge);
        }
    }

    /// <summary>
    /// Let go: on to the end (the page goes off, or the picture comes all the way in), or back to the start, from where
    /// it is, carrying on at the fingers' speed (DIPs a second along the swipe) if that's the way it's going.
    /// </summary>
    internal void LetGoSwipe(bool through, double speed)
    {
        if (!Swiping) return;
        var width = (float)Math.Max(1, PageWidth);
        var offset = _swipeWay < 0 ? BodyHolder.Translation.X : width - PictureLayer.Translation.X;
        var end = through ? width : 0f;
        var towards = end - offset;
        var most = Math.Abs(towards) * LibrarySwipe.CarryPerPixel;
        var carry = (float)(speed * towards <= 0 ? 0 : Math.Clamp(speed, -most, most));
        if (_swipeWay < 0)
        {
            Spring.To(BodyHolder, "Translation", new Vector3(end, 0, 0), LetGoSeconds, 0,
                from: new Vector3(offset, 0, 0), velocity: new Vector3(carry, 0, 0));
            Ui.Fade(PictureShade, through ? 0 : Dim, milliseconds: 250);
            Ui.Fade(BodyEdge, through ? 0 : EdgeOpacity, milliseconds: 250);
        }
        else
        {
            Spring.To(PictureLayer, "Translation", new Vector3(width - end, 0, 0), LetGoSeconds, 0,
                from: new Vector3(width - offset, 0, 0), velocity: new Vector3(-carry, 0, 0));
            Ui.Fade(BodyShade, through ? Dim : 0, milliseconds: 250);
            Ui.Fade(PictureEdge, through ? 0 : EdgeOpacity, milliseconds: 250);
        }
    }

    /// <summary>
    /// The swipe's over: the picture goes at once, and the page is back in its place (showing the page it went to, or
    /// the one it stayed on). As WebKit's, no fade: by now the page looks as the picture did.
    /// </summary>
    internal void EndSwipe()
    {
        if (!Swiping) return;
        _swipeWay = 0;
        PictureLayer.Visibility = Visibility.Collapsed;
        Picture.Source = null;
        Spring.Jump(BodyHolder, "Translation", Vector3.Zero);
        Spring.Jump(PictureLayer, "Translation", Vector3.Zero);
        foreach (var part in new UIElement[] { PictureShade, BodyShade, BodyEdge, PictureEdge })
        {
            Ui.StopFade(part, 0);
            part.Visibility = Visibility.Collapsed;
        }
    }

    // ───────────────────────── A page's last look ─────────────────────────

    /// <summary>How much of the page's last look shows while it loads again: enough to know it, faded so it reads as not ready.</summary>
    private const double LastLookOpacity = 0.5;

    /// <summary>The page as it last looked, faded, under the page while it loads again (SwipePage.cs).</summary>
    internal void ShowLastLook(ImageSource picture)
    {
        if (Swiping) return;
        _lastLookGone?.Stop();
        Picture.Source = picture;
        Canvas.SetZIndex(PictureLayer, 0);
        Spring.Jump(PictureLayer, "Translation", Vector3.Zero);
        PictureShade.Visibility = PictureEdge.Visibility = Visibility.Collapsed;
        Ui.StopFade(PictureLayer, LastLookOpacity);
        PictureLayer.Visibility = Visibility.Visible;
    }

    internal void HideLastLook()
    {
        if (Swiping) return;
        _lastLookGone?.Stop();
        PictureLayer.Visibility = Visibility.Collapsed;
        Picture.Source = null;
        Ui.StopFade(PictureLayer, 1);
    }

    /// <summary>Once the page has faded in over it.</summary>
    internal void HideLastLookSoon()
    {
        if (_lastLookGone is null)
        {
            _lastLookGone = DispatcherQueue.CreateTimer();
            _lastLookGone.IsRepeating = false;
            _lastLookGone.Interval = TimeSpan.FromMilliseconds(200);
            _lastLookGone.Tick += (_, _) => HideLastLook();
        }
        _lastLookGone.Start();
    }
}
