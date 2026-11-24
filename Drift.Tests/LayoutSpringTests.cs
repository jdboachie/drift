using Drift;
using Xunit;

public class LayoutSpringTests
{
    private static (double At, double Speed, double Furthest) Run(double from, double to, double seconds, double duration, double bounce, double speed = 0)
    {
        var at = from;
        var furthest = from;
        for (var t = 0.0; t < seconds; t += 1 / 60.0)
        {
            LayoutSpring.Step(ref at, ref speed, to, 1 / 60.0, duration, bounce);
            if (System.Math.Abs(at - from) > System.Math.Abs(furthest - from)) furthest = at;
        }
        return (at, speed, furthest);
    }

    [Fact]
    public void It_arrives_within_about_its_duration()
    {
        var (at, _, _) = Run(0, 100, 0.6, duration: 0.3, bounce: 0);
        Assert.InRange(at, 99, 100.0001);
    }

    [Fact]
    public void Without_bounce_it_never_overshoots()
    {
        var (_, _, furthest) = Run(0, 100, 2, duration: 0.3, bounce: 0);
        Assert.True(furthest <= 100.0001, $"went to {furthest}");
    }

    [Fact]
    public void With_bounce_it_overshoots_a_little()
    {
        var (_, _, furthest) = Run(0, 100, 2, duration: 0.3, bounce: 0.15);
        Assert.InRange(furthest, 100.5, 115);
    }

    [Fact]
    public void A_speed_it_already_has_carries_on()
    {
        // Heading away at 1000/s: it goes further away first, rather than turning on the spot.
        var at = 0.0;
        var speed = -1000.0;
        LayoutSpring.Step(ref at, ref speed, 100, 1 / 60.0, 0.3, 0);
        Assert.True(at < 0, $"at {at}");
    }

    [Fact]
    public void A_long_frame_stays_steady()
    {
        // A hitch of a quarter second doesn't fling it.
        var at = 0.0;
        var speed = 0.0;
        LayoutSpring.Step(ref at, ref speed, 100, 0.25, 0.3, 0);
        Assert.InRange(at, 0, 100.0001);
    }

    [Fact]
    public void Resting_is_close_and_slow()
    {
        Assert.True(LayoutSpring.Resting(99.8, 0.5, 100));
        Assert.False(LayoutSpring.Resting(98, 0, 100));
        Assert.False(LayoutSpring.Resting(100, 50, 100));
    }
}
