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

    // ---- Undo, and other notices with a button or two ----

    private (string Label, Action Act)[] _toastActions = [];
    private DispatcherQueueTimer? _toastTimer;

    /// <summary>"Deleted …" or "Cleared …" at the foot of the page, with Undo, for a few seconds (longer while the pointer's on it).</summary>
    private void ShowUndo(string text, Action undo) => ShowToast(text, TimeSpan.FromSeconds(6), choices: false, ("Undo", undo));

    private void HideUndo() => HideToast();

    /// <summary>
    /// A notice at the foot of the page with up to two buttons, for <paramref name="time"/> or longer
    /// while the pointer's on it. A button closes it. Undo is a plain text button; a notice's
    /// <paramref name="choices"/> are buttons, the first the coloured one.
    /// </summary>
    private void ShowToast(string text, TimeSpan time, bool choices, params (string Label, Action Act)[] actions)
    {
        var plain = new Thickness(8, 3, 8, 3);
        ToastAction1.Style = (Style)Application.Current.Resources[choices ? "AccentButtonStyle" : "SubtleButtonStyle"];
        ToastAction2.Style = (Style)Application.Current.Resources[choices ? "DefaultButtonStyle" : "SubtleButtonStyle"];
        foreach (var button in new[] { ToastAction1, ToastAction2 })
        {
            if (choices) button.ClearValue(Control.PaddingProperty);
            else button.Padding = plain;
        }
        UndoToast.Padding = choices ? new Thickness(14, 10, 10, 10) : new Thickness(14, 4, 6, 4);
        UndoToast.CornerRadius = new CornerRadius(choices ? 12 : 10);
        // Room between what it says and what you can do about it.
        ToastTexts.Margin = new Thickness(0, 0, choices ? 12 : 0, 0);
        UndoToastText.FontWeight = choices ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        ToastIcon.Content = null;
        ToastIcon.Visibility = Visibility.Collapsed;
        ToastDetail.Visibility = Visibility.Collapsed;
        _toastActions = actions;
        HideBookmarkToast();
        CopiedToast.Opacity = 0;
        UndoToastText.Text = text;
        Button[] buttons = [ToastAction1, ToastAction2];
        for (var i = 0; i < buttons.Length; i++)
        {
            buttons[i].Visibility = i < actions.Length ? Visibility.Visible : Visibility.Collapsed;
            if (i < actions.Length) buttons[i].Content = actions[i].Label;
        }
        UndoToast.Visibility = Visibility.Visible;
        UndoToast.Opacity = 1;
        UndoToast.IsHitTestVisible = true;
        Spring.To(UndoToast, "Translation", new Vector3(0, 0, 32), 0.25, 0, from: new Vector3(0, 8, 32));
        if (_toastTimer is null)
        {
            _toastTimer = DispatcherQueue.CreateTimer();
            _toastTimer.IsRepeating = false;
            _toastTimer.Tick += (_, _) => HideToast();
        }
        _toastTimer.Interval = time;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    /// <summary>
    /// A notice: <paramref name="icon"/>, <paramref name="title"/> over <paramref name="detail"/>, and
    /// its choices as buttons, the first coloured.
    /// </summary>
    private void ShowNotice(FrameworkElement icon, string title, string detail, TimeSpan time, params (string Label, Action Act)[] actions)
    {
        ShowToast(title, time, choices: true, actions);
        ToastIcon.Content = icon;
        ToastIcon.Visibility = Visibility.Visible;
        ToastDetail.Text = detail;
        ToastDetail.Visibility = Visibility.Visible;
    }

    private void HideToast()
    {
        _toastActions = [];
        _toastTimer?.Stop();
        UndoToast.Opacity = 0;
        UndoToast.IsHitTestVisible = false;
        // Out the way it came in: down a little as it fades.
        Spring.To(UndoToast, "Translation", new Vector3(0, 8, 32), 0.25, 0);
    }

    private void ToastAction_Click(object sender, RoutedEventArgs e)
    {
        var index = sender == ToastAction2 ? 1 : 0;
        var act = index < _toastActions.Length ? _toastActions[index].Act : null;
        HideToast();
        act?.Invoke();
    }

    private void UndoToast_PointerEntered(object sender, PointerRoutedEventArgs e) => _toastTimer?.Stop();

    private void UndoToast_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_toastActions.Length > 0) _toastTimer?.Start();
    }
}
