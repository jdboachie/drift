using Drift;

public class GpuFailuresTests
{
    private static readonly DateTime Start = new(2026, 11, 15, 22, 13, 0);

    [Fact]
    public void One_failure_is_left_alone()
    {
        var failures = new GpuFailures();
        Assert.Equal(GpuFailure.Counted, failures.Report(Start));
    }

    [Fact]
    public void Every_page_reporting_the_same_failure_counts_once()
    {
        var failures = new GpuFailures();
        failures.Report(Start);
        for (var i = 1; i <= 30; i++)
            Assert.Equal(GpuFailure.Repeat, failures.Report(Start.AddMilliseconds(i * 50)));
    }

    [Fact]
    public void A_second_failure_soon_after_is_too_many()
    {
        var failures = new GpuFailures();
        failures.Report(Start);
        Assert.Equal(GpuFailure.TooMany, failures.Report(Start.AddMinutes(7)));
    }

    [Fact]
    public void Too_many_is_said_once()
    {
        var failures = new GpuFailures();
        failures.Report(Start);
        failures.Report(Start.AddMinutes(7));
        Assert.Equal(GpuFailure.Counted, failures.Report(Start.AddMinutes(10)));
    }

    [Fact]
    public void Failures_far_apart_are_left_alone()
    {
        var failures = new GpuFailures();
        failures.Report(Start);
        Assert.Equal(GpuFailure.Counted, failures.Report(Start.AddMinutes(45)));
        Assert.Equal(GpuFailure.TooMany, failures.Report(Start.AddMinutes(60)));
    }

    [Fact]
    public void Video_stays_on_the_cpu_on_the_runtime_it_failed_on()
    {
        Assert.True(GpuFailures.DecodeOnCpu(fellBackOn: "157.0.4341.46", runtime: "157.0.4341.46"));
    }

    [Fact]
    public void A_new_runtime_gets_the_gpu_again()
    {
        Assert.False(GpuFailures.DecodeOnCpu(fellBackOn: "157.0.4341.46", runtime: "158.0.4400.12"));
        Assert.False(GpuFailures.DecodeOnCpu(fellBackOn: null, runtime: "157.0.4341.46"));
        Assert.False(GpuFailures.DecodeOnCpu(fellBackOn: "157.0.4341.46", runtime: null));
    }
}
