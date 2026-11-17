using Drift;

public class VideoDecodeTests
{
    private const string Runtime = "157.0.4341.46";
    private static readonly Gpu Hd530Old = VideoDecode.Parse(@"PCI\VEN_8086&DEV_191B", "31.0.101.2111")!.Value;
    private static readonly Gpu Quadro = VideoDecode.Parse(@"pci\ven_10de&dev_13b0&subsys_80d5103c", "32.0.15.7371")!.Value;

    [Fact]
    public void Reads_the_vendor_device_and_driver_from_windows_ids()
    {
        Assert.Equal(new Gpu(0x8086, 0x191B, new Version(31, 0, 101, 2111)), Hd530Old);
        Assert.Equal(0x10DE, Quadro.Vendor);
        Assert.Equal(0x13B0, Quadro.Device);
    }

    [Fact]
    public void Ids_that_arent_pci_devices_are_skipped()
    {
        Assert.Null(VideoDecode.Parse(@"ROOT\BasicRender", "10.0.22621.1"));
        Assert.Null(VideoDecode.Parse(null, null));
    }

    [Fact]
    public void A_driver_version_that_cant_be_read_is_unknown()
    {
        Assert.Null(VideoDecode.Parse(@"PCI\VEN_8086&DEV_191B", "unknown")!.Value.Driver);
    }

    [Fact]
    public void Skylake_graphics_on_the_driver_seen_hanging_decode_on_the_cpu()
    {
        Assert.Equal(VideoDecoding.CpuKnownIssue, VideoDecode.Choose([Quadro, Hd530Old], fellBackOn: null, Runtime));
    }

    [Fact]
    public void Older_drivers_count_too()
    {
        var older = Hd530Old with { Driver = new Version(27, 20, 100, 9664) };
        Assert.Equal(VideoDecoding.CpuKnownIssue, VideoDecode.Choose([older], null, Runtime));
    }

    [Fact]
    public void A_newer_driver_gets_the_gpu()
    {
        var newer = Hd530Old with { Driver = new Version(31, 0, 101, 2130) };
        Assert.Equal(VideoDecoding.Gpu, VideoDecode.Choose([newer], null, Runtime));
    }

    [Fact]
    public void Other_graphics_get_the_gpu()
    {
        var newerIntel = new Gpu(0x8086, 0x9A49, new Version(31, 0, 101, 2111));
        Assert.Equal(VideoDecoding.Gpu, VideoDecode.Choose([Quadro, newerIntel], null, Runtime));
        Assert.Equal(VideoDecoding.Gpu, VideoDecode.Choose([], null, Runtime));
    }

    [Fact]
    public void An_unknown_driver_gets_the_gpu()
    {
        Assert.Equal(VideoDecoding.Gpu, VideoDecode.Choose([Hd530Old with { Driver = null }], null, Runtime));
    }

    [Fact]
    public void Having_failed_on_this_runtime_decodes_on_the_cpu()
    {
        Assert.Equal(VideoDecoding.CpuAfterFailures, VideoDecode.Choose([Quadro], fellBackOn: Runtime, Runtime));
        Assert.Equal(VideoDecoding.Gpu, VideoDecode.Choose([Quadro], fellBackOn: "156.0.1.1", Runtime));
    }
}
