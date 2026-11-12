using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Drift.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace Drift;

/// <summary>
/// Bookmarks: pages you save to come back to, kept apart from the sidebar's pinned tabs (which, as
/// in Arc, are for the few you keep open), so there can be any number of them. Ctrl+D saves the page
/// in front, and says so at the foot of the page with an Edit button; on a page that's already saved
/// it opens the editor (name, folder, Remove). The command bar has Bookmark Page, Bookmark to
/// [folder], Edit Bookmark and Remove Bookmark, and finds bookmarks first among what you type; a
/// tab's right-click menu has Bookmark too. They're in the Library under Bookmarks, in folders that
/// open and close, with search. Kept in %LOCALAPPDATA%\Drift\bookmarks.json.
/// </summary>
public sealed partial class MainWindow
{
    // Folders opened in the Library, by id, for this session.
    private readonly HashSet<string> _openBookmarkFolders = new();
    private DispatcherQueueTimer? _bookmarkToastTimer;
    // The bookmark the toast is about.
    private BookmarkNode? _toastBookmark;
    private bool _bookmarkEditorOpen;

    private static bool CanBookmark(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "file";

    /// <summary>Ctrl+D: saves the page in front, or for one already saved, opens its editor.</summary>
    private void BookmarkCurrent()
    {
        if (_active?.View.CoreWebView2 is not { } core || !CanBookmark(core.Source)) return;
        if (BookmarkStore.Tree.Find(core.Source) is { } saved)
        {
            ShowBookmarkToast(saved, "Bookmarked");
            ShowBookmarkEditor(saved, BookmarkToast, FlyoutPlacementMode.Top);
            return;
        }
        BookmarkTab(_active, null);
    }

    /// <summary>Saves a tab's page, into a folder or at the top, and says so.</summary>
    private void BookmarkTab(BrowserTab tab, BookmarkNode? folder)
    {
        var url = tab.View.CoreWebView2?.Source ?? tab.Url;
        if (!CanBookmark(url)) return;
        var node = BookmarkStore.Tree.Find(url!);
        if (node is null)
        {
            var title = string.IsNullOrWhiteSpace(tab.Title) ? url! : tab.Title;
            node = BookmarkStore.Tree.Add(url!, title, folder);
            BookmarkStore.Save();
        }
        else if (folder is not null && BookmarkStore.Tree.ParentOf(node) != folder)
        {
            BookmarkStore.Tree.Move(node, folder, int.MaxValue);
            BookmarkStore.Save();
        }
        ShowBookmarkToast(node, folder is null ? "Bookmarked" : $"Bookmarked to {folder.Title}");
        BookmarksChanged();
    }

    /// <summary>Deleted at once, with Undo for a moment, as Chrome does, rather than asked about (Confirm.cs).</summary>
    private void RemoveBookmark(BookmarkNode node)
    {
        var tree = BookmarkStore.Tree;
        if (tree.Take(node) is not { } place) return;
        BookmarkStore.Save();
        BookmarksChanged();
        ShowUndo($"Deleted “{node.Title}”", () =>
        {
            if (tree != BookmarkStore.Tree || tree.ById(node.Id) is not null) return;
            tree.PutBack(node, place);
            BookmarkStore.Save();
            BookmarksChanged();
        });
    }

    private void BookmarksChanged()
    {
        if (_libraryOpen && _libraryPage == "bookmarks") ShowLibraryBookmarks();
    }

    // ───────────────────────── The toast and the editor ─────────────────────────

    private void ShowBookmarkToast(BookmarkNode node, string text)
    {
        HideUndo();
        _toastBookmark = node;
        BookmarkToastText.Text = text;
        BookmarkToast.Visibility = Visibility.Visible;
        BookmarkToast.Opacity = 1;
        BookmarkToast.IsHitTestVisible = true;
        Spring.To(BookmarkToast, "Translation", new Vector3(0, 0, 32), 0.25, 0, from: new Vector3(0, 8, 32));
        if (_bookmarkToastTimer is null)
        {
            _bookmarkToastTimer = DispatcherQueue.CreateTimer();
            _bookmarkToastTimer.IsRepeating = false;
            _bookmarkToastTimer.Interval = TimeSpan.FromSeconds(3);
            _bookmarkToastTimer.Tick += (_, _) => HideBookmarkToast();
        }
        _bookmarkToastTimer.Stop();
        _bookmarkToastTimer.Start();
    }

    private void HideBookmarkToast()
    {
        // Not while its editor's open, or the pointer's on it.
        if (_bookmarkEditorOpen) return;
        BookmarkToast.Opacity = 0;
        BookmarkToast.IsHitTestVisible = false;
    }

    private void BookmarkToastEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_toastBookmark is { } node && BookmarkStore.Tree.ById(node.Id) is not null)
            ShowBookmarkEditor(node, BookmarkToast, FlyoutPlacementMode.Top);
    }

    /// <summary>
    /// A bookmark's name and folder, with Remove; a folder's name, with Delete. Changes are kept as
    /// the editor closes, however it closes; Enter closes it.
    /// </summary>
    private void ShowBookmarkEditor(BookmarkNode node, FrameworkElement anchor, FlyoutPlacementMode placement, FlyoutShowOptions? options = null)
    {
        var tree = BookmarkStore.Tree;
        var panel = new StackPanel { Spacing = 10, Width = 280 };
        var name = new TextBox
        {
            Header = node.IsFolder ? "Folder name" : "Name",
            Text = node.Title,
            IsSpellCheckEnabled = false,
        };
        panel.Children.Add(name);

        ComboBox? folders = null;
        var choices = new List<BookmarkNode?> { null };
        if (!node.IsFolder)
        {
            folders = new ComboBox { Header = "Folder", HorizontalAlignment = HorizontalAlignment.Stretch };
            folders.Items.Add("Bookmarks");
            foreach (var (folder, path) in tree.Folders())
            {
                choices.Add(folder);
                folders.Items.Add(path);
            }
            folders.SelectedIndex = Math.Max(0, choices.IndexOf(tree.ParentOf(node)));
            panel.Children.Add(folders);
        }

        var buttons = new Grid();
        var remove = new Button { Content = node.IsFolder ? "Delete" : "Remove" };
        var done = new Button { Content = "Done", Style = (Style)Application.Current.Resources["AccentButtonStyle"], HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(remove);
        buttons.Children.Add(done);
        panel.Children.Add(buttons);

        var flyout = new Flyout { Content = panel, Placement = placement };
        var removed = false;
        remove.Click += (_, _) =>
        {
            flyout.Hide();
            removed = true;
            RemoveBookmark(node);
            if (_toastBookmark == node) HideBookmarkToast();
        };
        done.Click += (_, _) => flyout.Hide();
        name.KeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Enter) return;
            e.Handled = true;
            flyout.Hide();
        };
        flyout.Opened += (_, _) =>
        {
            _bookmarkEditorOpen = true;
            name.Focus(FocusState.Keyboard);
            name.SelectAll();
        };
        flyout.Closed += (_, _) =>
        {
            _bookmarkEditorOpen = false;
            if (!removed && tree.ById(node.Id) is not null)
            {
                var title = name.Text.Trim();
                if (title.Length > 0) node.Title = title;
                if (folders is not null && folders.SelectedIndex >= 0 && choices[folders.SelectedIndex] is var target
                    && tree.ParentOf(node) != target)
                    tree.Move(node, target, int.MaxValue);
                BookmarkStore.Save();
                BookmarksChanged();
            }
            _bookmarkToastTimer?.Stop();
            _bookmarkToastTimer?.Start();
        };
        if (options is not null) flyout.ShowAt(anchor, options);
        else flyout.ShowAt(anchor);
    }

    // ───────────────────────── The Library's section ─────────────────────────

    private void ShowLibraryBookmarks()
    {
        var tree = BookmarkStore.Tree;
        var query = LibrarySearch.Text.Trim();
        BookmarkList.Children.Clear();
        if (query.Length > 0)
            foreach (var node in tree.Search(query)) BookmarkList.Children.Add(BookmarkRow(node, 0, showFolder: true));
        else AddBookmarkRows(tree.Root, 0);

        var any = BookmarkList.Children.Count > 0;
        LibraryBookmarksEmpty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        LibraryBookmarksEmpty.Text = query.Length > 0 ? "No bookmarks match"
            : "No bookmarks yet. Press Ctrl+D on a page to save it here.";
    }

    private void AddBookmarkRows(List<BookmarkNode> nodes, int depth)
    {
        foreach (var node in nodes)
        {
            BookmarkList.Children.Add(BookmarkRow(node, depth, showFolder: false));
            if (node.IsFolder && _openBookmarkFolders.Contains(node.Id)) AddBookmarkRows(node.Children!, depth + 1);
        }
    }

    /// <summary>A folder (click to open or close it) or a page (click to open it in a new tab), indented by depth.</summary>
    private Grid BookmarkRow(BookmarkNode node, int depth, bool showFolder)
    {
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var row = new Grid
        {
            Padding = new Thickness(8 + depth * 18, 6, 8, 6),
            ColumnSpacing = 10,
            CornerRadius = new CornerRadius(8),
            Background = transparent,
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, Text = node.Title };
        text.Children.Add(title);
        Grid.SetColumn(text, 1);

        if (node.IsFolder)
        {
            var open = _openBookmarkFolders.Contains(node.Id);
            row.Children.Add(new FontIcon { Glyph = open ? "" : "", FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            var count = node.Children!.Count;
            title.Inlines.Clear();
            title.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = node.Title });
            title.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
            {
                Text = $"  {count}",
                Foreground = (Brush)Application.Current.Resources["DriftSecondaryTextBrush"],
            });
            row.Tapped += (_, _) =>
            {
                if (!_openBookmarkFolders.Remove(node.Id)) _openBookmarkFolders.Add(node.Id);
                ShowLibraryBookmarks();
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(row, $"Folder {node.Title}, {count} items, {(open ? "open" : "closed")}");
        }
        else
        {
            var glyph = new FontIcon { Glyph = GlobeGlyph, FontSize = 14, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center };
            var icon = new Image { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(glyph);
            row.Children.Add(icon);
            var uri = Uri.TryCreate(node.Url, UriKind.Absolute, out var parsed) ? parsed : null;
            var host = uri?.Host ?? node.Url!;
            if (host.StartsWith("www.")) host = host[4..];
            if (showFolder && BookmarkStore.Tree.ParentOf(node) is { } parent)
                host = $"{host} · {BookmarkStore.Tree.Folders().First(f => f.Folder == parent).Path}";
            text.Children.Add(new TextBlock
            {
                FontSize = 11,
                Text = host,
                Foreground = (Brush)Application.Current.Resources["DriftSecondaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            if (uri is not null) LoadBookmarkIcon(uri.Authority, glyph, icon);
            ToolTipService.SetToolTip(row, node.Url);
            row.Tapped += (_, _) =>
            {
                if (uri is not null) OpenPage(uri);
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(row, $"{node.Title}, {host}");
        }
        row.Children.Add(text);

        // Dragged to reorder (not in search results, which aren't in their order).
        if (!showFolder) WireBookmarkDrag(row, node);

        row.PointerEntered += (_, _) => row.Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
        row.PointerExited += (_, _) => row.Background = transparent;
        row.ContextRequested += (_, e) =>
        {
            e.Handled = true;
            var options = new FlyoutShowOptions();
            if (e.TryGetPosition(row, out var at)) options.Position = at;
            BookmarkMenu(node, row).ShowAt(row, options);
        };
        return row;
    }

    // ───────────────────────── Dragging ─────────────────────────

    private BookmarkNode? _draggedBookmark;

    /// <summary>
    /// A row can be dragged onto another: onto its top or bottom edge to go before or after it, onto
    /// the middle of a folder to go in it. A line shows where it'll go, or the folder lights up.
    /// Folders stay above pages wherever it's dropped (BookmarkTree).
    /// </summary>
    private void WireBookmarkDrag(Grid row, BookmarkNode node)
    {
        var accent = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        Border Line(VerticalAlignment edge) => new()
        {
            Height = 2,
            CornerRadius = new CornerRadius(1),
            Background = accent,
            VerticalAlignment = edge,
            Margin = new Thickness(0, edge == VerticalAlignment.Top ? -5 : 0, 0, edge == VerticalAlignment.Bottom ? -5 : 0),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        var above = Line(VerticalAlignment.Top);
        var below = Line(VerticalAlignment.Bottom);
        Grid.SetColumnSpan(above, 2);
        Grid.SetColumnSpan(below, 2);
        row.Children.Add(above);
        row.Children.Add(below);

        row.CanDrag = true;
        row.AllowDrop = true;
        row.DragStarting += (_, e) =>
        {
            _draggedBookmark = node;
            e.Data.SetText(node.Url ?? node.Title);
            e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
        };
        row.DropCompleted += (_, _) => _draggedBookmark = null;

        void Show(BookmarkDrop? where)
        {
            above.Visibility = where == BookmarkDrop.Before ? Visibility.Visible : Visibility.Collapsed;
            below.Visibility = where == BookmarkDrop.After ? Visibility.Visible : Visibility.Collapsed;
            row.Background = where == BookmarkDrop.Into
                ? (Brush)Application.Current.Resources["SubtleFillColorTertiaryBrush"]
                : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }

        BookmarkDrop? Where(DragEventArgs e)
        {
            if (_draggedBookmark is not { } dragged || dragged == node) return null;
            var y = e.GetPosition(row).Y;
            var h = Math.Max(1, row.ActualHeight);
            BookmarkDrop where;
            if (node.IsFolder)
            {
                // An open folder's pages are right below it: its lower part means in it.
                var open = _openBookmarkFolders.Contains(node.Id);
                where = y < h * 0.25 ? BookmarkDrop.Before
                    : y > h * 0.75 && !open ? BookmarkDrop.After
                    : BookmarkDrop.Into;
            }
            else where = y < h / 2 ? BookmarkDrop.Before : BookmarkDrop.After;
            // Nowhere it can go: a folder into itself, or what's inside it.
            if (dragged.IsFolder && (node == dragged || IsInside(node, dragged))) return null;
            return where;
        }

        row.DragOver += (_, e) =>
        {
            var where = Where(e);
            e.AcceptedOperation = where is null
                ? Windows.ApplicationModel.DataTransfer.DataPackageOperation.None
                : Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
            e.DragUIOverride.IsCaptionVisible = false;
            e.DragUIOverride.IsGlyphVisible = false;
            Show(where);
        };
        row.DragLeave += (_, _) => Show(null);
        row.Drop += (_, e) =>
        {
            Show(null);
            if (Where(e) is not { } where || _draggedBookmark is not { } dragged) return;
            _draggedBookmark = null;
            if (!BookmarkStore.Tree.Drop(dragged, node, where)) return;
            if (where == BookmarkDrop.Into) _openBookmarkFolders.Add(node.Id);
            BookmarkStore.Save();
            ShowLibraryBookmarks();
        };
    }

    /// <summary>Whether a node is somewhere inside a folder.</summary>
    private static bool IsInside(BookmarkNode node, BookmarkNode folder) =>
        folder.Children is { } children && children.Any(c => c == node || IsInside(node, c));

    private static async void LoadBookmarkIcon(string site, FontIcon glyph, Image image)
    {
        if (await History.IconAsync(site) is not { } icon) return;
        image.Source = icon;
        glyph.Visibility = Visibility.Collapsed;
    }

    private MenuFlyout BookmarkMenu(BookmarkNode node, FrameworkElement row)
    {
        var menu = new MenuFlyout();
        MenuFlyoutItem Item(string text, string glyph, Action run)
        {
            var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
            item.Click += (_, _) => run();
            menu.Items.Add(item);
            return item;
        }
        if (node.IsFolder)
        {
            Item("Rename", "", () => ShowBookmarkEditor(node, row, FlyoutPlacementMode.Bottom));
            Item("New folder inside", "", () => NewBookmarkFolder(node));
            menu.Items.Add(new MenuFlyoutSeparator());
            Item("Delete", "", () => RemoveBookmark(node));
        }
        else
        {
            var uri = Uri.TryCreate(node.Url, UriKind.Absolute, out var parsed) ? parsed : null;
            if (uri is not null) Item("Open in new tab", "", () => OpenPage(uri));
            Item("Copy link", "", () =>
            {
                var package = new DataPackage();
                package.SetText(node.Url);
                Clipboard.SetContent(package);
                ShowCopiedToast();
            });
            Item("Edit", "", () => ShowBookmarkEditor(node, row, FlyoutPlacementMode.Bottom));
            menu.Items.Add(new MenuFlyoutSeparator());
            Item("Delete", "", () => RemoveBookmark(node));
        }
        return menu;
    }

    private void BookmarksNewFolder_Click(object sender, RoutedEventArgs e) => NewBookmarkFolder(null);

    /// <summary>A new folder, named straight away.</summary>
    private void NewBookmarkFolder(BookmarkNode? parent)
    {
        var folder = BookmarkStore.Tree.NewFolder("New folder", parent);
        if (parent is not null) _openBookmarkFolders.Add(parent.Id);
        BookmarkStore.Save();
        if (LibrarySearch.Text.Length > 0) LibrarySearch.Text = "";
        ShowLibraryBookmarks();
        var index = RowIndexOf(folder);
        if (index >= 0 && index < BookmarkList.Children.Count && BookmarkList.Children[index] is Grid row)
            ShowBookmarkEditor(folder, row, FlyoutPlacementMode.Bottom);
    }

    /// <summary>Where a node's row is in the list as it's shown (folders closed or open).</summary>
    private int RowIndexOf(BookmarkNode target)
    {
        var index = 0;
        bool Walk(List<BookmarkNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node == target) return true;
                index++;
                if (node.IsFolder && _openBookmarkFolders.Contains(node.Id) && Walk(node.Children!)) return true;
            }
            return false;
        }
        return Walk(BookmarkStore.Tree.Root) ? index : -1;
    }

    // ───────────────────────── The tab's menu ─────────────────────────

    /// <summary>A tab's right-click menu: "Bookmark", or "Remove bookmark" for a page already saved.</summary>
    private void ShowBookmarkMenuItem(MenuFlyout menu)
    {
        var tab = menu.Items.Select(i => i.Tag).OfType<BrowserTab>().FirstOrDefault();
        var item = menu.Items.OfType<MenuFlyoutItem>().FirstOrDefault(i => i.Tag as string == "bookmark");
        if (item is null)
        {
            item = new MenuFlyoutItem { Tag = "bookmark" };
            var close = menu.Items.OfType<MenuFlyoutItem>().FirstOrDefault(i => i.Text.StartsWith("Close tab"));
            var at = close is null ? menu.Items.Count : menu.Items.IndexOf(close);
            menu.Items.Insert(at, item);
        }
        var url = tab?.View.CoreWebView2?.Source ?? tab?.Url;
        item.Visibility = tab is not null && CanBookmark(url) ? Visibility.Visible : Visibility.Collapsed;
        if (tab is null || !CanBookmark(url)) return;
        var saved = BookmarkStore.Tree.Find(url!);
        item.Text = saved is null ? "Bookmark" : "Remove bookmark";
        item.Icon = new FontIcon { Glyph = saved is null ? "" : "" };
        item.Click -= TabBookmark_Click;
        item.Click += TabBookmark_Click;
        item.DataContext = tab;
    }

    private void TabBookmark_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not BrowserTab tab) return;
        var url = tab.View.CoreWebView2?.Source ?? tab.Url;
        if (url is null) return;
        if (BookmarkStore.Tree.Find(url) is { } saved) RemoveBookmark(saved);
        else BookmarkTab(tab, null);
    }
}

/// <summary>The bookmarks, read once and saved after every change.</summary>
internal static class BookmarkStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Drift", "bookmarks.json");

    private static BookmarkTree? _tree;

    public static BookmarkTree Tree => _tree ??= Load();

    private static BookmarkTree Load()
    {
        try
        {
            if (File.Exists(FilePath)) return BookmarkTree.FromJson(File.ReadAllText(FilePath));
        }
        catch { }
        return new BookmarkTree();
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            // Written whole to the side, then swapped in, so a crash mid-write can't lose them.
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, Tree.ToJson());
            File.Move(temp, FilePath, overwrite: true);
        }
        catch { }
    }
}
