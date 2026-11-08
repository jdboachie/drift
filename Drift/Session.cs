using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Drift;

internal sealed class SessionTab
{
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public bool Pinned { get; set; }
    /// <summary>"Essential", "Pinned" or "Regular". Missing in files from before Essentials: Pinned meant a tile.</summary>
    public string? Place { get; set; }
    /// <summary>For pinned tabs, the page they're pinned to.</summary>
    public string? Home { get; set; }
    /// <summary>For closed tabs, where in its group it was, to put it back there.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Index { get; set; }
    /// <summary>For a tab in a folder: the folder's Id, and where it is among the folder's contents.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Folder { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Position { get; set; }
    /// <summary>
    /// Split view, as saved before splits had rows of their own (SessionData.Splits): this tab is the
    /// right-hand page beside the tab before it, the left taking SplitRatio of the width. Read, not written.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SplitWithPrevious { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? SplitRatio { get; set; }
}

public sealed class SessionFolder
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>The folder it's nested in; null at the top.</summary>
    public string? Parent { get; set; }
    public bool Expanded { get; set; } = true;
    /// <summary>Where it is among its parent's contents (or among the top-level folders).</summary>
    public int Position { get; set; }
}

/// <summary>A split view: its two tabs (indices into SessionData.Tabs) and its row in the tab list.</summary>
internal sealed class SessionSplit
{
    public int Left { get; set; }
    public int Right { get; set; }
    public int Row { get; set; }
    /// <summary>The left page's share of the width.</summary>
    public double Ratio { get; set; } = 0.5;
}

internal sealed class SessionData
{
    public List<SessionTab> Tabs { get; set; } = new();
    /// <summary>Folders, parents before the folders nested in them.</summary>
    public List<SessionFolder> Folders { get; set; } = new();
    /// <summary>Index into Tabs of the tab that was in front; −1 for none.</summary>
    public int Active { get; set; } = -1;
    public List<SessionSplit> Splits { get; set; } = new();
    /// <summary>Recently closed tabs, oldest first, for Reopen closed tab.</summary>
    public List<SessionTab> Closed { get; set; } = new();
}

/// <summary>
/// The open tabs, in %LOCALAPPDATA%\Drift\session.json, for the next launch. Written a couple of
/// seconds after tabs change (so a crash loses little) and when the window closes.
/// </summary>
internal static class Session
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Drift", "session.json");

    /// <summary>Null on first launch (no file yet), so the default pinned tabs open instead.</summary>
    public static SessionData? Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<SessionData>(File.ReadAllText(FilePath)) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <param name="wait">True when the app is closing, so the write finishes before it exits.</param>
    public static void Save(SessionData data, bool wait)
    {
        var json = JsonSerializer.Serialize(data);
        void Write()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var temp = FilePath + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, FilePath, overwrite: true);
            }
            catch { }
        }
        if (wait) Write();
        else _ = Task.Run(Write);
    }
}
