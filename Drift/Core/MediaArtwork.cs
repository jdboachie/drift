using System;
using System.Collections.Generic;
using System.Linq;

namespace Drift;

/// <summary>
/// Which of the artwork a page lists for what's playing (navigator.mediaSession's metadata: an album
/// cover, a video's thumbnail) the mini player shows. No UI here, so it can be tested.
/// </summary>
public static class MediaArtwork
{
    /// <summary>
    /// The smallest at least as big as wanted (sharp, without loading a huge one), else the biggest;
    /// one whose size isn't given only when nothing says. Only web addresses: a blob or data one lives
    /// in the page and can't be loaded outside it.
    /// </summary>
    public static string? Pick(IEnumerable<(string Src, string? Sizes)> artwork, int wanted)
    {
        var loadable = artwork
            .Where(a => Uri.TryCreate(a.Src, UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp))
            .Select(a => (a.Src, Size: Largest(a.Sizes)))
            .ToList();
        var sized = loadable.Where(a => a.Size > 0).ToList();
        if (sized.Count == 0) return loadable.Select(a => a.Src).FirstOrDefault();
        var bigEnough = sized.Where(a => a.Size >= wanted).OrderBy(a => a.Size).ToList();
        return bigEnough.Count > 0 ? bigEnough[0].Src : sized.MaxBy(a => a.Size).Src;
    }

    /// <summary>"96x96 512x512" → 512; "any" (it scales) → as big as can be; nothing → 0.</summary>
    private static int Largest(string? sizes)
    {
        var largest = 0;
        foreach (var size in (sizes ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (size.Equals("any", StringComparison.OrdinalIgnoreCase)) return int.MaxValue;
            var x = size.IndexOfAny(['x', 'X']);
            if (x > 0 && int.TryParse(size[..x], out var width) && int.TryParse(size[(x + 1)..], out var height))
                largest = Math.Max(largest, Math.Min(width, height));
        }
        return largest;
    }
}
