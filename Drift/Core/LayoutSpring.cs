namespace Drift;

/// <summary>
/// A spring stepped by hand, for what the compositor's springs (Spring) can't move: a size, or a place
/// that layout sets, like an Essential's tile changing width as it moves between a full row and a short
/// one (EssentialsPanel). Tuned as Spring is: a perceptual duration (one undamped oscillation) and a
/// bounce from 0 (no overshoot). Solved exactly rather than integrated, so a long frame can't fling it,
/// and it keeps the speed it has when its target changes, so a retarget halfway carries on smoothly.
/// </summary>
public static class LayoutSpring
{
    /// <summary>Closer than this, and slower: it's arrived.</summary>
    private const double RestPixels = 0.5;
    private const double RestSpeed = 10;

    /// <summary>Moves <paramref name="at"/> (and its <paramref name="speed"/>, a second) towards <paramref name="target"/> over <paramref name="seconds"/>.</summary>
    public static void Step(ref double at, ref double speed, double target, double seconds, double duration, double bounce)
    {
        var omega = 2 * System.Math.PI / duration;
        var zeta = 1 - System.Math.Clamp(bounce, 0, 0.99);
        var x0 = at - target;
        var v0 = speed;
        var t = seconds;
        double x, v;
        if (zeta >= 1)
        {
            var decay = System.Math.Exp(-omega * t);
            x = (x0 + (v0 + omega * x0) * t) * decay;
            v = (v0 - omega * (v0 + omega * x0) * t) * decay;
        }
        else
        {
            var damped = omega * System.Math.Sqrt(1 - zeta * zeta);
            var decay = System.Math.Exp(-zeta * omega * t);
            var cos = System.Math.Cos(damped * t);
            var sin = System.Math.Sin(damped * t);
            x = decay * (x0 * cos + (v0 + zeta * omega * x0) / damped * sin);
            v = decay * (v0 * cos - (zeta * omega * v0 + omega * omega * x0) / damped * sin);
        }
        at = target + x;
        speed = v;
    }

    public static bool Resting(double at, double speed, double target) =>
        System.Math.Abs(at - target) < RestPixels && System.Math.Abs(speed) < RestSpeed;
}
