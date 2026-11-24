namespace Drift;

/// <summary>Where a page's load has got to, for the loading bar above the page (LoadingBar.cs).</summary>
public enum LoadPhase
{
    /// <summary>Not loading.</summary>
    None,
    /// <summary>Asked for, and waiting for the site to answer.</summary>
    Waiting,
    /// <summary>The new page has come and is being drawn, till it's all loaded.</summary>
    Drawing,
}

/// <summary>
/// Whether a tab's page is loading, for the stop button that stands in for reload meanwhile, and how far it's got,
/// for the loading bar. Navigations can overlap (a second link clicked before the first loads): only the newest one
/// finishing ends the loading.
/// </summary>
public sealed class PageLoad
{
    private ulong? _loading;
    private bool _committed;

    public bool IsLoading => _loading is not null;

    public LoadPhase Phase => _loading is null ? LoadPhase.None : _committed ? LoadPhase.Drawing : LoadPhase.Waiting;

    public void Started(ulong navigation)
    {
        _loading = navigation;
        _committed = false;
    }

    /// <summary>The page loading has come (the engine's ContentLoading).</summary>
    public void Committed()
    {
        if (_loading is not null) _committed = true;
    }

    public void Finished(ulong navigation)
    {
        if (_loading == navigation) _loading = null;
    }

    /// <summary>A fresh page (the tab's engine replaced): nothing loading yet.</summary>
    public void Reset() => _loading = null;
}
