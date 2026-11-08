using System;
using System.Threading.Tasks;

namespace Drift;

/// <summary>A step that might never finish (the engine, an image decode): its result, or the fallback once time's up or it fails.</summary>
public static class Deadline
{
    public static async Task<T> Within<T>(Task<T> step, TimeSpan time, T fallback)
    {
        if (await Task.WhenAny(step, Task.Delay(time)) != step) return fallback;
        try { return await step; }
        catch { return fallback; }
    }
}
