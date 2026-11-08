using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Drift.Models;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Drift;

/// <summary>
/// The command bar's commands, as in Arc (its list of Command Bar actions, without the ones about
/// what Drift doesn't have: Spaces, notes, Little Arc, the archive). Typing part of a command's name
/// offers it among the results (CommandMatch), with the tab or folder it's for in a chip and its
/// shortcut at the end: "pin to read" offers Pin to [Reading List]. Only commands that make sense
/// now are offered: Unpin Tab for a pinned tab, Go Back when there's somewhere to go back to.
/// </summary>
public sealed partial class MainWindow
{
    /// <param name="Name">What it says, and what's matched: "Pin to", "Developer Tools".</param>
    /// <param name="Target">The tab or folder it's for, shown in a chip after the name, and matched too.</param>
    /// <param name="ShortcutId">The Settings action whose shortcut it shows.</param>
    private sealed record CommandAction(string Name, string Glyph, Action Run,
        string? Target = null, string? ShortcutId = null, string[]? Keywords = null)
    {
        public string FullName => Target is null ? Name : $"{Name} {Target}";
    }

    // "Add Split View": the command bar again, and what's opened from it goes beside this tab.
    private BrowserTab? _commandSplitWith;

    /// <summary>The commands that apply right now, to the tab in front.</summary>
    private IEnumerable<CommandAction> AvailableActions()
    {
        var tab = _active;
        var core = tab?.View.CoreWebView2;
        var page = core is not null && !string.IsNullOrEmpty(core.Source) && core.Source != "about:blank";

        // ── Tabs ──
        if (tab is not null)
        {
            if (page) yield return new("Copy Link", "", () => { if (CopyCurrentLink()) ShowCopiedToast(); }, ShortcutId: "copyurl", Keywords: ["url", "address", "current"]);
            yield return new("Close Tab", "", CloseCurrent, ShortcutId: "closetab");
            if (page) yield return new("Duplicate Tab", "", DuplicateTab, Keywords: ["copy", "clone"]);
            if (tab.Place == TabPlace.Regular) yield return new("Pin Tab", "", () => MoveTab(tab, TabPlace.Pinned));
            if (tab.Place is TabPlace.Pinned or TabPlace.Folder) yield return new("Unpin Tab", "", () => MoveTab(tab, TabPlace.Regular));
            if (tab.Place != TabPlace.Essential) yield return new("Add to Essentials", "", () => MoveTab(tab, TabPlace.Essential), Keywords: ["favorite", "favourite"]);
            else yield return new("Remove from Essentials", "", () => MoveTab(tab, TabPlace.Pinned), Keywords: ["unfavorite", "unpin"]);
            foreach (var folder in _folderTree.AllFolders.Where(f => tab.Folder != f).ToList())
                yield return new("Pin to", "", () => MoveTabToFolder(tab, folder, null), Target: folder.Path, Keywords: ["move", "folder", "add"]);
            yield return new("Pin to New Folder", "", () => NewFolder(null, tab), Keywords: ["move", "add"]);
            if (tab.IsPinned && tab.HomeUri is { } home && page && !SamePage(core!.Source, home))
            {
                yield return new("Reset Tab", "", () => Navigate(tab, home), Keywords: ["pinned", "home", "original"]);
                yield return new("Replace Pin with Current Page", "", () => ReplacePin(tab), Keywords: ["home", "update"]);
            }
            yield return new("Reveal Tab in Sidebar", "", () => RevealInSidebar(tab), Keywords: ["show", "find", "locate"]);
        }
        if (_closedTabs.Count > 0) yield return new("Reopen Closed Tab", "", ReopenClosedTab, ShortcutId: "reopen", Keywords: ["last", "undo"]);
        if (Tabs.Count > 0) yield return new("Clear Tabs", "", ClearTabs, Keywords: ["close", "all", "today"]);
        yield return new("New Folder", "", () => NewFolder(null), ShortcutId: "folder");
        foreach (var folder in _folderTree.AllFolders.ToList())
            yield return new("Open", "", () => OpenFolder(folder), Target: folder.Path, Keywords: ["folder", "show", "expand"]);

        // ── Split view ──
        if (tab is { Split: { } split })
        {
            yield return new("Separate Tabs", "", () => SeparateTabs(split), Keywords: ["remove", "split", "view", "unsplit"]);
            yield return new("Expand Split", "", () => ExpandSplit(split, tab), Keywords: ["only", "current"]);
        }
        else if (tab is not null && !tab.IsSplitRow)
            yield return new("Add Split View", "", () => OpenSplitCommandBar(tab), Keywords: ["side", "beside", "new"]);

        // ── The page ──
        if (page)
        {
            if (tab!.View.CanGoBack) yield return new("Go Back", "", () => tab.View.GoBack(), Keywords: ["history", "previous"]);
            if (tab.View.CanGoForward) yield return new("Go Forward", "", () => tab.View.GoForward(), Keywords: ["history", "next"]);
            yield return new("Reload Page", "", () => tab.View.Reload(), Keywords: ["refresh"]);
            yield return new("Find on Page", "", () => SendPageKeys(VkControl, VkF), Keywords: ["search", "text"]);
            yield return new("Zoom In", "", () => SendPageKeys(VkControl, VkPlus), Keywords: ["bigger", "larger"]);
            yield return new("Zoom Out", "", () => SendPageKeys(VkControl, VkMinus), Keywords: ["smaller"]);
            if (Math.Abs(tab.Zoom - 1) > 0.01) yield return new("Actual Size", "", ResetZoom, Keywords: ["zoom", "reset", "100"]);
            yield return new("Print", "", () => core!.ShowPrintUI(CoreWebView2PrintDialogKind.Browser));
            yield return new("Save Page As", "", () => _ = SavePageAsync(core!), Keywords: ["download"]);
            yield return new("Developer Tools", "", () => core!.OpenDevToolsWindow(), Keywords: ["devtools", "inspect", "console"]);
        }

        // ── New documents, as Arc does: their sites' .new addresses ──
        foreach (var (name, url) in NewDocuments)
            yield return new(name, "", () => OpenInNewTab(new Uri(url)), Keywords: ["create"]);

        // ── Drift ──
        yield return new(_sidebarHidden ? "Show Sidebar" : "Hide Sidebar", "", () => SetSidebarHidden(!_sidebarHidden), ShortcutId: "sidebar", Keywords: ["toggle"]);
        yield return new("New Private Window", "", OpenPrivateWindow, ShortcutId: "private", Keywords: ["incognito"]);
        yield return new("Downloads", "", () => ToggleLibrary("downloads"), ShortcutId: "downloads", Keywords: ["library"]);
        yield return new("History", "", () => ToggleLibrary("history"), ShortcutId: "history", Keywords: ["library", "view"]);
        var fullScreen = AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;
        yield return new(fullScreen ? "Exit Full Screen" : "Enter Full Screen", fullScreen ? "" : "", ToggleFullScreen, Keywords: ["fullscreen"]);
        yield return new("Minimize Window", "", Minimize);
        yield return new("Close Window", "", Close, Keywords: ["quit", "exit"]);
        yield return new("Settings", "", () => SettingsWindow.Open(this), ShortcutId: "settings", Keywords: ["preferences", "options"]);
        yield return new("Keyboard Shortcuts", "", () => SettingsWindow.Open(this, "shortcuts"), Keywords: ["keys"]);
        yield return new("Manage Extensions", "", () => SettingsWindow.Open(this, "extensions"));
        yield return new("Add Extension", "", () => OpenInNewTab(new Uri("https://chromewebstore.google.com/")), Keywords: ["chrome", "web", "store"]);
        if (!DefaultBrowser.IsDefault) yield return new("Set as Default Browser", "", SetAsDefaultBrowser);
    }

    private static readonly (string Name, string Url)[] NewDocuments =
    [
        ("New Google Doc", "https://docs.new"),
        ("New Google Sheet", "https://sheets.new"),
        ("New Google Slides", "https://slides.new"),
        ("New Google Form", "https://forms.new"),
        ("New Notion Page", "https://notion.new"),
        ("New Figma File", "https://figma.new"),
    ];

    private static bool SamePage(string url, Uri home) => Normalize(url) == Normalize(home.ToString());

    /// <summary>The commands what's typed names, best first: a few, so they don't crowd out the pages.</summary>
    private List<(CommandAction Action, bool Strong)> MatchingActions(string query)
    {
        if (_commandSplitWith is not null) return new();
        return AvailableActions()
            .Select(a => (Action: a, Score: CommandMatch.Score(query, a.FullName, a.Keywords)))
            .Where(m => m.Score >= 0)
            .OrderByDescending(m => m.Score)
            .Take(3)
            .Select(m => (m.Action, CommandMatch.IsStrong(query, m.Action.FullName, m.Action.Keywords)))
            .ToList();
    }

    // ── What the commands do, where Drift didn't already have it ──

    private void DuplicateTab()
    {
        if (_active?.Url is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri)) OpenInNewTab(uri);
    }

    /// <summary>A pinned tab's own page becomes the one it's on now.</summary>
    private void ReplacePin(BrowserTab tab)
    {
        if (!Uri.TryCreate(tab.Url, UriKind.Absolute, out var here)) return;
        tab.HomeUri = here;
        SaveSessionSoon();
    }

    /// <summary>Every tab in the tab list closes (Essentials, pinned and folder tabs stay), as Arc's Clear does.</summary>
    private void ClearTabs()
    {
        foreach (var row in Tabs.ToList())
        {
            if (row is { IsSplitRow: true, Split: { } split }) CloseSplit(split);
            else if (Tabs.Contains(row)) RemoveTab(row);
        }
    }

    /// <summary>The sidebar shows, with the tab's folders open, and its row scrolled into sight.</summary>
    private void RevealInSidebar(BrowserTab tab)
    {
        if (_sidebarHidden) SetSidebarHidden(false);
        for (var folder = tab.Folder; folder is not null; folder = folder.Parent) _folderTree.SetExpanded(folder, true);
        RefreshFolderRows();
        SaveSessionSoon();
        var list = tab.Place switch
        {
            TabPlace.Essential => (ListViewBase)EssentialList,
            TabPlace.Pinned => PinnedList,
            TabPlace.Folder => FolderList,
            _ => TabList,
        };
        object item = tab.Split?.Row ?? tab;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            (list.ContainerFromItem(item) as UIElement)?.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.5 }));
    }

    /// <summary>A folder open in the sidebar (and the folders it's in), scrolled into sight.</summary>
    private void OpenFolder(TabFolder folder)
    {
        if (_sidebarHidden) SetSidebarHidden(false);
        for (var at = folder; at is not null; at = at.Parent) _folderTree.SetExpanded(at, true);
        RefreshFolderRows();
        SaveSessionSoon();
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            (FolderList.ContainerFromItem(folder) as UIElement)?.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.3 }));
    }

    /// <summary>Only this half stays in view: the split comes apart, and you stay on it.</summary>
    private void ExpandSplit(TabSplit split, BrowserTab keep)
    {
        SeparateTabs(split);
        Activate(keep);
    }

    /// <summary>The command bar again, for the page to open beside this tab: a new page, or a tab you have.</summary>
    private void OpenSplitCommandBar(BrowserTab with)
    {
        OpenCommandBar(newTab: true, animate: false);
        _commandSplitWith = with;
        CommandBox.PlaceholderText = "Open beside this tab…";
        UpdateCommandResults();
    }

    /// <summary>A page opened from "Add Split View" goes on the right of the tab it was for.</summary>
    private void SplitBeside(BrowserTab with, BrowserTab tab)
    {
        if (!AllTabs.Contains(with) || !CanSplit(with, tab))
        {
            Activate(tab);
            return;
        }
        var width = PageColumns.ActualWidth;
        SplitTabs(with, tab, front: tab);
        AnimateSplitIn(SplitSide.Right, width, tab);
    }

    private static async Task SavePageAsync(CoreWebView2 core)
    {
        try { await core.ShowSaveAsUIAsync(); }
        catch { }
    }

    private void ToggleFullScreen()
    {
        var full = AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;
        AppWindow.SetPresenter(full ? AppWindowPresenterKind.Default : AppWindowPresenterKind.FullScreen);
    }

    private void Minimize()
    {
        if (AppWindow.Presenter is OverlappedPresenter overlapped) overlapped.Minimize();
    }

    private static void SetAsDefaultBrowser()
    {
        try
        {
            DefaultBrowser.Register();
            DefaultBrowser.OpenSystemSettings();
        }
        catch { }
    }
}
