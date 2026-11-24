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
    /// <summary>The name you gave it (Rename…), shown in place of Title.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }
    /// <summary>The icon you chose for it (Change icon…): an emoji, or a symbol (CustomIcon).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Icon { get; set; }
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
    /// <summary>For closed tabs, when it was closed (UTC), for the library's Closed tabs. Missing from older files.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? Closed { get; set; }
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
    /// <summary>The icon you chose for it (Change icon…): an emoji, or a symbol (CustomIcon).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Icon { get; set; }
    /// <summary>The folder it's nested in; null at the top.</summary>
    public string? Parent { get; set; }
    public bool Expanded { get; set; } = true;
    /// <summary>Where it is among its parent's contents (or among the top-level folders).</summary>
    public int Position { get; set; }
    /// <summary>A live folder's kind (LiveSource), and the keys of what was closed in it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Live { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Dismissed { get; set; }
    /// <summary>A live folder's menu: searches turned off, drafts shown, repositories hidden (LiveSource).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Off { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Drafts { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? HiddenRepos { get; set; }
    /// <summary>A live folder that has been filled once (LiveSource.Filled).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Filled { get; set; }
}

/// <summary>A split view: its two tabs (indices into SessionData.Tabs) and its row in the tab list.</summary>
internal sealed class SessionSplit
{
    /// <summary>Its tabs, left to right (indices into SessionData.Tabs).</summary>
    public List<int>? Pages { get; set; }
    /// <summary>Each page's share of the width.</summary>
    public List<double>? Shares { get; set; }
    public int Row { get; set; }

    // As saved before a split could have more than two pages: read, not written.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Left { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Right { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Ratio { get; set; }

    public int[] PageList() => Pages is { Count: > 0 } ? Pages.ToArray() : [Left ?? -1, Right ?? -1];

    public double[]? ShareList()
    {
        if (Pages is { Count: > 0 }) return Shares?.ToArray();
        var ratio = Ratio ?? 0.5;
        return [ratio, 1 - ratio];
    }
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
    /// <summary>The docked sidebar's width, as last dragged; null for the default.</summary>
    public double? SidebarWidth { get; set; }
    /// <summary>The pull requests folder has been made for you once, so it isn't again (LiveFolders.cs).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool PullsFolderMade { get; set; }
}

/// <summary>
/// The open tabs, in %LOCALAPPDATA%\Drift\session.json, for the next launch. Written a couple of
/// seconds after tabs change (so a crash loses little) and when the window closes.
/// </summary>
internal static class Session
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Drift", "session.json");

    // The file as Drift started, read on another thread while the window was being made (ReadSoon).
    private static Task<SessionData?>? _reading;

    /// <summary>Starts reading the session on another thread, for the next Load to pick up.</summary>
    public static void ReadSoon() => _reading ??= Task.Run(Read);

    /// <summary>Null on first launch (no file yet), so the default pinned tabs open instead.</summary>
    public static SessionData? Load()
    {
        var reading = _reading;
        _reading = null;
        return reading is not null ? reading.Result : Read();
    }

    private static SessionData? Read()
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
