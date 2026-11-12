using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;

namespace Drift;

/// <summary>Small conversions for x:Bind, so the models (in Core) needn't know about XAML.</summary>
public static class Ui
{
    public static Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility Hidden(bool hidden) => hidden ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// Fades an element's opacity to a value, on the strong ease-out, about 150ms: what's left of a
    /// slide or spring with Windows' animation effects turned off. Less motion, still some feedback.
    /// When it's done the element holds the value as its own (not held by the animation), and
    /// <paramref name="done"/> runs, unless another fade of the element has started since.
    /// </summary>
    public static void Fade(UIElement element, double to, Action? done = null, int milliseconds = 150)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(milliseconds),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var board = new Storyboard();
        board.Children.Add(animation);
        // A fade already going: from wherever it got to, with nothing left holding the old one's value.
        if (Fades.TryGetValue(element, out var previous))
        {
            var now = element.Opacity;
            previous.Stop();
            element.Opacity = now;
            Fades.Remove(element);
        }
        Fades.Add(element, board);
        board.Completed += (_, _) =>
        {
            if (!Fades.TryGetValue(element, out var current) || current != board) return;
            Fades.Remove(element);
            element.Opacity = to;
            board.Stop();
            done?.Invoke();
        };
        board.Begin();
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UIElement, Storyboard> Fades = new();
}
