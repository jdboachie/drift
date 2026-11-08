using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Drift.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace Drift;

/// <summary>
/// Folders on screen. What they hold and how they behave is FolderTree (Core, tested); this is the
/// sidebar's side of it: the folder list, renaming in place, the menus, and dragging.
/// <list type="bullet">
/// <item>New folder (menu, Ctrl+Alt+N, or a tab's Add to folder) starts with its name being edited.
/// Double-click a folder to rename it: Enter keeps the name, Esc puts it back.</item>
/// <item>A folder's menu: Rename, New subfolder, Expand all, Collapse all, Close all tabs (unloads
/// them, keeping their rows), Copy all links, Delete folder (asking whether to close its tabs or keep
/// them in the folder above).</item>
/// <item>Dragging: FolderTree.DropAt says where a drop lands; holding a drag over a closed folder's
/// header for half a second opens it.</item>
/// </list>
/// </summary>
public sealed partial class MainWindow
{
    private readonly FolderTree _folderTree = new();

    /// <summary>What the folder list shows: folder headers (TabFolder) and the tabs under them (BrowserTab).</summary>
    public ObservableCollection<object> FolderRows { get; } = new();

    /// <summary>Every tab in a folder, in sidebar order.</summary>
    private IEnumerable<BrowserTab> FolderTabs => _folderTree.AllTabs.Cast<BrowserTab>();

    // ───────────────────────── Showing ─────────────────────────

    /// <summary>Brings the folder list in line with the folders, changing only the rows that changed.</summary>
    private void RefreshFolderRows()
    {
        var syncing = _syncingSelection;
        _syncingSelection = true;
        ListSync.Apply(FolderRows, _folderTree.Rows(_active));
        FolderList.SelectedItem = _active is { Place: TabPlace.Folder, Split: null } && FolderRows.Contains(_active) ? _active : null;
        _syncingSelection = syncing;
        FolderList.Visibility = FolderRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var row in FolderRows) IndentFolderRow(FolderList.ContainerFromItem(row), row);
    }

    /// <summary>
    /// A row in a folder sits in from the edge a step per level, its highlight too, so it reads as
    /// inside the folder.
    /// </summary>
    private static void IndentFolderRow(DependencyObject? container, object row)
    {
        if (container is not ListViewItem item) return;
        var depth = row switch { BrowserTab tab => tab.Depth, TabFolder folder => folder.Depth, _ => 0 };
        item.Margin = new Thickness(depth * BrowserTab.IndentStep, 1, 0, 1);
    }

    /// <summary>A tab came to the front: in a closed folder, it stays under the folder's header.</summary>
    private void NoteFolderTabShown(BrowserTab tab)
    {
        if (tab.Place == TabPlace.Folder) _folderTree.Shown(tab);
        RefreshFolderRows();
    }

    private void ToggleFolder(TabFolder folder)
    {
        _folderTree.Toggle(folder, _active);
        RefreshFolderRows();
        SaveSessionSoon();
    }

    private void SetAllExpanded(bool expanded)
    {
        foreach (var folder in _folderTree.AllFolders.ToList()) _folderTree.SetExpanded(folder, expanded);
        RefreshFolderRows();
        SaveSessionSoon();
    }

    // ───────────────────────── Making and moving ─────────────────────────

    /// <summary>A new folder, at the end of its parent (or of the folders), its name ready to type.</summary>
    private TabFolder NewFolder(TabFolder? parent, BrowserTab? tab = null)
    {
        var folder = _folderTree.NewFolder("New folder", parent);
        if (folder.Parent is { } opened) _folderTree.SetExpanded(opened, true);
        if (tab is not null) MoveTabToFolder(tab, folder, null);
        RefreshFolderRows();
        SaveSessionSoon();
        BeginRename(folder);
        return folder;
    }

    /// <summary>Takes a tab out of whichever folder or group it's in, without closing it.</summary>
    private void DetachTab(BrowserTab tab)
    {
        // A tab moving elsewhere leaves its split (Split.cs).
        LeaveSplit(tab);
        var syncing = _syncingSelection;
        _syncingSelection = true;
        if (tab.Place == TabPlace.Folder) _folderTree.Remove(tab);
        else CollectionFor(tab.Place).Remove(tab);
        _syncingSelection = syncing;
    }

    /// <summary>Puts a tab in a folder, at an index among its contents (by default, last).</summary>
    private void MoveTabToFolder(BrowserTab tab, TabFolder folder, int? index)
    {
        // A tab that wasn't kept before belongs, from now on, to the page it's on.
        if (!tab.IsPinned) tab.HomeUri = Uri.TryCreate(tab.Url, UriKind.Absolute, out var here) ? here : null;
        if (tab.Place != TabPlace.Folder) DetachTab(tab);
        tab.Place = TabPlace.Folder;
        _folderTree.Add(tab, folder, index);
        if (tab == _active) _folderTree.Shown(tab);
        RefreshFolderRows();
        SaveSessionSoon();
        if (tab == _active) Activate(tab);
    }

    private void MoveFolder(TabFolder folder, TabFolder? into, int index)
    {
        _folderTree.MoveFolder(folder, into, index);
        RefreshFolderRows();
        SaveSessionSoon();
    }

    /// <summary>
    /// Removes a folder. Its tabs are closed, or kept: moved, with its subfolders, to where it was
    /// (a top-level folder's tabs become pinned tabs).
    /// </summary>
    private void DeleteFolder(TabFolder folder, bool closeTabs)
    {
        if (closeTabs)
        {
            foreach (var tab in folder.AllTabs.Cast<BrowserTab>().ToList()) RemoveTab(tab);
            _folderTree.Delete(folder, keepTabs: false);
        }
        else
        {
            foreach (var tab in _folderTree.Delete(folder, keepTabs: true).Cast<BrowserTab>()) MoveTab(tab, TabPlace.Pinned);
        }
        RefreshFolderRows();
        SaveSessionSoon();
    }

    // ───────────────────────── Renaming ─────────────────────────

    private void BeginRename(TabFolder folder)
    {
        foreach (var other in _folderTree.AllFolders) other.IsRenaming = false;
        folder.IsRenaming = true;
        FocusRenameBox(folder, attempts: 20);
    }

    /// <summary>Focuses the folder's name box with the name selected, once its row exists (a new folder's takes a layout pass or two).</summary>
    private void FocusRenameBox(TabFolder folder, int attempts)
    {
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!folder.IsRenaming) return;
            (FolderList.ContainerFromItem(folder) as UIElement)?.StartBringIntoView();
            if (FolderList.ContainerFromItem(folder) is DependencyObject row && FindChild<TextBox>(row) is { } box
                && box.Visibility == Visibility.Visible)
            {
                box.Text = folder.Name;
                box.Focus(FocusState.Programmatic);
                box.SelectAll();
            }
            else if (attempts > 0)
            {
                var timer = DispatcherQueue.CreateTimer();
                timer.Interval = TimeSpan.FromMilliseconds(30);
                timer.IsRepeating = false;
                timer.Tick += (_, _) => FocusRenameBox(folder, attempts - 1);
                timer.Start();
            }
        });
    }

    private void EndRename(TabFolder folder, string? name)
    {
        if (!folder.IsRenaming) return;
        if (!string.IsNullOrWhiteSpace(name)) folder.Name = name.Trim();
        folder.IsRenaming = false;
        SaveSessionSoon();
    }

    private void FolderName_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox { Tag: TabFolder folder } box) return;
        if (e.Key == VirtualKey.Enter) EndRename(folder, box.Text);
        else if (e.Key == VirtualKey.Escape) EndRename(folder, null);
        else return;
        e.Handled = true;
        _active?.View.Focus(FocusState.Programmatic);
    }

    private void FolderName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { Tag: TabFolder folder } box) EndRename(folder, box.Text);
    }

    private void FolderHeader_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TabFolder folder) return;
        // The two clicks opened and closed it again; the double-click itself renames.
        BeginRename(folder);
        e.Handled = true;
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            if (FindChild<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    // ───────────────────────── Clicks and menus ─────────────────────────

    private void FolderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection) return;
        switch (FolderList.SelectedItem)
        {
            case TabFolder folder:
                // Headers aren't selectable: the click opens or closes the folder, and the
                // selection goes back to the tab in front.
                _syncingSelection = true;
                FolderList.SelectedItem = _active is { Place: TabPlace.Folder, Split: null } && FolderRows.Contains(_active) ? _active : null;
                _syncingSelection = false;
                // After this event: the toggle changes the list's items.
                if (!folder.IsRenaming) DispatcherQueue.TryEnqueue(() => ToggleFolder(folder));
                break;
            case BrowserTab tab when tab != _active:
                Activate(tab);
                break;
        }
    }

    private static TabFolder? FolderOf(object sender) => (sender as FrameworkElement)?.Tag as TabFolder;

    private void FolderRename_Click(object sender, RoutedEventArgs e)
    {
        if (FolderOf(sender) is { } folder) BeginRename(folder);
    }

    private void FolderNewSubfolder_Click(object sender, RoutedEventArgs e)
    {
        if (FolderOf(sender) is { } folder) NewFolder(folder);
    }

    /// <summary>New tab, into this folder: the command bar opens, and what you open from it lands here.</summary>
    private void FolderNewTab_Click(object sender, RoutedEventArgs e)
    {
        if (FolderOf(sender) is not { } folder) return;
        OpenCommandBar(newTab: true, animate: true);
        _commandFolder = folder;
    }

    private void FolderExpandAll_Click(object sender, RoutedEventArgs e) => SetAllExpanded(true);

    private void FolderCollapseAll_Click(object sender, RoutedEventArgs e) => SetAllExpanded(false);

    /// <summary>Unloads every tab in it: their pages close, their rows stay.</summary>
    private void FolderCloseAll_Click(object sender, RoutedEventArgs e)
    {
        if (FolderOf(sender) is not { } folder) return;
        foreach (var tab in folder.AllTabs.Cast<BrowserTab>().ToList()) UnloadTab(tab);
        RefreshFolderRows();
    }

    private void FolderCopyLinks_Click(object sender, RoutedEventArgs e)
    {
        if (FolderOf(sender) is not { } folder) return;
        var links = folder.AllTabs.Cast<BrowserTab>()
            .Select(t => t.Url is { Length: > 0 } url && url != "about:blank" ? url : t.HomeUri?.ToString())
            .Where(u => !string.IsNullOrEmpty(u));
        var package = new DataPackage();
        package.SetText(string.Join(Environment.NewLine, links));
        Clipboard.SetContent(package);
    }

    private async void FolderDelete_Click(object sender, RoutedEventArgs e)
    {
        if (FolderOf(sender) is not { } folder) return;
        var count = folder.AllTabs.Count();
        if (count == 0 && !folder.Folders.Any())
        {
            DeleteFolder(folder, closeTabs: false);
            return;
        }
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = $"Delete “{folder.Name}”?",
            Content = count == 0
                ? "Its folders move up a level."
                : $"Close its {count} {(count == 1 ? "tab" : "tabs")}, or keep {(count == 1 ? "it" : "them")} in "
                  + (folder.Parent is { } parent ? $"“{parent.Name}”" : "your pinned tabs") + "?",
            PrimaryButtonText = count == 0 ? "Delete" : $"Close {(count == 1 ? "tab" : "tabs")}",
            SecondaryButtonText = count == 0 ? "" : $"Keep {(count == 1 ? "tab" : "tabs")}",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        _openSidebarMenus++;
        try
        {
            switch (await dialog.ShowAsync())
            {
                case ContentDialogResult.Primary:
                    DeleteFolder(folder, closeTabs: count > 0);
                    break;
                case ContentDialogResult.Secondary:
                    DeleteFolder(folder, closeTabs: false);
                    break;
            }
        }
        finally
        {
            _openSidebarMenus = Math.Max(0, _openSidebarMenus - 1);
        }
    }

    private void NewFolder_Click(object sender, RoutedEventArgs e) => NewFolder(null);

    /// <summary>A tab's menu: fills its "Add to folder" submenu with the folders there are now.</summary>
    private void TabMenu_Opening(object? sender, object e)
    {
        if (sender is not MenuFlyout menu) return;
        ShowSplitMenuItems(menu);
        foreach (var sub in menu.Items.OfType<MenuFlyoutSubItem>().Where(s => s.Tag is BrowserTab))
        {
            var tab = (BrowserTab)sub.Tag;
            sub.Items.Clear();
            var add = new MenuFlyoutItem { Text = "New folder", Icon = new FontIcon { Glyph = "" } };
            add.Click += (_, _) => NewFolder(null, tab);
            sub.Items.Add(add);
            if (_folderTree.Folders.Count > 0) sub.Items.Add(new MenuFlyoutSeparator());
            foreach (var folder in _folderTree.AllFolders)
            {
                var item = new MenuFlyoutItem
                {
                    Text = folder.Path,
                    Icon = new FontIcon { Glyph = "" },
                    IsEnabled = tab.Folder != folder,
                };
                var target = folder;
                item.Click += (_, _) => MoveTabToFolder(tab, target, null);
                sub.Items.Add(item);
            }
        }
    }

    /// <summary>A folder tab's "Remove from folder": it becomes an ordinary pinned tab.</summary>
    private void RemoveFromFolder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is BrowserTab tab) MoveTab(tab, TabPlace.Pinned);
    }

    // ───────────────────────── Dragging ─────────────────────────

    private TabFolder? _dragFolder;
    private TabFolder? _springFolder;
    private DispatcherQueueTimer? _springTimer;

    /// <summary>
    /// Where a drop here lands: onto the folder header the pointer's in the middle of, or at the
    /// slot the rows are showing. A header that can't take it falls back to the slot.
    /// </summary>
    private (FolderDrop? Drop, Landing Land, bool Onto) FindFolderDrop(DragEventArgs e)
    {
        var land = LandingIn(FolderList, e);
        var rows = FolderRows.ToList();
        if (land.Onto is TabFolder onto && _folderTree.DropAt(rows, land.Gap, onto, _dragFolder) is { } into)
            return (into, land, true);
        return (_folderTree.DropAt(rows, land.Gap, null, _dragFolder), land, false);
    }

    private void FolderList_DragOver(DragEventArgs e)
    {
        var (drop, land, onto) = FindFolderDrop(e);
        if (drop is not { } d)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            HideFolderHighlight();
            ShowRowPreview(FolderList, null);
            return;
        }
        // Onto a header: it goes last inside, so no room opens here; the header lights up.
        if (onto && d.Into is { } into)
        {
            ShowRowPreview(FolderList, null);
            ShowFolderHighlight(into);
            // Held over a closed folder: it springs open.
            if (!into.IsExpanded && _springFolder != into)
            {
                _springFolder = into;
                _springTimer ??= CreateSpringTimer();
                _springTimer.Stop();
                _springTimer.Start();
            }
            return;
        }
        HideFolderHighlight();
        ShowRowPreview(FolderList, land.Slot);
    }

    private DispatcherQueueTimer CreateSpringTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(500);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            if (_springFolder is { IsExpanded: false } folder && (_dragTab is not null || _dragFolder is not null))
            {
                _folderTree.SetExpanded(folder, true);
                RefreshFolderRows();
            }
        };
        return timer;
    }

    private void FolderList_Drop(DragEventArgs e)
    {
        var (drop, _, _) = FindFolderDrop(e);
        HideFolderHighlight();
        if (drop is not { } d) return;
        if (_dragFolder is { } folder) MoveFolder(folder, d.Into, d.Index);
        else if (_dragTab is { } tab && d.Into is { } into) MoveTabToFolder(tab, into, d.Index);
    }

    private void ShowFolderHighlight(TabFolder folder)
    {
        if (FolderList.ContainerFromItem(folder) is not FrameworkElement row || FolderList.ItemsPanelRoot is not { } panel) return;
        // Where the header's shown, which mid-drag isn't where it's laid out.
        var top = _previewTops.TryGetValue(FolderList, out var tops) && tops.TryGetValue(folder, out var shown) ? shown : row.ActualOffset.Y;
        var origin = panel.TransformToVisual(DropIndicatorLayer).TransformPoint(new Point(row.ActualOffset.X, top));
        Canvas.SetLeft(FolderDropHighlight, origin.X);
        Canvas.SetTop(FolderDropHighlight, origin.Y);
        FolderDropHighlight.Width = row.ActualWidth;
        FolderDropHighlight.Height = row.ActualHeight;
        FolderDropHighlight.Visibility = Visibility.Visible;
    }

    private void HideFolderHighlight()
    {
        FolderDropHighlight.Visibility = Visibility.Collapsed;
        _springFolder = null;
        _springTimer?.Stop();
    }

    private async Task RenderFolderGhostAsync(TabFolder folder)
    {
        _rowGhost = _tileGhost = null;
        RowGhostIcon.Source = null;
        RowGhostGlyph.Glyph = folder.Glyph;
        RowGhostGlyph.Visibility = Visibility.Visible;
        RowGhostTitle.Text = folder.Name;
        RowGhost.Width = Math.Max(120, FolderList.ActualWidth);
        try
        {
            var row = await SnapshotAsync(RowGhost);
            if (_dragFolder == folder && row is { } r)
            {
                _rowGhost = r.Bitmap;
                _rowGhostAnchor = new Point(19 * r.Scale, r.Bitmap.PixelHeight / 2.0);
            }
        }
        catch
        {
            // The list's own picture of the row will do.
        }
        finally
        {
            RowGhostGlyph.Glyph = "";
        }
    }
}
