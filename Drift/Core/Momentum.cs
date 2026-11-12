namespace Drift;

/// <summary>Where a gesture's momentum would carry it: Apple's projection, as scrolling decelerates.</summary>
public static class Momentum
{
    // Apple's normal deceleration rate.
    private const double DecelerationRate = 0.998;

    /// <summary>How much further something let go at a velocity (pixels a second) travels before it stops.</summary>
    public static double Project(double velocity) =>
        velocity / 1000 * DecelerationRate / (1 - DecelerationRate);
}
