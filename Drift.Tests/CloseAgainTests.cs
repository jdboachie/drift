using Drift;

public class CloseAgainTests
{
    [Fact]
    public void The_first_press_only_asks_for_another()
    {
        var again = new CloseAgain();
        Assert.False(again.Press(1000));
    }

    [Fact]
    public void A_second_press_soon_after_closes()
    {
        var again = new CloseAgain();
        again.Press(1000);
        Assert.True(again.Press(1000 + CloseAgain.WindowMs));
    }

    [Fact]
    public void A_second_press_too_late_starts_over()
    {
        var again = new CloseAgain();
        again.Press(1000);
        Assert.False(again.Press(1001 + CloseAgain.WindowMs));
        Assert.True(again.Press(1500 + CloseAgain.WindowMs));
    }

    [Fact]
    public void After_closing_it_asks_again()
    {
        var again = new CloseAgain();
        again.Press(1000);
        again.Press(1200);
        Assert.False(again.Press(1400));
    }

    [Fact]
    public void Showing_until_the_moment_has_passed()
    {
        var again = new CloseAgain();
        Assert.False(again.IsWaiting(1000));
        again.Press(1000);
        Assert.True(again.IsWaiting(1000 + CloseAgain.WindowMs));
        Assert.False(again.IsWaiting(1001 + CloseAgain.WindowMs));
    }
}
