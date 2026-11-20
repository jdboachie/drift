using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Drift.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls.Primitives;
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
/// them, keeping their rows), Copy all links, Ungroup (the folder goes, what's in it moves up into
/// its place, as Chrome's tab groups' Ungroup), Delete folder (the folder and its tabs).</item>
/// <item>Deleting (its menu, or the × on its header) asks nothing: it goes at once, with Undo for a
/// moment (Apple's forgiveness over confirmation). Its tabs close for good, and can be reopened,
/// once Undo's gone.</item>
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

    // How each row sits among the folders' cards (FolderCard), worked out whenever the rows change.
    private readonly Dictionary<object, CardRow> _folderCardRows = new(ReferenceEqualityComparer.Instance);

    /// <summary>Brings the folder list in line with the folders, changing only the rows that changed.</summary>
    private void RefreshFolderRows()
    {
        // A folder mid-way open or shut gets there at once: its rows are about to change under it.
        FinishFolderMotion();
        var syncing = _syncingSelection;
        _syncingSelection = true;
        ListSync.Apply(FolderRows, _folderTree.Rows(_active));
        var cards = FolderCard.Layout(FolderRows.Select(CardInput).ToList());
        _folderCardRows.Clear();
        for (var i = 0; i < FolderRows.Count; i++) _folderCardRows[FolderRows[i]] = cards[i];
        FolderList.SelectedItem = _active is { Place: TabPlace.Folder, Split: null } && FolderRows.Contains(_active) ? _active : null;
        _syncingSelection = syncing;
        FolderList.Visibility = FolderRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var row in FolderRows) IndentFolderRow(FolderList.ContainerFromItem(row), row);
    }

    private static int DepthOf(object row) => row switch { BrowserTab tab => tab.Depth, TabFolder folder => folder.Depth, LiveNote note => note.Depth, _ => 0 };

    private static (int Depth, bool IsFolder, bool Open) CardInput(object row) =>
        (DepthOf(row), row is TabFolder, row is TabFolder { IsExpanded: true });

    // Folders' cards (FolderCard): the rows inside one sit this far in from its sides, with this much more
    // room below its last row; its corners are a little rounder than the rows'. A folder's own header
    // sits as any row does, its card fitting round it.
    private const double CardInset = 4;
    private const double CardRoom = 4;
    private const double CardRadius = 10;
    private const double RowGap = 3;

    private static readonly CardRow NoCards = new([], 0, 0);

    /// <summary>A card piece in a row: whose and which, for the folder motion (FolderMotion.cs).</summary>
    private sealed record CardTag(CardPiece Piece);

    /// <summary>
    /// A row in a folder sits in from the edge a step per level, its highlight too, so it reads as
    /// inside the folder, and in a little more from both sides for each card it's in. It draws its
    /// pieces of those cards (and its own, for a folder's header): the rows' pieces meet across the
    /// gaps between them, so together each card is one rounded surface.
    /// </summary>
    private void IndentFolderRow(DependencyObject? container, object row)
    {
        if (container is not ListViewItem item) return;
        var cards = _folderCardRows.GetValueOrDefault(row) ?? NoCards;
        var depth = DepthOf(row);
        var left = depth * BrowserTab.IndentStep + cards.Inside * CardInset;
        var right = cards.Inside * CardInset;
        var bottom = RowGap + cards.Ends * CardRoom;
        item.Margin = new Thickness(left, RowGap, right, bottom);
        // A live folder's "No open pull requests" is only words: no hover, no click.
        item.IsHitTestVisible = row is not LiveNote { OpensSignIn: false };

        item.ApplyTemplate();
        if (VisualTreeHelper.GetChildrenCount(item) == 0 || VisualTreeHelper.GetChild(item, 0) is not Grid root) return;
        // As many pieces as cards, behind the rest of the row, outermost first.
        var pieces = root.Children.OfType<Border>().Where(b => b.Tag is CardTag).ToList();
        while (pieces.Count > cards.Pieces.Count)
        {
            root.Children.Remove(pieces[^1]);
            pieces.RemoveAt(pieces.Count - 1);
        }
        while (pieces.Count < cards.Pieces.Count)
        {
            var piece = new Border { Style = (Style)Application.Current.Resources["FolderCardStyle"], IsHitTestVisible = false };
            root.Children.Insert(pieces.Count, piece);
            pieces.Add(piece);
        }

        for (var i = 0; i < cards.Pieces.Count; i++)
        {
            var piece = cards.Pieces[i];
            var card = pieces[i];
            card.Tag = new CardTag(piece);
            // The card's sides: its folder's header's.
            var cardLeft = piece.Level * (BrowserTab.IndentStep + CardInset);
            var cardRight = piece.Level * CardInset;
            var ownHeader = row is TabFolder folder && folder.Depth == piece.Level;
            var above = ownHeader ? 0 : RowGap;
            var under = piece.Part switch
            {
                CardPart.Bottom => piece.Rank * CardRoom,
                CardPart.Only or CardPart.Hover => 0,
                _ => bottom,
            };
            card.Margin = new Thickness(-(left - cardLeft), -above, -(right - cardRight), -under);
            card.CornerRadius = piece.Part switch
            {
                CardPart.Top => new CornerRadius(CardRadius, CardRadius, 0, 0),
                CardPart.Bottom => new CornerRadius(0, 0, CardRadius, CardRadius),
                CardPart.Only or CardPart.Hover => new CornerRadius(CardRadius),
                _ => new CornerRadius(0),
            };
            card.BorderThickness = piece.Part switch
            {
                CardPart.Top => new Thickness(1, 1, 1, 0),
                CardPart.Bottom => new Thickness(1, 0, 1, 1),
                CardPart.Only or CardPart.Hover => new Thickness(1),
                _ => new Thickness(1, 0, 1, 0),
            };
            card.Visibility = piece.Part != CardPart.Hover || row == _hoveredFolder ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static T? FindNamed<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T { Name: var n } match && n == name) return match;
            if (FindNamed<T>(child, name) is { } found) return found;
        }
        return null;
    }

    /// <summary>A tab came to the front: in a closed folder, it stays under the folder's header.</summary>
    private void NoteFolderTabShown(BrowserTab tab)
    {
        // Seen: no longer new in its live folder (LiveFolders.cs).
        tab.IsNew = false;
        if (tab.Place == TabPlace.Folder) _folderTree.Shown(tab);
        RefreshFolderRows();
    }

    private void ToggleFolder(TabFolder folder)
    {
        HideFolderPreview();
        // As Dia's groups: the card's bottom edge comes up over its tabs, or goes down to show them (FolderMotion.cs).
        if (!AnimateFolder(folder))
        {
            _folderTree.Toggle(folder, _active);
            RefreshFolderRows();
        }
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
        if (tab.Place == TabPlace.Folder)
        {
            DismissLive(tab);
            _folderTree.Remove(tab);
        }
        else CollectionFor(tab.Place).Remove(tab);
        _syncingSelection = syncing;
    }

    /// <summary>Puts a tab in a folder, at an index among its contents (by default, last).</summary>
    private void MoveTabToFolder(BrowserTab tab, TabFolder folder, int? index)
    {
        // A tab that wasn't kept before belongs, from now on, to the page it's on.
        if (!tab.IsPinned) tab.HomeUri = Uri.TryCreate(tab.Url, UriKind.Absolute, out var here) ? here : null;
        if (folder.IsLive) return;
        if (tab.Place != TabPlace.Folder) DetachTab(tab);
        else if (tab.Folder != folder) DismissLive(tab);
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

    internal void FolderName_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox { Tag: TabFolder folder } box) return;
        if (e.Key == VirtualKey.Enter) EndRename(folder, box.Text);
        else if (e.Key == VirtualKey.Escape) EndRename(folder, null);
        else return;
        e.Handled = true;
        _active?.View.Focus(FocusState.Programmatic);
    }

    internal void FolderName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { Tag: TabFolder folder } box) EndRename(folder, box.Text);
    }

    internal void FolderHeader_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
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
            case LiveNote note:
                // Not selectable either. Signed out, it takes you to GitHub's sign-in.
                _syncingSelection = true;
                FolderList.SelectedItem = _active is { Place: TabPlace.Folder, Split: null } && FolderRows.Contains(_active) ? _active : null;
                _syncingSelection = false;
                if (note.OpensSignIn) DispatcherQueue.TryEnqueue(() => OpenInNewTab(GitHubPulls.SignIn));
                break;
            case BrowserTab tab when tab != _active:
                Activate(tab);
                break;
        }
    }

    private static TabFolder? FolderOf(object sender) => (sender as FrameworkElement)?.Tag as TabFolder;

    /// <summary>
    /// A folder's right-click menu, grouped as Arc's: what goes in it and its links; its name and icon;
    /// opening and closing them all; then closing its tabs and deleting it. A live folder's tabs come
    /// from GitHub, not from you: it's refreshed and shown there, and says what it shows, instead.
    /// </summary>
    internal void FolderMenu_Opening(object? sender, object e)
    {
        if (sender is not MenuFlyout menu) return;
        menu.Items.Clear();
        if (menu.Target is not FrameworkElement { Tag: TabFolder folder }) return;

        // Each item carries the folder, as the handlers expect.
        MenuFlyoutItem Item(string text, RoutedEventHandler click)
        {
            var item = new MenuFlyoutItem { Text = text, Tag = folder };
            item.Click += click;
            return item;
        }

        var copyLinks = Item("Copy all links", FolderCopyLinks_Click);
        var expand = new MenuFlyoutItemBase?[] { Item("Expand all folders", FolderExpandAll_Click), Item("Collapse all folders", FolderCollapseAll_Click) };
        // A live folder's tabs are its source's, so there's nothing to ungroup.
        var ending = new MenuFlyoutItemBase?[]
        {
            Item("Close all tabs", FolderCloseAll_Click),
            folder.IsLive ? null : Item("Ungroup", FolderUngroup_Click),
            Item("Delete folder", FolderDelete_Click),
        };

        if (folder.Live is { } live)
            FillMenu(menu,
                [Item("Refresh", LiveFolderRefresh_Click), Item("Open on GitHub", LiveFolderOpenSource_Click), copyLinks],
                LiveMenuItems(folder, live),
                [Item("Rename", FolderRename_Click)],
                expand,
                ending);
        else
            FillMenu(menu,
                [Item("New tab", FolderNewTab_Click), Item("New subfolder", FolderNewSubfolder_Click), copyLinks],
                [Item("Rename", FolderRename_Click), Item("Change icon", FolderIcon_Click)],
                expand,
                ending);
    }

    private void FolderRename_Click(object sender, RoutedEventArgs e)
    {
        if (FolderOf(sender) is { } folder) BeginRename(folder);
    }

    /// <summary>Change icon…: the picker beside the folder's row.</summary>
    private void FolderIcon_Click(object sender, RoutedEventArgs e)
    {
        if (FolderOf(sender) is not { } folder || FolderList.ContainerFromItem(folder) is not FrameworkElement row) return;
        ShowIconPicker(row, folder.Icon, "Use folder icon", icon =>
        {
            folder.Icon = icon;
            SaveSessionSoon();
        });
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

    /// <summary>Unloads every tab in it: their pages close, their rows stay (its menu's Close all tabs).</summary>
    private async void FolderCloseAll_Click(object sender, RoutedEventArgs e)
    {
        if (FolderOf(sender) is not { } folder) return;
        if (!await MayLeaveAsync(folder.AllTabs.Cast<BrowserTab>())) return;
        foreach (var tab in folder.AllTabs.Cast<BrowserTab>().ToList()) UnloadTab(tab);
        RefreshFolderRows();
    }

    // ───────────────────────── Hovering a header ─────────────────────────

    /// <summary>
    /// Its × shows, as Dia's groups' does. A closed folder, held a moment,
    /// shows what's in it beside the sidebar.
    /// </summary>
    internal void FolderHeader_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Grid { Tag: TabFolder folder } header) return;
        if (header.Children.OfType<Button>().FirstOrDefault() is { } close)
        {
            close.Visibility = Visibility.Visible;
            close.Opacity = 1;
        }
        // A live folder's spinner moves over for the ×, still in sight.
        if (header.Children.OfType<ProgressRing>().FirstOrDefault() is { } spinner) spinner.Translation = default;
        // Closed, it shows its card, as Dia's groups do.
        _hoveredFolder = folder;
        IndentFolderRow(FolderList.ContainerFromItem(folder), folder);
        // Back on the header of the folder whose list is open: it stays as it is (rebuilt, it flickered).
        if (_folderPreview is { IsOpen: true } && _folderPreviewShown == folder) return;
        if (!folder.IsExpanded && !folder.IsRenaming && _dragTab is null) ShowFolderPreviewSoon(folder);
    }

    // The folder whose header the pointer's on: closed, its card shows (IndentFolderRow).
    private TabFolder? _hoveredFolder;

    internal void FolderHeader_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid { Tag: TabFolder folder } && folder == _hoveredFolder)
        {
            _hoveredFolder = null;
            IndentFolderRow(FolderList.ContainerFromItem(folder), folder);
        }
        if (sender is Grid header && header.Children.OfType<Button>().FirstOrDefault() is { } close) close.Opacity = 0;
        if (sender is Grid row && row.Children.OfType<ProgressRing>().FirstOrDefault() is { } spinner) spinner.Translation = SpinnerAtRest;
        // Not yet shown: passing over it on the way somewhere else shows nothing. Shown, it stays while
        // the pointer goes over to it (WatchFolderPreview).
        _folderPreviewTimer?.Stop();
    }

    // A live folder's spinner at rest: over in the ×'s place (its 24, and the gap before it, less the
    // difference in their widths, so the two are centred alike).
    private static readonly System.Numerics.Vector3 SpinnerAtRest = new(27, 0, 0);

    /// <summary>
    /// Put at rest before its slide's set, so it doesn't slide there as it loads. (In code: a
    /// Translation written in the XAML stops the XAML compiler, with no message.)
    /// </summary>
    internal void FolderSpinner_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ProgressRing spinner || spinner.TranslationTransition is not null) return;
        spinner.Translation = SpinnerAtRest;
        spinner.TranslationTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(150) };
        // Showing, it takes some of the name's room; gone, the name has it back.
        spinner.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) =>
        {
            if (spinner.Parent is Grid header) FitFolderName(header);
        });
        if (spinner.Parent is Grid row) FitFolderName(row);
    }

    /// <summary>The name takes the room there is, short of its chevron and the ×, and trims to fit.</summary>
    internal void FolderHeader_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Grid header) FitFolderName(header);
    }

    private static void FitFolderName(Grid header)
    {
        // The name's column (the emoji's is the first): it gives way so the × always fits.
        if (header.ActualWidth <= 0 || header.Children.OfType<TextBlock>().FirstOrDefault(t => Grid.GetColumn(t) == 1) is not { } name) return;
        // Icon, chevron and × with the gaps between: 18 + 10 + 24, and four gaps of the header's spacing.
        // A live folder's spinner too while it shows (beside the × while the header's hovered): 14 and a gap.
        var spinner = header.Children.OfType<ProgressRing>().Any(r => r.Visibility == Visibility.Visible) ? 14 + header.ColumnSpacing : 0;
        name.MaxWidth = Math.Max(0, header.ActualWidth - 18 - 10 - 24 - 4 * header.ColumnSpacing - spinner);
    }

    // A closed folder's contents, beside the sidebar while its header's hovered (ShowFolderPreview).
    private MenuFlyout? _folderPreview;
    private DispatcherQueueTimer? _folderPreviewTimer;
    private TabFolder? _folderPreviewFor;
    // The folder whose list is the one showing.
    private TabFolder? _folderPreviewShown;
    // Long enough that passing over a header on the way past shows nothing.
    private const int FolderPreviewDelayMs = 250;
    // While it's open, where the pointer is is checked this often; off the header and the list for
    // two checks running (long enough to cross between them), it goes.
    private const int FolderPreviewWatchMs = 150;
    private DispatcherQueueTimer? _folderPreviewWatch;
    private FrameworkElement? _folderPreviewRow;
    private int _folderPreviewMisses;
    // Where the pointer last was on the header: on its way from there to the list, it stays (MenuAim).
    private Point? _folderPreviewLeftFrom;
    // Checks running that only the way-to-the-list kept it: parked there, off both, it goes after these.
    private int _folderPreviewAiming;
    private const int FolderPreviewAimTicks = 5;

    private void ShowFolderPreviewSoon(TabFolder folder)
    {
        if (!Settings.Current.PreviewFolders) return;
        _folderPreviewFor = folder;
        if (_folderPreviewTimer is null)
        {
            _folderPreviewTimer = DispatcherQueue.CreateTimer();
            _folderPreviewTimer.Interval = TimeSpan.FromMilliseconds(FolderPreviewDelayMs);
            _folderPreviewTimer.IsRepeating = false;
            _folderPreviewTimer.Tick += (_, _) =>
            {
                if (_folderPreviewFor is { IsExpanded: false } shown) ShowFolderPreview(shown);
            };
        }
        _folderPreviewTimer.Stop();
        _folderPreviewTimer.Start();
    }

    /// <summary>
    /// A closed folder's contents in a list beside its header, as Dia shows a closed group's: its tabs,
    /// its folders (each opening its own list beside it), and New tab at the end. Pick a tab to go to
    /// it (it shows under the header while it's in front). Goes when the pointer has left both it and
    /// the header (WatchFolderPreview): not WinUI's own "moves away", which goes by how far the pointer
    /// is from the list, so resting on the folder's name at the far side of the sidebar closed it at the
    /// slightest movement.
    /// </summary>
    private void ShowFolderPreview(TabFolder folder)
    {
        // Still on screen: the list may have changed while the pointer rested on it.
        // Not over the command bar, which is where the keyboard is.
        if (_commandBarOpen) return;
        if (FolderList.ContainerFromItem(folder) is not FrameworkElement { IsLoaded: true, XamlRoot: not null } row) return;
        // A new one each time: one still open or on its way out doesn't take new content or a new place
        // (XAML failed on a reused one and took Drift down). Inside the window, not a window of its own:
        // the command bar opened from one of those took Drift down once you typed in it.
        _folderPreview?.Hide();
        var menu = _folderPreview = new MenuFlyout();
        // What was picked happens once the list has gone, so the keyboard isn't moved while it's closing.
        Action? picked = null;
        var items = FolderPreviewItems(folder, action => picked = action);
        // A live folder with nothing in it just now: nothing to list.
        if (items.Count == 0) return;
        foreach (var item in items) menu.Items.Add(item);
        // Open, it keeps a sidebar that's floating over the page from going away.
        menu.Opened += SidebarMenu_Opened;
        menu.Closed += SidebarMenu_Closed;
        menu.Opened += (sender, _) =>
        {
            // Hidden or replaced while it was still opening, it opens anyway, after Drift's moved on, and
            // with its row gone it lands on the window's corner, over the sidebar, where nothing closed it.
            if (sender != _folderPreview || _folderPreviewFor != folder || !row.IsLoaded || !PreviewBeside(menu, row))
            {
                menu.Hide();
                return;
            }
            _folderPreviewShown = folder;
            WatchFolderPreview(row);
        };
        menu.Closed += (sender, _) =>
        {
            if (sender == _folderPreview) _folderPreviewWatch?.Stop();
            CloseSubmenusSoon(menu);
            if (picked is { } action) DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => action());
            picked = null;
        };
        try
        {
            menu.ShowAt(row, new FlyoutShowOptions
            {
                Placement = FlyoutPlacementMode.RightEdgeAlignedTop,
                ShowMode = FlyoutShowMode.Transient,
            });
        }
        catch (Exception error)
        {
            // Nowhere to show it just now: the folder's still there to click.
            App.LogCrash(error);
            _folderPreview = null;
        }
    }

    /// <summary>
    /// A subfolder's list, opened from the preview, goes with it. WinUI leaves a submenu that's open (or
    /// still opening) when its menu closes, and drops it on the window's corner, over the sidebar, where
    /// nothing closes it (as GhostPopups notes for menus). Now, and again a moment later for one that
    /// was still on its way.
    /// </summary>
    private void CloseSubmenusSoon(MenuFlyout menu)
    {
        CloseSubmenus(menu);
        var later = DispatcherQueue.CreateTimer();
        later.Interval = TimeSpan.FromMilliseconds(400);
        later.IsRepeating = false;
        later.Tick += (_, _) =>
        {
            later.Stop();
            if (!menu.IsOpen) CloseSubmenus(menu);
        };
        later.Start();
    }

    private void CloseSubmenus(MenuFlyout menu)
    {
        if (Root.XamlRoot is null) return;
        var inSubmenus = new HashSet<MenuFlyoutItemBase>();
        void Collect(IEnumerable<MenuFlyoutItemBase> items)
        {
            foreach (var item in items)
                if (item is MenuFlyoutSubItem sub)
                {
                    foreach (var inner in sub.Items) inSubmenus.Add(inner);
                    Collect(sub.Items);
                }
        }
        Collect(menu.Items);
        if (inSubmenus.Count == 0) return;
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot))
            if (popup.Child is MenuFlyoutPresenter list && list.Items.Any(inSubmenus.Contains))
                popup.IsOpen = false;
    }

    /// <summary>Its list, beside its row (not over it, as one that opened with no row to go by is).</summary>
    private bool PreviewBeside(MenuFlyout menu, FrameworkElement row)
    {
        if (PreviewList(menu) is not { } list) return true;
        var at = BoundsOf(row);
        return BoundsOf(list).X >= at.X + at.Width - 8;
    }

    /// <summary>The list a menu's showing in, if it's open.</summary>
    private MenuFlyoutPresenter? PreviewList(MenuFlyout menu)
    {
        if (menu.Items.FirstOrDefault() is not { } first || Root.XamlRoot is null) return null;
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot))
            if (popup.Child is MenuFlyoutPresenter list && list.Items.Contains(first)) return list;
        return null;
    }

    /// <summary>
    /// The preview stays while the pointer's on its folder's header or on it (or a folder's list opened
    /// from it), or on its way from the header to it, for a moment.
    /// </summary>
    private void WatchFolderPreview(FrameworkElement row)
    {
        _folderPreviewRow = row;
        _folderPreviewMisses = 0;
        _folderPreviewAiming = 0;
        _folderPreviewLeftFrom = null;
        if (_folderPreviewWatch is null)
        {
            _folderPreviewWatch = DispatcherQueue.CreateTimer();
            _folderPreviewWatch.Interval = TimeSpan.FromMilliseconds(FolderPreviewWatchMs);
            _folderPreviewWatch.Tick += (_, _) =>
            {
                // Drift's own windows count: the list is one of its own.
                var at = PointerOverDrift();
                if (at is { } on && FolderPreviewHolds(on))
                {
                    _folderPreviewMisses = 0;
                    _folderPreviewAiming = 0;
                    if (_folderPreviewRow is { XamlRoot: not null } header && Contains(BoundsOf(header), on)) _folderPreviewLeftFrom = on;
                    return;
                }
                if (at is { } going && HeadingForPreview(going) && ++_folderPreviewAiming <= FolderPreviewAimTicks)
                {
                    _folderPreviewMisses = 0;
                    return;
                }
                if (++_folderPreviewMisses >= 2) HideFolderPreview();
            };
        }
        _folderPreviewWatch.Start();
    }

    private static bool Contains(Rect r, Point at) => at.X >= r.X && at.X < r.X + r.Width && at.Y >= r.Y && at.Y < r.Y + r.Height;

    /// <summary>On its way from where it left the header to the list (MenuAim's triangle).</summary>
    private bool HeadingForPreview(Point at)
    {
        if (_folderPreviewLeftFrom is not { } from || _folderPreview is not { } menu || PreviewList(menu) is not { } list) return false;
        var edge = BoundsOf(list);
        return MenuAim.Toward(at.X, at.Y, from.X, from.Y, edge.X, edge.Y - 8, edge.Y + edge.Height + 8);
    }

    private bool FolderPreviewHolds(Point at)
    {
        if (_folderPreviewRow is { XamlRoot: not null } row && Near(BoundsOf(row), 2)) return true;
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot))
            if (popup.Child is MenuFlyoutPresenter list && Near(BoundsOf(list), 8)) return true;
        return false;

        bool Near(Rect r, double slack) =>
            at.X >= r.X - slack && at.X < r.X + r.Width + slack && at.Y >= r.Y - slack && at.Y < r.Y + r.Height + slack;
    }

    /// <summary>
    /// A folder's list: its tabs and folders in sidebar order, then New tab into it. Not into a live
    /// folder, whose tabs come from where it's following (as its own menu has no New tab).
    /// </summary>
    private List<MenuFlyoutItemBase> FolderPreviewItems(TabFolder folder, Action<Action> pick)
    {
        var items = new List<MenuFlyoutItemBase>();
        foreach (var child in folder.Children)
        {
            switch (child)
            {
                case TabFolder sub:
                    var subItem = new MenuFlyoutSubItem { Text = sub.Name, Icon = FolderIconOf(sub) };
                    foreach (var item in FolderPreviewItems(sub, pick)) subItem.Items.Add(item);
                    items.Add(subItem);
                    break;
                case BrowserTab tab:
                    IconElement icon = tab.Favicon is { } favicon
                        ? new ImageIcon { Source = favicon }
                        : new FontIcon { Glyph = "\uE774" };
                    var tabItem = new MenuFlyoutItem { Text = tab.Title, Icon = icon };
                    tabItem.Click += (_, _) => pick(() => Activate(tab));
                    items.Add(tabItem);
                    break;
            }
        }
        if (folder.IsLive) return items;
        var newTab = new MenuFlyoutItem { Text = "New tab", Icon = new FontIcon { Glyph = "\uE710" } };
        newTab.Click += (_, _) => pick(() =>
        {
            OpenCommandBar(newTab: true, animate: true);
            _commandFolder = folder;
        });
        items.Add(newTab);
        return items;
    }

    /// <summary>A folder's icon in the list: its emoji or symbol, else a folder.</summary>
    private static IconElement FolderIconOf(TabFolder folder) => folder.HasEmoji
        ? new FontIcon { Glyph = folder.Emoji, FontFamily = new FontFamily("Segoe UI Emoji") }
        : new FontIcon { Glyph = folder.Glyph, FontFamily = Ui.IconFont(folder.Glyph) };

    private void HideFolderPreview()
    {
        _folderPreviewTimer?.Stop();
        _folderPreviewWatch?.Stop();
        _folderPreviewFor = null;
        // Let go of too: one still opening is closed as it opens (its Opened), not left on the corner.
        var menu = _folderPreview;
        _folderPreview = null;
        menu?.Hide();
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

    /// <summary>
    /// The × on its header, or Delete folder in its menu: the folder and everything in it go at once,
    /// with Undo for a moment. Only a page's own unsaved changes are asked about first.
    /// </summary>
    internal async void FolderDelete_Click(object sender, RoutedEventArgs e)
    {
        if (FolderOf(sender) is not { } folder) return;
        var tabs = folder.AllTabs.Cast<BrowserTab>().ToList();
        if (tabs.Count > 0 && !await MayLeaveAsync(tabs)) return;
        if (!_folderTree.AllFolders.Contains(folder)) return;
        SetFolderAside(folder, tabs);
    }

    /// <summary>
    /// A folder out of the sidebar, its tabs still open behind the scenes so Undo puts it all back as
    /// it was; once Undo's gone, its tabs close (and can be reopened, as any closed tab).
    /// </summary>
    private void SetFolderAside(TabFolder folder, List<BrowserTab> tabs)
    {
        foreach (var tab in tabs)
        {
            LeaveSplit(tab);
            if (tab == _peekOwner) ClosePeek(animate: false);
        }
        if (_active is { } active && tabs.Contains(active))
        {
            var next = _recentTabs.FirstOrDefault(t => !tabs.Contains(t) && AllTabs.Contains(t))
                ?? AllTabs.FirstOrDefault(t => !tabs.Contains(t));
            if (next is not null) Activate(next);
            else
            {
                active.View.Visibility = Visibility.Collapsed;
                _active = null;
                UpdateChrome();
            }
        }
        var (parent, index) = _folderTree.Detach(folder);
        RefreshFolderRows();
        SaveSessionSoon();
        var count = tabs.Count;
        ShowUndo(count == 0 ? $"Deleted “{folder.Name}”" : $"Deleted “{folder.Name}” and its {count} {(count == 1 ? "tab" : "tabs")}",
            undo: () =>
            {
                _folderTree.Reattach(folder, parent, index);
                RefreshFolderRows();
                SaveSessionSoon();
                if (folder.IsLive) LiveTick();
            },
            gone: () =>
            {
                foreach (var tab in folder.AllTabs.Cast<BrowserTab>().ToList()) RemoveTab(tab);
                SaveSessionSoon();
            });
    }

    /// <summary>Ungroup: the folder goes, and what's in it moves up into its place (a top-level folder's tabs become pinned tabs).</summary>
    private void FolderUngroup_Click(object sender, RoutedEventArgs e)
    {
        if (FolderOf(sender) is { } folder) DeleteFolder(folder, closeTabs: false);
    }

    internal void NewFolder_Click(object sender, RoutedEventArgs e) => NewFolder(null);

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
        GhostStage.RowIcon.Source = null;
        GhostStage.RowGlyph.Glyph = folder.Glyph;
        GhostStage.RowGlyph.Visibility = Visibility.Visible;
        GhostStage.RowTitle.Text = folder.Name;
        GhostStage.Row.Width = Math.Max(120, FolderList.ActualWidth);
        try
        {
            var row = await SnapshotAsync(GhostStage.Row);
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
            GhostStage.RowGlyph.Glyph = "";
        }
    }
}
