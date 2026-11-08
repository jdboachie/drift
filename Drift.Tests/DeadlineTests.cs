using System;
using System.Threading.Tasks;
using Drift;
using Xunit;

public class DeadlineTests
{
    [Fact]
    public async Task A_step_that_finishes_in_time_gives_its_result()
    {
        Assert.Equal(7, await Deadline.Within(Task.FromResult(7), TimeSpan.FromSeconds(1), fallback: 0));
    }

    [Fact]
    public async Task A_step_that_never_finishes_gives_the_fallback_once_time_is_up()
    {
        var never = new TaskCompletionSource<int>().Task;
        Assert.Equal(-1, await Deadline.Within(never, TimeSpan.FromMilliseconds(50), fallback: -1));
    }

    [Fact]
    public async Task A_step_that_fails_gives_the_fallback()
    {
        Assert.Equal(-1, await Deadline.Within(Task.FromException<int>(new InvalidOperationException()), TimeSpan.FromSeconds(1), fallback: -1));
    }
}
