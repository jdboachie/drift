namespace Drift;

/// <summary>
/// The picture of a Peek's page that stands in for it while it becomes a tab. It's taken while the
/// pointer is on "Open as tab", so the click has nothing to wait for, unless it's old by then.
/// </summary>
public static class PeekFreeze
{
    public const long FreshMs = 1000;

    public static bool IsFresh(long? takenAt, long now) => takenAt is { } taken && now - taken < FreshMs;
}
