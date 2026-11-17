using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Drift;

/// <summary>A display adapter, from Windows' device id ("PCI\VEN_8086&amp;DEV_191B") and driver version.</summary>
public readonly record struct Gpu(int Vendor, int Device, Version? Driver);

public enum VideoDecoding
{
    /// <summary>On the graphics card, as normal.</summary>
    Gpu,
    /// <summary>On the CPU: this graphics card and driver are known to hang decoding video.</summary>
    CpuKnownIssue,
    /// <summary>On the CPU: the GPU kept failing on this WebView2 runtime (GpuFailures).</summary>
    CpuAfterFailures,
}

/// <summary>
/// Where the engine decodes video, chosen before it starts (an engine's options can't change once
/// it's running). See KNOWN-ISSUES.md: "Pages freeze when the GPU hangs decoding video".
/// </summary>
public static partial class VideoDecode
{
    private const int Intel = 0x8086;
    /// <summary>The newest driver seen hanging; newer ones haven't been tried, and get the GPU.</summary>
    private static readonly Version LastHangingDriver = new(31, 0, 101, 2111);

    /// <summary>
    /// Intel's 6th-generation (Skylake: HD 510–580, Iris 540–580, device ids 0x19xx) graphics, on a
    /// driver no newer than the one seen hanging: its GPU process hung on HEVC every few minutes.
    /// </summary>
    public static bool KnownToHang(Gpu gpu) =>
        gpu.Vendor == Intel && (gpu.Device & 0xFF00) == 0x1900 && gpu.Driver is { } driver && driver <= LastHangingDriver;

    public static VideoDecoding Choose(IEnumerable<Gpu> gpus, string? fellBackOn, string? runtime)
    {
        if (GpuFailures.DecodeOnCpu(fellBackOn, runtime)) return VideoDecoding.CpuAfterFailures;
        return gpus.Any(KnownToHang) ? VideoDecoding.CpuKnownIssue : VideoDecoding.Gpu;
    }

    public static Gpu? Parse(string? deviceId, string? driverVersion)
    {
        var id = PciId().Match(deviceId ?? "");
        if (!id.Success) return null;
        return new Gpu(
            int.Parse(id.Groups[1].Value, NumberStyles.HexNumber),
            int.Parse(id.Groups[2].Value, NumberStyles.HexNumber),
            Version.TryParse(driverVersion, out var driver) ? driver : null);
    }

    [GeneratedRegex(@"^pci\\ven_([0-9a-f]{4})&dev_([0-9a-f]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex PciId();
}
