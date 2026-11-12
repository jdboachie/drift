namespace Drift;

/// <summary>
/// Closing Drift takes two presses, as Arc and Chrome's "warn before quitting" do: the first only
/// says to press again (Confirm.cs), and a second soon after closes.
/// </summary>
public sealed class CloseAgain
{
    public const int WindowMs = 2500;

    private long? _first;

    /// <summary>True if this press closes; false if it's the first, and another is wanted.</summary>
    public bool Press(long nowMs)
    {
        if (IsWaiting(nowMs))
        {
            _first = null;
            return true;
        }
        _first = nowMs;
        return false;
    }

    /// <summary>A first press has been made, and another now would close.</summary>
    public bool IsWaiting(long nowMs) => _first is { } first && nowMs - first <= WindowMs;
}
