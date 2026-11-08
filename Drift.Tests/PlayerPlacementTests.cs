using Drift;
using Xunit;

public class PlayerPlacementTests
{
    private static readonly Box Screen = new(0, 0, 1920, 1040);   // the work area, above the taskbar

    [Fact]
    public void A_video_shapes_its_player_within_400_by_360()
    {
        Assert.Equal((400, 225), PlayerPlacement.Size(videoWidth: 1920, videoHeight: 1080));
        Assert.Equal((400, 300), PlayerPlacement.Size(640, 480));
        Assert.Equal((203, 360), PlayerPlacement.Size(1080, 1920));   // a phone video stands up
        Assert.Equal((400, 225), PlayerPlacement.Size(0, 0));         // not known yet: 16:9
    }

    [Fact]
    public void The_first_player_sits_in_the_bottom_right_corner()
    {
        Assert.Equal(new Box(1920 - 16 - 400, 1040 - 16 - 225, 400, 225), PlayerPlacement.Place(null, 400, 225, Screen));
    }

    [Fact]
    public void Later_ones_open_where_the_last_was_left_kept_on_screen()
    {
        Assert.Equal(new Box(100, 80, 480, 270), PlayerPlacement.Place(new Box(100, 80, 480, 270), 400, 225, Screen));
        // Left partly off a screen that's since got smaller: pulled back in.
        Assert.Equal(new Box(1920 - 480, 1040 - 270, 480, 270), PlayerPlacement.Place(new Box(2500, 1200, 480, 270), 400, 225, Screen));
    }
}
