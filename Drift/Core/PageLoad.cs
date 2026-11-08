namespace Drift;

/// <summary>
/// Whether a tab's page is loading, for the stop button that stands in for reload meanwhile.
/// Navigations can overlap (a second link clicked before the first loads): only the newest one
/// finishing ends the loading.
/// </summary>
public sealed class PageLoad
{
    private ulong? _loading;

    public bool IsLoading => _loading is not null;

    public void Started(ulong navigation) => _loading = navigation;

    public void Finished(ulong navigation)
    {
        if (_loading == navigation) _loading = null;
    }

    /// <summary>A fresh page (the tab's engine replaced): nothing loading yet.</summary>
    public void Reset() => _loading = null;
}
