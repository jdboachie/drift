using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using Drift.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Drift;

/// <summary>A question (Warnings.cs) in a dialog, Cancel the default; false if it's cancelled, or can't be asked.</summary>
public static class ConfirmDialog
{
    public static async Task<bool> AskAsync(XamlRoot root, Warning warning)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = warning.Title,
            Content = new TextBlock { Text = warning.Body, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = warning.Action,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch (Exception)
        {
            // Another dialog's already open: nothing is done.
            return false;
        }
    }
}

/// <summary>
/// Asking before what can't be undone, and Undo for what can:
/// <list type="bullet">
/// <item>Closing a window takes a second press soon after the first (Core/CloseAgain.cs), unless
/// it's turned off in Settings.</item>
/// <item>Closing a window that would take running downloads, private windows, tabs that aren't kept
/// (On close: keep pinned tabs) or unsaved changes with it.</item>
/// <item>Closing or unloading pages whose sites say they have unsaved changes. A page's own "Leave
/// site?" never comes, as its view is closed rather than navigated away, so Drift asks the page
/// itself, the way the browser would, and asks you for it.</item>
/// <item>Undo, for a moment, after a bookmark's deleted or the tab list's cleared.</item>
/// </list>
/// </summary>
public sealed partial class MainWindow
{
    private bool _closeConfirmed;
    private bool _closeAsking;
    private readonly CloseAgain _closeAgain = new();
    private DispatcherQueueTimer? _closeAgainTimer;

    private async Task<bool> AskAsync(Warning warning)
    {
        // The sidebar stays out while it's asked, as it does for a menu.
        _openSidebarMenus++;
        try
        {
            return await ConfirmDialog.AskAsync(Root.XamlRoot, warning);
        }
        finally
        {
            _openSidebarMenus = Math.Max(0, _openSidebarMenus - 1);
        }
    }

    // ---- Closing the window ----

    /// <summary>Alt+F4 and the taskbar's Close come here first, as the caption's × does.</summary>
    private void WatchClosing()
    {
        AppWindow.Closing += (_, e) =>
        {
            if (_closeConfirmed) return;
            e.Cancel = true;
            RequestClose("Press again to close");
        };
    }

    /// <summary>
    /// The window closes, once you've said so if anything would be lost with it; otherwise on a
    /// second press, saying <paramref name="again"/> after the first. The command bar's Close Window
    /// is meant already, so it doesn't say anything.
    /// </summary>
    private async void RequestClose(string? again = null)
    {
        if (_closeConfirmed || _closeAsking) return;
        _closeAsking = true;
        try
        {
            if (await CloseWarningAsync() is { } warning)
            {
                if (!await AskAsync(warning)) return;
            }
            else if (again is not null && Settings.Current.CloseTwice && !_closeAgain.Press(Environment.TickCount64))
            {
                ShowCloseAgain(again);
                return;
            }
            CloseAgainHint.Opacity = 0;
            _closeConfirmed = true;
            Close();
        }
        finally
        {
            _closeAsking = false;
        }
    }

    /// <summary>"Click again to close", under the window buttons, while a second press would close.</summary>
    private void ShowCloseAgain(string text)
    {
        CloseAgainText.Text = text;
        CloseAgainHint.Visibility = Visibility.Visible;
        CloseAgainHint.Opacity = 1;
        Spring.To(CloseAgainHint, "Translation", new Vector3(0, 0, 32), 0.25, 0, from: new Vector3(0, -6, 32));
        if (_closeAgainTimer is null)
        {
            _closeAgainTimer = DispatcherQueue.CreateTimer();
            _closeAgainTimer.IsRepeating = false;
            _closeAgainTimer.Interval = TimeSpan.FromMilliseconds(CloseAgain.WindowMs);
            _closeAgainTimer.Tick += (_, _) => CloseAgainHint.Opacity = 0;
        }
        _closeAgainTimer.Stop();
        _closeAgainTimer.Start();
    }

    private async Task<Warning?> CloseWarningAsync()
    {
        // The main window takes every window's downloads with it; a private one, the private ones
        // if it's the last.
        var downloads = Downloads.Items.Count(i => i.IsRunning && (!_private || (i.IsPrivate && PrivateWindows.Count <= 1)));
        var privateWindows = _private ? 0 : PrivateWindows.Count;
        var dropped = !_private && Settings.Current.OnClose == CloseBehavior.KeepPinned ? AllTabs.Count(t => !t.IsPinned) : 0;
        var unsaved = (await PagesWithChangesAsync(AllTabs)).Count;
        if (!_private)
            foreach (var window in PrivateWindows.ToList())
                unsaved += (await window.PagesWithChangesAsync(window.AllTabs)).Count;
        return Warnings.CloseWindow(_private, downloads, privateWindows, dropped, unsaved);
    }

    // ---- Unsaved changes ----

    /// <summary>
    /// What the page's beforeunload would say, asked as the browser asks it: only once you've done
    /// something on the page, and yes if a handler cancels the event or gives it a message.
    /// </summary>
    private const string LeaveCheck = """
        (() => {
          if (navigator.userActivation && !navigator.userActivation.hasBeenActive) return false;
          let asked = false;
          const e = new Event('beforeunload', { cancelable: true });
          Object.defineProperty(e, 'returnValue', {
            configurable: true,
            get: () => '',
            set: v => { if (v !== '' && v != null && v !== true) asked = true; },
          });
          const own = window.onbeforeunload;
          try {
            window.onbeforeunload = null;
            window.dispatchEvent(e);
            if (typeof own === 'function') {
              const r = own.call(window, e);
              if (r != null && r !== '' && r !== true) asked = true;
            }
          } catch { } finally {
            window.onbeforeunload = own;
          }
          return asked || e.defaultPrevented;
        })()
        """;

    /// <summary>The sites, of these tabs' open pages, that say they have unsaved changes.</summary>
    private async Task<List<string>> PagesWithChangesAsync(IEnumerable<BrowserTab> tabs)
    {
        var open = tabs.Where(t => !t.IsSplitRow && !t.IsUnloaded && t.View.CoreWebView2 is not null).Distinct().ToList();
        if (open.Count == 0) return [];
        var answers = await Task.WhenAll(open.Select(WouldAskAsync));
        return open.Where((_, i) => answers[i]).Select(t => SiteOf(t) is { Length: > 0 } site ? site : t.Title).ToList();
    }

    private static async Task<bool> WouldAskAsync(BrowserTab tab)
    {
        try
        {
            var check = EvaluateAsync(tab.View.CoreWebView2, LeaveCheck, gesture: false);
            // A page that doesn't answer (busy, or showing its own dialog) isn't waited on.
            if (await Task.WhenAny(check, Task.Delay(1000)) != check) return false;
            return await check is { ValueKind: JsonValueKind.True };
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>True if these pages can go: none has unsaved changes, or you said to leave them.</summary>
    private async Task<bool> MayLeaveAsync(IEnumerable<BrowserTab> tabs) =>
        Warnings.LeavePages(await PagesWithChangesAsync(tabs)) is not { } warning || await AskAsync(warning);

    // ---- Undo ----

    private Action? _undo;
    private DispatcherQueueTimer? _undoTimer;

    /// <summary>"Deleted …" or "Cleared …" at the foot of the page, with Undo, for a few seconds (longer while the pointer's on it).</summary>
    private void ShowUndo(string text, Action undo)
    {
        _undo = undo;
        HideBookmarkToast();
        CopiedToast.Opacity = 0;
        UndoToastText.Text = text;
        UndoToast.Visibility = Visibility.Visible;
        UndoToast.Opacity = 1;
        UndoToast.IsHitTestVisible = true;
        Spring.To(UndoToast, "Translation", new Vector3(0, 0, 32), 0.25, 0, from: new Vector3(0, 8, 32));
        if (_undoTimer is null)
        {
            _undoTimer = DispatcherQueue.CreateTimer();
            _undoTimer.IsRepeating = false;
            _undoTimer.Interval = TimeSpan.FromSeconds(6);
            _undoTimer.Tick += (_, _) => HideUndo();
        }
        _undoTimer.Stop();
        _undoTimer.Start();
    }

    private void HideUndo()
    {
        _undo = null;
        _undoTimer?.Stop();
        UndoToast.Opacity = 0;
        UndoToast.IsHitTestVisible = false;
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        var undo = _undo;
        HideUndo();
        undo?.Invoke();
    }

    private void UndoToast_PointerEntered(object sender, PointerRoutedEventArgs e) => _undoTimer?.Stop();

    private void UndoToast_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_undo is not null) _undoTimer?.Start();
    }
}
