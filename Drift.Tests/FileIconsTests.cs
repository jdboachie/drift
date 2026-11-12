using Drift;

public class FileIconsTests
{
    [Fact]
    public void Files_of_a_type_share_one_icon_whatever_their_name()
    {
        var a = FileIcons.Source(@"C:\Users\me\Downloads\Report.PDF", exists: true);
        var b = FileIcons.Source(@"D:\other\notes.pdf", exists: false);
        Assert.Equal(".pdf", a.Key);
        Assert.Equal(a.Key, b.Key);
        Assert.False(a.FromFile);
    }

    [Fact]
    public void Programs_and_shortcuts_have_their_own_icon_once_on_disk()
    {
        var setup = FileIcons.Source(@"C:\Users\me\Downloads\ZoomInstaller.exe", exists: true);
        Assert.True(setup.FromFile);
        Assert.Equal(@"C:\Users\me\Downloads\ZoomInstaller.exe", setup.Key);
        Assert.True(FileIcons.Source(@"C:\x\site.url", exists: true).FromFile);
        Assert.True(FileIcons.Source(@"C:\x\app.ico", exists: true).FromFile);
    }

    [Fact]
    public void A_program_not_on_disk_yet_gets_the_type_icon()
    {
        // Still downloading, or deleted since.
        var setup = FileIcons.Source(@"C:\Users\me\Downloads\ZoomInstaller.exe", exists: false);
        Assert.False(setup.FromFile);
        Assert.Equal(".exe", setup.Key);
    }

    [Fact]
    public void A_file_without_an_extension_gets_the_plain_file_icon()
    {
        var plain = FileIcons.Source(@"C:\Users\me\Downloads\README", exists: true);
        Assert.Equal("", plain.Key);
        Assert.False(plain.FromFile);
    }

    [Fact]
    public void Icon_pixels_with_their_own_transparency_are_premultiplied()
    {
        // One opaque red pixel, one half-transparent white one (BGRA).
        var pixels = new byte[] { 0, 0, 255, 255, 255, 255, 255, 128 };
        IconPixels.Prepare(pixels, mask: null);
        Assert.Equal(new byte[] { 0, 0, 255, 255, 128, 128, 128, 128 }, pixels);
    }

    [Fact]
    public void Old_icons_without_transparency_take_it_from_their_mask()
    {
        // No alpha at all: the mask says the first pixel shows and the second doesn't.
        var pixels = new byte[] { 10, 20, 30, 0, 40, 50, 60, 0 };
        var mask = new byte[] { 0, 0, 0, 0, 255, 255, 255, 0 };
        IconPixels.Prepare(pixels, mask);
        Assert.Equal(new byte[] { 10, 20, 30, 255, 0, 0, 0, 0 }, pixels);
    }
}
