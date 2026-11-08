using Drift;
using Xunit;

public class BrowserPipTests
{
    [Fact]
    public void The_engines_floating_video_window_is_kept_off_the_taskbar()
    {
        Assert.True(BrowserPip.IsPlayer(visible: true, alwaysOnTop: true));
    }

    [Fact]
    public void The_engines_other_windows_are_left_alone()
    {
        Assert.False(BrowserPip.IsPlayer(visible: true, alwaysOnTop: false));
        Assert.False(BrowserPip.IsPlayer(visible: false, alwaysOnTop: true));
    }
}
