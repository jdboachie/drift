using System;

namespace Drift;

/// <summary>
/// What the library's history list was built from. Building it is slow (half a second for a
/// page of rows), so it's only rebuilt when something it shows has changed: a visit (Version), the
/// search, how many rows, the day (Today, Yesterday), or light/dark. No UI here, so it can be tested.
/// </summary>
public readonly record struct HistoryListState(int Version, string Query, int Rows, DateTime Today, bool Dark)
{
    public bool NeedsRebuild(HistoryListState now) => now != this;

    /// <summary>Whether the list must be built now, given what (if anything) it was last built from.</summary>
    public static bool Rebuild(HistoryListState? shown, HistoryListState now) => shown is not { } was || was.NeedsRebuild(now);
}
