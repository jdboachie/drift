using Drift;

public class DownloadRingTests
{
    [Fact]
    public void Nothing_running_shows_no_ring()
    {
        Assert.Null(DownloadRing.Of([]));
    }

    [Fact]
    public void Fills_with_everything_received_of_everything_expected()
    {
        var ring = DownloadRing.Of([(Received: 50, Total: 100, Paused: false), (Received: 0, Total: 300, Paused: false)]);
        Assert.Equal(new RingState(0.125, Paused: false), ring);
    }

    [Fact]
    public void Spins_while_any_size_is_unknown()
    {
        var ring = DownloadRing.Of([(Received: 50, Total: 100, Paused: false), (Received: 10, Total: null, Paused: false)]);
        Assert.Equal(new RingState(null, Paused: false), ring);
    }

    [Fact]
    public void Greys_only_when_everything_is_paused()
    {
        Assert.Equal(new RingState(0.5, Paused: true), DownloadRing.Of([(Received: 50, Total: 100, Paused: true)]));
        Assert.Equal(new RingState(0.5, Paused: false),
            DownloadRing.Of([(Received: 25, Total: 50, Paused: true), (Received: 25, Total: 50, Paused: false)]));
    }

    [Fact]
    public void Never_overfills()
    {
        Assert.Equal(new RingState(1, Paused: false), DownloadRing.Of([(Received: 120, Total: 100, Paused: false)]));
    }
}
