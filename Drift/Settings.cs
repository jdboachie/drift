using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.System;

namespace Drift;

internal enum CloseBehavior
{
    /// <summary>Every tab comes back next time, where you left it.</summary>
    KeepTabs,
    /// <summary>Regular tabs close; Essentials and pinned tabs stay, back on their own pages.</summary>
    KeepPinned,
}

internal enum StartupBehavior
{
    /// <summary>The tab you were on when Drift closed (or the first pinned tab).</summary>
    LastTab,
    /// <summary>The command bar, ready for a new tab.</summary>
    NewTab,
    /// <summary>A page of your choosing, opened in a tab (or switched to, if it's open).</summary>
    Page,
}

/// <summary>A key with its modifiers. Needs Ctrl or Alt, except for the function keys.</summary>
internal readonly record struct Shortcut(VirtualKey Key, bool Ctrl, bool Shift, bool Alt)
{
    public IEnumerable<string> Parts()
    {
        if (Ctrl) yield return "Ctrl";
        if (Alt) yield return "Alt";
        if (Shift) yield return "Shift";
        yield return KeyName(Key) ?? Key.ToString();
    }

    public override string ToString() => string.Join("+", Parts());

    public static Shortcut? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split('+');
        // "Ctrl++" style names never arise: the plus key isn't offered.
        var keyName = parts[^1];
        var key = KeyFromName(keyName);
        if (key is null) return null;
        var mods = parts[..^1];
        return new Shortcut(key.Value, mods.Contains("Ctrl"), mods.Contains("Shift"), mods.Contains("Alt"));
    }

    // The keys a shortcut can use: letters, digits, function keys, Tab and some punctuation.
    private static readonly Dictionary<VirtualKey, string> Punctuation = new()
    {
        [(VirtualKey)188] = ",", [(VirtualKey)190] = ".", [(VirtualKey)191] = "/", [(VirtualKey)186] = ";",
        [(VirtualKey)222] = "'", [(VirtualKey)219] = "[", [(VirtualKey)221] = "]", [(VirtualKey)220] = "\\",
        [(VirtualKey)189] = "-", [(VirtualKey)187] = "=", [(VirtualKey)192] = "`",
    };

    public static string? KeyName(VirtualKey key)
    {
        var code = (int)key;
        if (code is >= 'A' and <= 'Z' or >= '0' and <= '9') return ((char)code).ToString();
        if (key is >= VirtualKey.F1 and <= VirtualKey.F12) return "F" + (code - (int)VirtualKey.F1 + 1);
        if (key == VirtualKey.Tab) return "Tab";
        return Punctuation.TryGetValue(key, out var name) ? name : null;
    }

    private static VirtualKey? KeyFromName(string name)
    {
        if (name.Length == 1 && (char.IsAsciiLetterUpper(name[0]) || char.IsAsciiDigit(name[0]))) return (VirtualKey)name[0];
        if (name == "Tab") return VirtualKey.Tab;
        if (name.Length is 2 or 3 && name[0] == 'F' && int.TryParse(name[1..], out var f) && f is >= 1 and <= 12)
            return VirtualKey.F1 + (f - 1);
        foreach (var (key, text) in Punctuation) if (text == name) return key;
        return null;
    }

    public bool IsFunctionKey => Key is >= VirtualKey.F1 and <= VirtualKey.F12;
}

internal sealed record ShortcutAction(string Id, string Name, Shortcut Default);

internal sealed class SettingsData
{
    public CloseBehavior OnClose { get; set; } = CloseBehavior.KeepTabs;
    public StartupBehavior OnStartup { get; set; } = StartupBehavior.LastTab;
    public string StartupPage { get; set; } = "";
    /// <summary>Ad blocking, with uBlock Origin Lite (Extensions).</summary>
    public bool BlockAds { get; set; } = true;

    /// <summary>Leaving a tab with a video playing floats it in its own window (Pip.cs).</summary>
    public bool AutoPictureInPicture { get; set; } = true;
    /// <summary>Ctrl+Tab goes by the tabs used last (TabSwitcher.cs); off, down the sidebar.</summary>
    public bool CtrlTabRecentOrder { get; set; } = true;
    /// <summary>Offer to save passwords, and fill them in (WebView2's password manager).</summary>
    public bool SavePasswords { get; set; } = true;
    /// <summary>Fill in addresses and other form data seen before.</summary>
    public bool Autofill { get; set; } = true;
    /// <summary>The installed uBlock Origin Lite, to find it again.</summary>
    public string? AdBlockExtensionId { get; set; }
    /// <summary>Action id → shortcut, for the ones changed from their defaults.</summary>
    public Dictionary<string, string> Shortcuts { get; set; } = new();
}

/// <summary>
/// Drift's settings, in %LOCALAPPDATA%\Drift\settings.json. Changes save straight away and raise
/// Changed, which the main window listens to (its shortcuts, for one, follow along).
/// </summary>
internal static class Settings
{
    public static readonly ShortcutAction[] Actions =
    {
        new("newtab", "New tab", new Shortcut(VirtualKey.T, true, false, false)),
        new("closetab", "Close tab", new Shortcut(VirtualKey.W, true, false, false)),
        new("reopen", "Reopen closed tab", new Shortcut(VirtualKey.T, true, true, false)),
        new("folder", "New folder", new Shortcut(VirtualKey.N, true, false, true)),
        new("nexttab", "Next tab", new Shortcut(VirtualKey.Tab, true, false, false)),
        new("prevtab", "Previous tab", new Shortcut(VirtualKey.Tab, true, true, false)),
        new("address", "Edit address", new Shortcut(VirtualKey.L, true, false, false)),
        new("copyurl", "Copy link", new Shortcut(VirtualKey.C, true, true, false)),
        new("private", "New private window", new Shortcut(VirtualKey.N, true, true, false)),
        new("downloads", "Downloads", new Shortcut(VirtualKey.J, true, false, false)),
        new("history", "History", new Shortcut(VirtualKey.H, true, false, false)),
        new("sidebar", "Show or hide sidebar", new Shortcut(VirtualKey.S, true, false, false)),
        new("settings", "Settings", new Shortcut((VirtualKey)188, true, false, false)),
    };

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Drift", "settings.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static SettingsData Current { get; private set; } = Load();

    public static event Action? Changed;

    public static Shortcut ShortcutFor(ShortcutAction action) =>
        Current.Shortcuts.TryGetValue(action.Id, out var text) && Shortcut.Parse(text) is { } custom ? custom : action.Default;

    public static ShortcutAction? ActionFor(Shortcut shortcut) => Actions.FirstOrDefault(a => ShortcutFor(a) == shortcut);

    public static void Update(Action<SettingsData> change)
    {
        change(Current);
        Save();
        Changed?.Invoke();
    }

    public static void SetShortcut(ShortcutAction action, Shortcut? shortcut) => Update(s =>
    {
        if (shortcut is null || shortcut == action.Default) s.Shortcuts.Remove(action.Id);
        else s.Shortcuts[action.Id] = shortcut.Value.ToString();
    });

    private static SettingsData Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(FilePath), Json) ?? new();
        }
        catch
        {
            // A damaged file: back to the defaults.
        }
        return new();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, Json));
        }
        catch { }
    }
}
