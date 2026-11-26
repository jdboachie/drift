using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Drift.Models;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;

namespace Drift;

/// <summary>
/// A tab's right-click menu, the same for every kind of tab (Essentials, folder, pinned, the tab
/// list), with what fits each, after Arc's and Zen's, and like Arc's, by name only:
/// <list type="bullet">
/// <item>Copy link (Alt held as it opens: as a Markdown link); Rename… (in its row); Change icon…; Mute.</item>
/// <item>A pinned tab's page: back to it, or the page it's on becomes it.</item>
/// <item>Split, Duplicate, and where it lives (pinned, Essentials, a folder).</item>
/// <item>Reload, Bookmark; Unload a pinned tab, Close, and Close tabs › (below it, the others, duplicates).</item>
/// <item>A split's row: a submenu for each of its pages, Duplicate split, Separate tabs, Close tabs.</item>
/// </list>
/// </summary>
public sealed partial class MainWindow
{
    internal void TabMenu_Opening(object? sender, object e)
    {
        if (sender is not MenuFlyout menu) return;
        menu.Items.Clear();
        if (menu.Target is not FrameworkElement { DataContext: BrowserTab tab }) return;
        // One of several picked (Picks.cs): what's done with them all. Any other tab: just that one, and the picks go.
        if (_picked.Count > 1 && _picked.Contains(tab))
        {
            FillPickedMenu(menu);
            return;
        }
        ClearPicks();
        if (tab is { IsSplitRow: true, Split: { } split }) FillSplitRowMenu(menu, split);
        else FillTabMenu(menu, tab);
    }

    /// <summary>Several tabs picked: into a new folder together, or a folder there is, in the sidebar's order.</summary>
    private void FillPickedMenu(MenuFlyout menu)
    {
        var tabs = TabPicks.InOrder(_picked, PickableTabs());
        var folders = _folderTree.AllFolders.Where(f => !f.IsLive).ToList();
        void Into(TabFolder folder)
        {
            ClearPicks();
            foreach (var tab in tabs) MoveTabToFolder(tab, folder, null);
        }
        FillMenu(menu,
        [
            Command($"Add {tabs.Count} tabs to new tab group", () => { ClearPicks(); NewFolder(null, [.. tabs]); }),
            folders.Count == 0 ? null : Submenu($"Add {tabs.Count} tabs to tab group",
                folders.Select(f => (MenuFlyoutItemBase)Command(f.Path, () => Into(f), enabled: tabs.Any(t => t.Folder != f))).ToList()),
        ]);
    }

    /// <summary>Grouped as Arc's: its link; naming it; its pinned page; opening it elsewhere; where it lives; closing it.</summary>
    private void FillTabMenu(MenuFlyout menu, BrowserTab tab)
    {
        // The row it's on, for the icon picker: the menu forgets it once it closes.
        var row = menu.Target as FrameworkElement;
        var loaded = tab.View.CoreWebView2 is not null && !tab.IsUnloaded;
        var link = LinkOf(tab);
        var markdown = IsAltDown();
        var awayFromHome = tab.IsPinned && tab.HomeUri is { } home && tab.Url is { } url && !SamePage(url, home);

        FillMenu(menu,
        [
            link is null ? null : Command(markdown ? "Copy link as Markdown" : "Copy link", () => CopyTabLink(tab, markdown)),
        ],
        [
            tab.Place == TabPlace.Essential ? null : Command("Rename", () => AfterMenu(menu, () => BeginTabRename(tab))),
            Command("Change icon", () => AfterMenu(menu, () => PickTabIcon(row, tab))),
            loaded ? Command(tab.IsMuted ? "Unmute tab" : "Mute tab", () => ToggleMute(tab)) : null,
        ],
        [
            awayFromHome ? Submenu("Edit pinned page",
            [
                Command("Go to pinned page", () => Navigate(tab, tab.HomeUri!)),
                Command("Replace pinned page", () => ReplacePin(tab), enabled: loaded),
            ]) : null,
        ],
        [
            _active is { } current && CanGoBeside(current, tab) ? Command("Split with current tab", () => SplitWithCurrent(tab)) : null,
            link is null ? null : Command("Duplicate", () => _ = DuplicateTabAsync(tab)),
            loaded ? Command("Reload", () => tab.View.Reload()) : null,
            BookmarkItem(tab),
        ],
        PlaceItems(tab),
        [
            tab.IsPinned && loaded ? Command("Unload tab", () => _ = UnloadFromMenuAsync(tab)) : null,
            Command("Close tab", () => _ = CloseFromMenuAsync(tab)),
            RowOf(tab) is { } tabRow ? CloseTabsSubmenu(tabRow) : null,
        ]);
    }

    /// <summary>Where it lives: pin it, make it an Essential, put it in a folder, or back out.</summary>
    private IEnumerable<MenuFlyoutItemBase?> PlaceItems(BrowserTab tab)
    {
        switch (tab.Place)
        {
            case TabPlace.Regular:
                yield return Command("Pin tab", () => MoveTab(tab, TabPlace.Pinned));
                yield return Command("Add to Essentials", () => MoveTab(tab, TabPlace.Essential));
                yield return FolderSubmenu(tab, "Add to tab group");
                yield return ProfileSubmenu(tab);
                break;
            case TabPlace.Pinned:
                yield return Command("Add to Essentials", () => MoveTab(tab, TabPlace.Essential));
                yield return FolderSubmenu(tab, "Add to tab group");
                yield return ProfileSubmenu(tab);
                yield return Command("Unpin tab", () => MoveTab(tab, TabPlace.Regular));
                break;
            // In an unpinned group: one of your other tabs, which can be pinned, or taken out among them.
            case TabPlace.Folder when !tab.IsPinned:
                yield return FolderSubmenu(tab, "Move to tab group");
                yield return ProfileSubmenu(tab);
                yield return Command("Remove from tab group", () => MoveTab(tab, TabPlace.Regular));
                yield return Command("Pin tab", () => MoveTab(tab, TabPlace.Pinned));
                yield return Command("Add to Essentials", () => MoveTab(tab, TabPlace.Essential));
                break;
            case TabPlace.Folder:
                yield return FolderSubmenu(tab, "Move to tab group");
                yield return ProfileSubmenu(tab);
                yield return Command("Remove from tab group", () => MoveTab(tab, TabPlace.Pinned));
                yield return Command("Add to Essentials", () => MoveTab(tab, TabPlace.Essential));
                yield return Command("Unpin tab", () => MoveTab(tab, TabPlace.Regular));
                break;
            case TabPlace.Essential:
                yield return Command("Move to pinned tabs", () => MoveTab(tab, TabPlace.Pinned));
                yield return FolderSubmenu(tab, "Add to tab group");
                yield return Command("Remove from Essentials", () => MoveTab(tab, TabPlace.Regular));
                break;
        }
    }

    /// <summary>Its folders, and a new one: at the top, or (for a tab in a folder) inside its own.</summary>
    private MenuFlyoutSubItem FolderSubmenu(BrowserTab tab, string text)
    {
        var items = new List<MenuFlyoutItemBase> { Command("New tab group", () => NewFolder(null, tab)) };
        if (tab.Folder is { } own && FolderTree.CanHoldSubfolder(own)) items.Add(Command("New tab group inside", () => NewFolder(own, tab)));
        var folders = _folderTree.AllFolders.Where(f => !f.IsLive).ToList();
        if (folders.Count > 0) items.Add(new MenuFlyoutSeparator());
        foreach (var folder in folders)
            items.Add(Command(folder.Path, () => MoveTabToFolder(tab, folder, null), enabled: tab.Folder != folder));
        return Submenu(text, items);
    }

    /// <summary>
    /// Move to Profile: the other Profiles (Arc's "Move to…"); not for an Essential, and a private window has the one.
    /// </summary>
    private MenuFlyoutSubItem? ProfileSubmenu(BrowserTab tab) =>
        _private || Profiles.Count < 2 || tab.Place == TabPlace.Essential ? null
            : Submenu("Move to Profile", Profiles.Where(s => s != _profile).Select(s => (MenuFlyoutItemBase)Command(s.Name, () => MoveTabToProfile(tab, s))).ToList());

    private MenuFlyoutItem? BookmarkItem(BrowserTab tab)
    {
        // An unloaded tab's engine is on a blank page: its own page is the one to bookmark.
        var url = LinkOf(tab);
        if (!CanBookmark(url)) return null;
        var saved = BookmarkStore.Tree.Find(url!);
        var item = new MenuFlyoutItem { Text = saved is null ? "Bookmark" : "Remove bookmark", DataContext = tab };
        item.Click += TabBookmark_Click;
        return item;
    }

    // ── A split's row ──

    private void FillSplitRowMenu(MenuFlyout menu, TabSplit split)
    {
        FillMenu(menu,
        [
            split.Pages.All(p => LinkOf(p) is not null)
                ? Command("Duplicate split", () => _ = DuplicateSplitAsync(split)) : null,
            Command("Separate tabs", () => SeparateAnimated(split)),
        ],
        split.Pages.Select(HalfSubmenu).ToArray(),
        [
            Command(split.Count == 2 ? "Close both tabs" : $"Close all {split.Count} tabs", () => _ = CloseSplitFromMenuAsync(split)),
            split.Row is { } row ? CloseTabsSubmenu(row) : null,
        ]);
    }

    /// <summary>One of a split's pages, titled with its page: copy its link, mute it, reload it, close it.</summary>
    private MenuFlyoutSubItem HalfSubmenu(BrowserTab page)
    {
        var items = new List<MenuFlyoutItemBase>();
        var loaded = page.View.CoreWebView2 is not null && !page.IsUnloaded;
        if (LinkOf(page) is not null) items.Add(Command("Copy link", () => CopyTabLink(page, markdown: false)));
        if (loaded)
        {
            items.Add(Command(page.IsMuted ? "Unmute tab" : "Mute tab", () => ToggleMute(page)));
            items.Add(Command("Reload", () => page.View.Reload()));
        }
        items.Add(new MenuFlyoutSeparator());
        items.Add(Command(page.IsPinned ? "Remove from split" : "Close tab", () => _ = CloseSplitPageFromMenuAsync(page)));
        return Submenu(Shorten(page.Title, 32), items);
    }

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";

    private async Task CloseSplitFromMenuAsync(TabSplit split)
    {
        if (!await MayLeaveAsync(split.Pages.Where(p => !p.IsPinned).ToList())) return;
        if (split.Row is not null) CloseSplit(split);
    }

    private async Task CloseSplitPageFromMenuAsync(BrowserTab page)
    {
        if (!page.IsPinned && !await MayLeaveAsync([page])) return;
        if (page.Split is not { } split) return;
        if (page.IsPinned) TakeOutOfSplit(page, next: _active == page ? split.Beside(page) : null);
        else RemoveTab(page);
    }

    // ── Close tabs › ──

    /// <summary>The row a tab has in the tab list (its own, or its split's); null for a tab elsewhere.</summary>
    private BrowserTab? RowOf(BrowserTab tab) =>
        tab.Split?.Row is { } splitRow ? (Tabs.Contains(splitRow) ? splitRow : null) : Tabs.Contains(tab) ? tab : null;

    private MenuFlyoutSubItem CloseTabsSubmenu(BrowserTab row)
    {
        var index = Tabs.IndexOf(row);
        var below = CloseTabs.Below(Tabs.Count, index);
        var others = CloseTabs.Others(Tabs.Count, index);
        var duplicates = DuplicateRows();
        return Submenu("Close tabs",
        [
            Command("Close tabs below", () => _ = CloseRowsAsync(below), enabled: below.Count > 0),
            Command("Close other tabs", () => _ = CloseRowsAsync(others), enabled: others.Count > 0),
            Command("Close duplicate tabs", () => _ = CloseRowsAsync(duplicates), enabled: duplicates.Count > 0),
        ]);
    }

    /// <summary>The tab list's rows whose page is open already (in a row above, a pinned tab, a split).</summary>
    private IReadOnlyList<int> DuplicateRows()
    {
        static string? Key(string? url) => string.IsNullOrEmpty(url) || url == "about:blank" ? null : Normalize(url);
        var kept = EssentialTabs.Concat(FolderTabs).Concat(PinnedTabs.SelectMany(RegularTabsOf))
            .Concat(Tabs.Where(r => r.IsSplitRow).SelectMany(r => r.Split?.Pages ?? Array.Empty<BrowserTab>()))
            .Select(t => Key(t.Url)).OfType<string>().ToList();
        var rows = Tabs.Select(r => r.IsSplitRow ? null : Key(r.Url)).ToList();
        int? front = _active is { } active && Tabs.IndexOf(active) is >= 0 and var at ? at : null;
        return CloseTabs.Duplicates(rows, kept, front);
    }

    /// <summary>Rows of the tab list close (a split's row, its regular pages); Undo brings them back where they were.</summary>
    private async Task CloseRowsAsync(IReadOnlyList<int> indices)
    {
        var rows = indices.Where(i => i >= 0 && i < Tabs.Count).Select(i => Tabs[i]).ToList();
        var pages = rows.SelectMany(RegularTabsOf).Where(p => !p.IsPinned).ToList();
        if (pages.Count == 0 || !await MayLeaveAsync(pages)) return;
        var entries = new List<SessionTab>();
        foreach (var row in rows)
        {
            var at = Tabs.IndexOf(row);
            foreach (var page in RegularTabsOf(row).Where(p => !p.IsPinned))
                if (ClosedEntry(page, at) is { } entry) entries.Add(entry);
        }
        foreach (var row in rows)
        {
            if (row is { IsSplitRow: true, Split: { } split }) CloseSplit(split, remember: false);
            else if (Tabs.Contains(row)) RemoveTab(row, remember: false);
        }
        // Each can still be reopened on its own, later.
        foreach (var entry in entries) RememberClosed(entry);
        ShowUndo(pages.Count == 1 ? "Closed 1 tab" : $"Closed {pages.Count} tabs", () => _ = RestoreClosedRowsAsync(entries));
    }

    private async Task RestoreClosedRowsAsync(List<SessionTab> entries)
    {
        foreach (var entry in entries) _closedTabs.Remove(entry);
        ClosedTabsChanged();
        SaveSessionSoon();
        foreach (var entry in entries.OrderBy(e => e.Index ?? int.MaxValue))
        {
            if (!Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri)) continue;
            await CreateTabAsync(uri, TabPlace.Regular, activate: false, deferLoad: true, title: entry.Title,
                index: entry.Index, name: entry.Name, icon: entry.Icon);
        }
    }

    private async Task CloseFromMenuAsync(BrowserTab tab)
    {
        if (!await MayLeaveAsync([tab])) return;
        RemoveTab(tab);
    }

    private async Task UnloadFromMenuAsync(BrowserTab tab)
    {
        if (await MayLeaveAsync([tab])) UnloadTab(tab);
    }

    // ── Copy link, Duplicate, Split ──

    /// <summary>The page it's on, or for an unloaded pinned tab, its own; null for a blank tab.</summary>
    private static string? LinkOf(BrowserTab tab)
    {
        var url = tab.Url;
        if (string.IsNullOrEmpty(url) || url == "about:blank") url = tab.HomeUri?.ToString();
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "file" ? url : null;
    }

    private static bool IsAltDown() =>
        InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(CoreVirtualKeyStates.Down);

    private void CopyTabLink(BrowserTab tab, bool markdown)
    {
        if (LinkOf(tab) is not { } url) return;
        var data = new DataPackage();
        data.SetText(markdown ? $"[{tab.Title.Replace("[", "\\[").Replace("]", "\\]")}]({url})" : url);
        try
        {
            Clipboard.SetContent(data);
            ShowCopiedToast();
        }
        catch
        {
            // The clipboard's busy with another app.
        }
    }

    /// <summary>A new tab on its page: beside it in the tab list, or for a pinned tab, at the top of it.</summary>
    private async Task DuplicateTabAsync(BrowserTab tab)
    {
        if (LinkOf(tab) is not { } url || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
        var index = RowOf(tab) is { } row ? Tabs.IndexOf(row) + 1 : 0;
        await CreateTabAsync(uri, TabPlace.Regular, activate: true, title: tab.PageTitle, index: index, name: tab.Name, icon: tab.Icon);
    }

    /// <summary>All its pages again, as a new split beneath it, with the same widths.</summary>
    private async Task DuplicateSplitAsync(TabSplit split)
    {
        var pages = split.Pages.ToList();
        var shares = split.Shares.ToArray();
        var links = pages.Select(p => LinkOf(p) is { } link && Uri.TryCreate(link, UriKind.Absolute, out var uri) ? uri : null).ToList();
        if (links.Any(l => l is null)) return;
        var index = split.Row is { } row ? Tabs.IndexOf(row) + 1 : Tabs.Count;
        var copies = new List<BrowserTab>();
        for (var i = 0; i < pages.Count; i++)
            copies.Add(await CreateTabAsync(links[i]!, TabPlace.Regular, activate: false, title: pages[i].PageTitle, index: index + i, name: pages[i].Name, icon: pages[i].Icon));
        SplitTabs(copies, shares, front: copies[0]);
    }

    /// <summary>The page's right-click menu: the link opens in a new tab, to the right of this one.</summary>
    private async Task OpenLinkInSplitAsync(Uri link, BrowserTab tab)
    {
        if (tab.Split is { IsFull: true })
        {
            OpenInNewTab(link);
            return;
        }
        var index = RowOf(tab) is { } row ? Tabs.IndexOf(row) + 1 : 0;
        var page = await CreateTabAsync(link, TabPlace.Regular, activate: false, index: index);
        PutBeside(tab, page, front: tab);
    }

    /// <summary>The tab settles in to the right of the one you're on, as if dropped there.</summary>
    private void SplitWithCurrent(BrowserTab tab)
    {
        if (_active is not { } current) return;
        PutBeside(current, tab, front: current);
    }

    // ── Rename…, Change icon… ──

    /// <summary>
    /// Once the menu has gone, and given focus back to the row it was on: what's focused before then
    /// (the name box) loses it to the row.
    /// </summary>
    private void AfterMenu(Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase menu, Action then)
    {
        void Closed(object? sender, object e)
        {
            menu.Closed -= Closed;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => then());
        }
        menu.Closed += Closed;
    }

    private void BeginTabRename(BrowserTab tab)
    {
        foreach (var other in AllTabs) other.IsRenaming = false;
        tab.IsRenaming = true;
        FocusTabNameBox(tab, attempts: 20);
    }

    /// <summary>Focuses the tab's name box, its title selected, once its row shows it.</summary>
    private void FocusTabNameBox(BrowserTab tab, int attempts)
    {
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!tab.IsRenaming) return;
            var container = new ListViewBase[] { FolderList, PinnedList, GroupList, TabList }
                .Select(list => list.ContainerFromItem(tab)).OfType<DependencyObject>().FirstOrDefault();
            if (container is not null && FindChild<TextBox>(container) is { Visibility: Visibility.Visible } box)
            {
                (container as UIElement)?.StartBringIntoView();
                box.Text = tab.Title;
                box.Focus(FocusState.Programmatic);
                box.SelectAll();
            }
            else if (attempts > 0) FocusTabNameBox(tab, attempts - 1);
            else tab.IsRenaming = false;
        });
    }

    /// <summary>The name typed: kept, unless it's empty or the page's own title, which takes its place again.</summary>
    private void EndTabRename(BrowserTab tab, string? name)
    {
        if (!tab.IsRenaming) return;
        tab.IsRenaming = false;
        if (name is null) return;
        tab.Name = string.IsNullOrWhiteSpace(name) || name.Trim() == tab.PageTitle ? null : name;
        SaveSessionSoon();
    }

    internal void TabName_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox { Tag: BrowserTab tab } box) return;
        if (e.Key == VirtualKey.Enter) EndTabRename(tab, box.Text);
        else if (e.Key == VirtualKey.Escape) EndTabRename(tab, null);
        else return;
        e.Handled = true;
        _active?.View.Focus(FocusState.Programmatic);
    }

    internal void TabName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { Tag: BrowserTab tab } box) EndTabRename(tab, box.Text);
    }

    private void PickTabIcon(FrameworkElement? anchor, BrowserTab tab)
    {
        if (anchor is null) return;
        ShowIconPicker(anchor, tab.Icon, "Use site icon", icon => _ = SetTabIconAsync(tab, icon));
    }
}
