using System;
using System.Collections.Generic;

namespace Drift;

/// <summary>
/// When a live folder asks GitHub again (LiveFolders.cs). New pull requests should show soon, so:
/// <list type="bullet">
/// <item>every minute while Drift is in front, every five behind other windows;</item>
/// <item>on coming to the front, if the last answer is over 30 seconds old;</item>
/// <item>after a pull request is made, at 10, 30 and 90 seconds: GitHub's search takes a while to catch
/// up with it, so asking only once, straight away, could miss it;</item>
/// <item>after any other page on GitHub, within a minute of the last answer;</item>
/// <item>an empty answer is asked again within a minute before it's believed (LiveFolder.Believe), and a
/// failed one waits two, in front or not.</item>
/// </list>
/// </summary>
public sealed class LiveSchedule
{
    public static readonly TimeSpan InFront = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan Behind = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan Retry = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan Doubt = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan Fresh = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan[] AfterAction = [TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(90)];

    private DateTimeOffset _fetched = DateTimeOffset.MinValue;
    private DateTimeOffset _next = DateTimeOffset.MinValue;
    private DateTimeOffset _nextInFront = DateTimeOffset.MinValue;
    private readonly List<DateTimeOffset> _followUps = [];

    public bool Due(DateTimeOffset now, bool inFront) =>
        now >= _next || (inFront && now >= _nextInFront) || _followUps.Exists(t => now >= t);

    /// <summary>An ask finished: answered or not, and if answered, whether the answer was doubted (empty).</summary>
    public void Fetched(DateTimeOffset now, bool answered, bool doubted)
    {
        _fetched = now;
        // Follow-ups this ask has covered go, however many fell due while it was out.
        _followUps.RemoveAll(t => t <= now);
        _next = now + (!answered ? Retry : doubted ? Doubt : Behind);
        _nextInFront = answered && !doubted ? now + InFront : _next;
    }

    /// <summary>A page on GitHub: something may have been done there (a review, a merge), so asked within a minute of the last answer.</summary>
    public void Visited(DateTimeOffset now)
    {
        var soon = _fetched + Doubt;
        if (soon < _next) _next = soon;
    }

    /// <summary>A pull request was just made: asked again as GitHub's search catches up.</summary>
    public void Acted(DateTimeOffset now)
    {
        _followUps.Clear();
        foreach (var after in AfterAction) _followUps.Add(now + after);
    }

    /// <summary>Drift's window came to the front.</summary>
    public void CameToFront(DateTimeOffset now)
    {
        if (now - _fetched >= Fresh) _next = now;
    }

    /// <summary>Asked for now: the folder just made, or Refresh.</summary>
    public void Now() => _next = DateTimeOffset.MinValue;
}
