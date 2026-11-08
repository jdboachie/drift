namespace Drift.Tests;

public class SpringMotionTests
{
    private static List<double> Run(SpringMotion spring, double seconds)
    {
        var path = new List<double>();
        for (var t = 0.0; t < seconds; t += 1 / 60.0)
        {
            spring.Step(1 / 60.0);
            path.Add(spring.Position);
        }
        return path;
    }

    [Fact]
    public void It_comes_to_rest_at_its_target()
    {
        var spring = new SpringMotion(duration: 0.3, bounce: 0.2) { Position = 194, Target = 426 };

        Run(spring, 1.0);

        Assert.True(spring.AtRest);
        Assert.Equal(426, spring.Position, precision: 0);
    }

    [Fact]
    public void A_bounce_overshoots_a_little_like_Zens_sidebar()
    {
        var spring = new SpringMotion(duration: 0.3, bounce: 0.2) { Position = 426, Target = 194 };

        var lowest = Run(spring, 1.0).Min();

        // Zen went 3px past over the same 232px.
        Assert.InRange(194 - lowest, 1.5, 5);
    }

    [Fact]
    public void Without_bounce_it_never_overshoots()
    {
        var spring = new SpringMotion(duration: 0.3, bounce: 0) { Position = 426, Target = 194 };

        Assert.True(Run(spring, 1.0).Min() >= 194 - 0.01);
    }

    [Fact]
    public void Most_of_the_way_there_in_about_its_duration()
    {
        var spring = new SpringMotion(duration: 0.3, bounce: 0.2) { Position = 0, Target = 100 };

        var path = Run(spring, 0.3);

        Assert.True(path[^1] > 95);
    }

    [Fact]
    public void It_carries_on_the_way_it_was_already_going()
    {
        // Let go mid-swipe, moving away from where it'll end up: it keeps going a moment first.
        var spring = new SpringMotion(duration: 0.3, bounce: 0.2) { Position = 300, Target = 194, Velocity = 1500 };

        spring.Step(1 / 60.0);

        Assert.True(spring.Position > 300);
    }
}
