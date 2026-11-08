using Drift;
using Xunit;

public class DownloadHoldTests
{
    [Fact]
    public void A_download_still_coming_in_is_left_running()
    {
        Assert.Equal(DownloadSettle.Running, DownloadHold.Check(engineDone: false, received: 40, total: 100, fileOnDisk: null));
    }

    [Fact]
    public void The_engine_saying_its_done_finishes_it()
    {
        Assert.Equal(DownloadSettle.Done, DownloadHold.Check(engineDone: true, received: 100, total: 100, fileOnDisk: null));
    }

    [Fact]
    public void Every_byte_in_and_the_finished_file_saved_finishes_it_even_unannounced()
    {
        // WebView2 doesn't always say a download finished; the file under its final name means it did.
        Assert.Equal(DownloadSettle.Done, DownloadHold.Check(engineDone: false, received: 100, total: 100, fileOnDisk: 100));
    }

    [Fact]
    public void Every_byte_in_but_no_finished_file_is_waiting_for_your_ok()
    {
        Assert.Equal(DownloadSettle.Waiting, DownloadHold.Check(engineDone: false, received: 100, total: 100, fileOnDisk: null));
    }

    [Fact]
    public void A_file_of_the_wrong_size_is_not_taken_as_finished()
    {
        Assert.Equal(DownloadSettle.Waiting, DownloadHold.Check(engineDone: false, received: 100, total: 100, fileOnDisk: 20));
    }

    [Fact]
    public void Without_a_known_size_it_waits_for_the_engine()
    {
        Assert.Equal(DownloadSettle.Running, DownloadHold.Check(engineDone: false, received: 100, total: null, fileOnDisk: 100));
    }
}
