namespace Drift.Models;

/// <summary>
/// The engine (WebView2) profile a Profile's pages run in, with its logins, cookies and history: one each (Profile.Engine,
/// ProfileData.cs). Its Id names the engine's profile (ProfileIds) and never changes.
/// </summary>
public sealed class EngineProfile(string id)
{
    public string Id { get; } = id;

    public bool IsEngineDefault => ProfileIds.IsEngineDefault(Id);
}
