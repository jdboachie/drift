using System;
using System.Collections.Generic;

namespace Drift;

public enum GpuFailure
{
    /// <summary>The same failure, reported again by another page.</summary>
    Repeat,
    /// <summary>A new failure, not (or no longer) enough to act on.</summary>
    Counted,
    /// <summary>Failing again and again: time to stop asking the GPU to decode video.</summary>
    TooMany,
}

/// <summary>
/// The engine's GPU process failing again and again, as one does when its driver hangs decoding
/// video (HEVC on an Intel HD 530): every page freezes until Chromium's watchdog restarts it.
/// Two failures within half an hour, and video moves to the CPU (MainWindow.GpuProcessFailed).
/// </summary>
public sealed class GpuFailures
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(30);
    /// <summary>Every page reports the GPU process's exit: reports this close together are one failure.</summary>
    public static readonly TimeSpan SameFailure = TimeSpan.FromSeconds(10);
    public const int Limit = 2;

    private readonly List<DateTime> _failures = [];
    private bool _tooMany;

    public GpuFailure Report(DateTime now)
    {
        if (_failures.Count > 0 && now - _failures[^1] < SameFailure) return GpuFailure.Repeat;
        _failures.Add(now);
        _failures.RemoveAll(failed => now - failed > Window);
        if (_tooMany || _failures.Count < Limit) return GpuFailure.Counted;
        _tooMany = true;
        return GpuFailure.TooMany;
    }

    /// <summary>
    /// Whether the engine decodes video on the CPU: it does on the runtime it fell back on. A new
    /// runtime may have fixed it, so it gets the GPU again.
    /// </summary>
    public static bool DecodeOnCpu(string? fellBackOn, string? runtime) => fellBackOn is not null && fellBackOn == runtime;
}
