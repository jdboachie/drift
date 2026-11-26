using System;
using System.Linq;
using System.Text.Json;
using Drift.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;

namespace Drift;

/// <summary>
/// Right-click menus on the sidebar's own parts, as in Arc and Zen:
/// <list type="bullet">
/// <item>Back and Forward: the tab's pages that way, nearest first, to go straight to one.</item>
/// <item>The address: Copy link, and Share… (Windows' own share).</item>
/// <item>The sidebar's empty space: New tab, New folder, New live folder ›, as the + button has.</item>
/// </list>
/// </summary>
public sealed partial class MainWindow
{
    private const int MaxHistoryItems = 15;

    internal void Back_ContextRequested(UIElement sender, ContextRequestedEventArgs e) => ShowHistoryMenu(sender, e, back: true);

    internal void Forward_ContextRequested(UIElement sender, ContextRequestedEventArgs e) => ShowHistoryMenu(sender, e, back: false);

    /// <summary>The page's history that way (the engine's own list), each a click away.</summary>
    private async void ShowHistoryMenu(UIElement sender, ContextRequestedEventArgs e, bool back)
    {
        e.Handled = true;
        if (_active?.View.CoreWebView2 is not { } core || sender is not FrameworkElement button) return;
        try
        {
            using var history = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Page.getNavigationHistory", "{}"));
            var current = history.RootElement.GetProperty("currentIndex").GetInt32();
            var entries = history.RootElement.GetProperty("entries").EnumerateArray()
                .Select(entry => (Id: entry.GetProperty("id").GetInt32(),
                                  Url: entry.GetProperty("url").GetString() ?? "",
                                  Title: entry.TryGetProperty("title", out var t) ? t.GetString() ?? "" : ""))
                .ToList();
            var way = back
                ? entries.Take(current).Reverse()
                : entries.Skip(current + 1);
            var menu = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
            foreach (var (id, url, title) in way.Take(MaxHistoryItems))
            {
                var item = new MenuFlyoutItem { Text = Shorten(string.IsNullOrWhiteSpace(title) ? url : title, 48) };
                ToolTipService.SetToolTip(item, url);
                item.Click += async (_, _) =>
                {
                    try { await core.CallDevToolsProtocolMethodAsync("Page.navigateToHistoryEntry", $"{{\"entryId\":{id}}}"); }
                    catch { /* The page has gone meanwhile. */ }
                };
                menu.Items.Add(item);
            }
            if (menu.Items.Count == 0) return;
            menu.Opened += SidebarMenu_Opened;
            menu.Closed += SidebarMenu_Closed;
            menu.ShowAt(button);
        }
        catch
        {
            // The engine didn't answer (the page is closing, or crashed).
        }
    }

    // ── The address ──

    internal void AddressMenu_Opening(object? sender, object e)
    {
        var page = IsPeekShown ? _peekView?.CoreWebView2?.Source : _active?.View.CoreWebView2?.Source;
        var has = !string.IsNullOrEmpty(page) && page != "about:blank";
        AddressBar.CopyItem.IsEnabled = AddressBar.ShareItem.IsEnabled = has;
    }

    internal void AddressCopy_Click(object sender, RoutedEventArgs e)
    {
        if (CopyCurrentLink()) ShowCopiedToast();
    }

    // The page Share… is for, until Windows asks for it.
    private (string Url, string Title)? _sharing;
    private bool _shareReady;

    /// <summary>Windows' share, for the page in front (or in Peek): its address and title.</summary>
    internal void AddressShare_Click(object sender, RoutedEventArgs e)
    {
        var core = IsPeekShown ? _peekView?.CoreWebView2 : _active?.View.CoreWebView2;
        if (core?.Source is not { Length: > 0 } url || url == "about:blank") return;
        _sharing = (url, string.IsNullOrWhiteSpace(core.DocumentTitle) ? url : core.DocumentTitle);
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        try
        {
            if (!_shareReady)
            {
                var manager = DataTransferManagerInterop.GetForWindow(hwnd);
                manager.DataRequested += (_, args) =>
                {
                    if (_sharing is not { } page) return;
                    args.Request.Data.Properties.Title = page.Title;
                    if (Uri.TryCreate(page.Url, UriKind.Absolute, out var uri)) args.Request.Data.SetWebLink(uri);
                    args.Request.Data.SetText(page.Url);
                };
                _shareReady = true;
            }
            DataTransferManagerInterop.ShowShareUIForWindow(hwnd);
        }
        catch (Exception error)
        {
            App.LogCrash(error);
        }
    }

    // ── The sidebar's empty space ──

    private void SidebarProfileMenu_Opening(object? sender, object e)
    {
        ProfileNewTabItem.KeyboardAcceleratorTextOverride = ShortcutText("newtab");
        ProfileNewFolderItem.KeyboardAcceleratorTextOverride = ShortcutText("folder");
        ProfileLiveFolderItem.Visibility = Ui.Shown(!_private);
        ProfileGitHubItem.IsEnabled = !LiveFolders.Any();
        // The Profile's own (Profiles.cs); a private window has the one sidebar, and your default Profile stays.
        foreach (var item in new MenuFlyoutItemBase[] { ProfileMenuSeparator, ProfileRenameItem, ProfileIconItem, ProfileHeaderItem })
            item.Visibility = Ui.Shown(!_private);
        ProfileHeaderItem.Text = Settings.Current.ShowProfileHeader ? "Hide Profile header" : "Show Profile header";
        ProfileDeleteSeparator.Visibility = ProfileDeleteItem.Visibility = Ui.Shown(!_private && ProfileIds.CanDelete(_profileShown.Engine.Id));
    }

    private void SidebarProfileMenu_Closed(object? sender, object e)
    {
        SidebarMenu_Closed(sender, e);
        // Its submenus can be left drawn where the window starts (GhostPopups.cs).
        _ghostPopups.MenuClosed(ProfileGitHubItem);
    }
}
