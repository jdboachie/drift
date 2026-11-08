using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace Drift;

/// <summary>
/// Spring animations on a UIElement's Scale and Translation, run by the compositor off the UI thread.
/// Springs settle by physics rather than on a timeline, carry on smoothly when retargeted halfway
/// (the pointer changing its mind), and can overshoot a touch, which is what makes motion feel
/// alive rather than scripted.
/// Tuned like Apple's springs: a perceptual duration (one undamped oscillation) and a bounce,
/// from 0 (settles without overshooting) up. 0.1–0.3 reads as lively without wobbling.
/// With Windows animation effects turned off, values go straight to their end.
/// </summary>
internal static class Spring
{
    // Less motion than this left anywhere on the element, and the spring is done (see To).
    private const double RestPixels = 1;

    private static readonly UISettings Settings = new();

    private sealed record Run(CompositionAnimation Animation, Vector3 Target, DispatcherQueueTimer RestTimer);

    // The spring last started per element and property: so Jump can stop it, the next spring knows
    // where this one was heading, and the rest timer knows whether it's still the current one.
    private static readonly ConditionalWeakTable<UIElement, Dictionary<string, Run>> Running = new();

    /// <param name="property">"Scale" or "Translation".</param>
    /// <param name="from">
    /// Where to start, for an entrance. The spring starts there, but the element's own property is
    /// set to the end value straight away. XAML draws borders and text at that property's scale, so an
    /// element sat at Scale 0.5 while it grew was drawn at half resolution and stretched up, with soft
    /// borders until it came to rest. Drawn at its end scale, it only ever shrinks, which stays sharp.
    /// </param>
    /// <param name="kick">
    /// Starting speed, as a fraction of the spring's natural speed towards its end (0 = from rest).
    /// A spring from rest starts slowly, like ease-in; a kick makes it leave at once, like ease-out.
    /// </param>
    public static void To(UIElement element, string property, Vector3 value, double duration, double bounce,
        TimeSpan delay = default, Vector3? from = null, double kick = 0)
    {
        if (!Settings.AnimationsEnabled)
        {
            Jump(element, property, value);
            return;
        }
        if (from is not null) Jump(element, property, value);

        var spring = CompositionTarget.GetCompositorForCurrentThread().CreateSpringVector3Animation();
        spring.Target = property;
        spring.FinalValue = value;
        spring.DampingRatio = (float)(1 - bounce);
        // The compositor's Period is 1/ω, not a whole oscillation (2π/ω): a Period of 0.3s took over
        // 1.5s to settle. So a perceptual duration d becomes d / 2π.
        spring.Period = TimeSpan.FromSeconds(duration / (2 * Math.PI));
        if (from is { } start)
        {
            spring.InitialValue = start;
            // Show the start value during any delay, rather than the end value.
            spring.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            if (kick > 0) spring.InitialVelocity = (value - start) * (float)(kick * 2 * Math.PI / duration);
        }
        if (delay > TimeSpan.Zero) spring.DelayTime = delay;

        var running = Running.GetOrCreateValue(element);
        // Where it's coming from: the given start, where a running spring was heading, or the value at rest.
        var origin = from
            ?? (running.TryGetValue(property, out var heading) ? heading.Target
                : property == "Scale" ? element.Scale : element.Translation);
        if (running.TryGetValue(property, out var previous)) previous.RestTimer.Stop();

        // A spring's last stretch is an exponential crawl: a few pixels over a few hundred
        // milliseconds, with the edges (and web content) between pixels and resampled soft the
        // whole time. Like Framer Motion's rest threshold, once less than a pixel is left the value
        // jumps to its exact end, where everything lands on whole pixels and draws crisp.
        var rest = DispatcherQueue.GetForCurrentThread().CreateTimer();
        rest.IsRepeating = false;
        // A kicked spring swings further, so its tail lasts a little longer.
        rest.Interval = delay + RestTime(duration, bounce, Travel(element, property, origin, value) * (1 + kick));
        Run run = null!;
        rest.Tick += (timer, _) =>
        {
            timer.Stop();
            if (Running.TryGetValue(element, out var current) && current.TryGetValue(property, out var now) && now == run)
                Jump(element, property, value);
        };
        run = new Run(spring, value, rest);

        // Starting it replaces any spring already running on the property, from wherever that one got to.
        element.StartAnimation(spring);
        running[property] = run;
        rest.Start();
    }

    /// <summary>Straight to a value, replacing any spring on the property.</summary>
    public static void Jump(UIElement element, string property, Vector3 value)
    {
        if (property == "Scale") element.Scale = value;
        else element.Translation = value;

        if (Running.TryGetValue(element, out var running) && running.Remove(property, out var run))
        {
            run.RestTimer.Stop();
            // Stopping a spring leaves the element showing wherever it had got to, and setting the
            // property to the value it already holds changes nothing, so a card would stay at
            // Scale 1.0003 with soft borders. A 1ms animation lands it exactly on the value instead.
            var snap = CompositionTarget.GetCompositorForCurrentThread().CreateVector3KeyFrameAnimation();
            snap.Target = property;
            snap.InsertKeyFrame(1, value);
            snap.Duration = TimeSpan.FromMilliseconds(1);
            element.StartAnimation(snap);
        }
    }

    /// <summary>The furthest any part of the element moves, in device pixels.</summary>
    private static double Travel(UIElement element, string property, Vector3 from, Vector3 to)
    {
        var pixels = element.XamlRoot?.RasterizationScale ?? 1;
        if (property != "Scale") return (to - from).Length() * pixels;

        // Scaling moves the corner furthest from the centre point the most. Not laid out yet
        // (just made visible): assume a large element, which only makes the rest come later.
        var size = element.ActualSize;
        if (size.X <= 0 || size.Y <= 0) size = new Vector2(2000, 2000);
        var center = new Vector2(element.CenterPoint.X, element.CenterPoint.Y);
        var reach = Math.Max(
            Math.Max(Vector2.Distance(center, Vector2.Zero), Vector2.Distance(center, new Vector2(size.X, 0))),
            Math.Max(Vector2.Distance(center, new Vector2(0, size.Y)), Vector2.Distance(center, size)));
        var change = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y));
        return reach * change * pixels;
    }

    /// <summary>
    /// When less than RestPixels of motion is left, from the spring's decay: for a spring starting
    /// at rest, the distance still to go never exceeds e^(−ζωt)/√(1−ζ²) of the travel
    /// (critically damped: (1 + ωt)e^(−ωt)).
    /// </summary>
    private static TimeSpan RestTime(double duration, double bounce, double travel)
    {
        if (travel <= RestPixels) return TimeSpan.Zero;
        var zeta = 1 - bounce;
        var omega = 2 * Math.PI / duration;
        for (var t = 0.0; t < 3; t += 0.005)
        {
            var left = zeta < 1
                ? Math.Exp(-zeta * omega * t) / Math.Sqrt(1 - zeta * zeta)
                : (1 + omega * t) * Math.Exp(-omega * t);
            if (left * travel < RestPixels) return TimeSpan.FromSeconds(t);
        }
        return TimeSpan.FromSeconds(3);
    }
}
