using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Drift;

/// <summary>
/// A tab's pictures of the pages it's left, by their place in its history (DevTools' Page.getNavigationHistory ids),
/// so a two-finger swipe can show the page it's going back or forward to underneath, as Safari does. Where the tab is
/// in its history comes from the same call. Only the newest few are kept, and only while Drift runs.
/// </summary>
public sealed class HistoryPictures
{
    /// <summary>How many pages back and forth a tab keeps pictures of.</summary>
    public const int MostKept = 10;

    private readonly List<(int Id, byte[] Picture)> _kept = new();

    /// <summary>The page the tab's on, and those back and forward from it, if there are any.</summary>
    public int? Current { get; private set; }
    public int? Back { get; private set; }
    public int? Forward { get; private set; }

    /// <summary>Where the tab is in its history, from Page.getNavigationHistory's reply.</summary>
    public void Read(string reply)
    {
        Current = Back = Forward = null;
        try
        {
            using var document = JsonDocument.Parse(reply);
            var root = document.RootElement;
            var at = root.GetProperty("currentIndex").GetInt32();
            var ids = root.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("id").GetInt32()).ToList();
            if (at < 0 || at >= ids.Count) return;
            Current = ids[at];
            if (at > 0) Back = ids[at - 1];
            if (at + 1 < ids.Count) Forward = ids[at + 1];
        }
        catch (JsonException) { }
        catch (KeyNotFoundException) { }
        catch (System.InvalidOperationException) { }
    }

    /// <summary>A page's picture, as it was left; the newest replaces the oldest once there are MostKept.</summary>
    public void Keep(int id, byte[] picture)
    {
        _kept.RemoveAll(k => k.Id == id);
        _kept.Add((id, picture));
        if (_kept.Count > MostKept) _kept.RemoveAt(0);
    }

    public byte[]? Of(int? id) => id is { } wanted ? _kept.FirstOrDefault(k => k.Id == wanted).Picture : null;

    /// <summary>
    /// The tab's page as it last looked, and where: shown, faded, while the tab loads it again after being unloaded
    /// (SwipePage.cs). It outlives the tab's engine; its pages don't.
    /// </summary>
    public (byte[] Picture, string Url)? LastLook { get; set; }

    /// <summary>Its last look, if it was taken on the page the tab's about to load.</summary>
    public byte[]? LastLookAt(string? url) => url is not null && LastLook is { } look && look.Url == url ? look.Picture : null;

    /// <summary>A new engine has a new history: the pages' pictures and places go.</summary>
    public void ForgetPages()
    {
        _kept.Clear();
        Current = Back = Forward = null;
    }
}
