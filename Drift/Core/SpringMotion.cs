using System;

namespace Drift;

/// <summary>
/// A spring stepped frame by frame, for what the compositor can't animate (a layout width).
/// Tuned like Spring (and Apple): a perceptual duration and a bounce. No UI here, so it can be tested.
/// </summary>
public sealed class SpringMotion
{
    private readonly double _omega;
    private readonly double _damping;

    public SpringMotion(double duration, double bounce)
    {
        _omega = 2 * Math.PI / duration;
        _damping = 1 - bounce;
    }

    public double Position { get; set; }
    public double Target { get; set; }

    /// <summary>Units per second.</summary>
    public double Velocity { get; set; }

    /// <summary>Less than half a unit from its target, and all but still.</summary>
    public bool AtRest => Math.Abs(Position - Target) < 0.5 && Math.Abs(Velocity) < 10;

    /// <summary>Moves it on by some time, in millisecond steps (a frame at a time is too coarse to be stable).</summary>
    public void Step(double seconds)
    {
        for (var left = seconds; left > 0; left -= 0.001)
        {
            var dt = Math.Min(0.001, left);
            var acceleration = -_omega * _omega * (Position - Target) - 2 * _damping * _omega * Velocity;
            Velocity += acceleration * dt;
            Position += Velocity * dt;
        }
    }
}
