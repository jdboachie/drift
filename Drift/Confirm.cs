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
            CloseAgainHint.Hide();
            _closeConfirmed = true;
            Close();
        }
        finally
        {
            _closeAsking = false;
        }
    }

    /// <summary>"Click again to close", under the window buttons, while a second press would close.</summary>
    private void ShowCloseAgain(string text) => CloseAgainHint.Show(text, TimeSpan.FromMilliseconds(CloseAgain.WindowMs));

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
          if (document.contentType === 'application/pdf') return 'pdf';
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
        // A PDF's title without the viewer's * for unsaved edits: the question says that already.
        return open.Where((_, i) => answers[i]).Select(t => SiteOf(t) is { Length: > 0 } site ? site : t.Title.TrimStart('*')).ToList();
    }

    private static async Task<bool> WouldAskAsync(BrowserTab tab)
    {
        try
        {
            var check = EvaluateAsync(tab.View.CoreWebView2, LeaveCheck, gesture: false);
            // A page that doesn't answer (busy, or showing its own dialog) isn't waited on.
            if (await Task.WhenAny(check, Task.Delay(1000)) != check) return false;
            var answer = await check;
            // A PDF's changes (drawn, highlighted, typed: PdfTools.cs) are the viewer's, which the page's
            // beforeunload doesn't know about; the viewer marks them with a * before the tab's title.
            if (answer is { ValueKind: JsonValueKind.String } && answer.Value.GetString() == "pdf")
                return PdfViewers.HasUnsavedEdits(tab.View.CoreWebView2.DocumentTitle);
            return answer is { ValueKind: JsonValueKind.True };
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

    private bool _toastWired;

    // What to finish once an Undo toast has gone without Undo (timed out, closed, or another took its place).
    private Action? _toastGone;

    /// <summary>
    /// "Deleted …" or "Cleared …" at the foot of the page, with Undo, for a few seconds (longer while
    /// the pointer's on it). <paramref name="gone"/>, if any, finishes the job once it's gone without Undo.
    /// </summary>
    private void ShowUndo(string text, Action undo, Action? gone = null)
    {
        ShowToast(text, TimeSpan.FromSeconds(6), choices: false, ("Undo", undo));
        _toastGone = gone;
    }

    /// <summary>A toast's leaving without Undo: whatever it held back is done now.</summary>
    private void ToastGone()
    {
        var gone = _toastGone;
        _toastGone = null;
        gone?.Invoke();
    }

    private void HideUndo() => HideToast();

    /// <summary>
    /// A notice at the foot of the page with up to two buttons, for <paramref name="time"/> or longer
    /// while the pointer's on it. A button closes it, the first in the accent colour: Undo, or a
    /// notice's first choice (<paramref name="choices"/>).
    /// </summary>
    private void ShowToast(string text, TimeSpan time, bool choices, params (string Label, Action Act)[] actions) =>
        ShowFootToast(text, time, choices, null, null, actions);

    /// <summary>
    /// A notice: <paramref name="icon"/>, <paramref name="title"/> over <paramref name="detail"/>, and
    /// its choices as buttons, the first coloured.
    /// </summary>
    private void ShowNotice(FrameworkElement icon, string title, string detail, TimeSpan time, params (string Label, Action Act)[] actions) =>
        ShowFootToast(title, time, choices: true, icon, detail, actions);

    private void ShowFootToast(string text, TimeSpan time, bool choices, FrameworkElement? icon, string? detail, (string Label, Action Act)[] actions)
    {
        // Another in its place: the one before is done with.
        ToastGone();
        if (!_toastWired)
        {
            _toastWired = true;
            UndoToast.Expired += ToastGone;
        }
        HideBookmarkToast();
        CopiedToast.Hide();
        // Undo (or a notice's choice) takes it from here: nothing's to be finished.
        var buttons = actions.Select(a => new ToastAction(a.Label, () => { _toastGone = null; a.Act(); })).ToArray();
        UndoToast.Show(text, time, buttons, notice: choices, icon: icon, detail: detail);
    }

    private void HideToast()
    {
        ToastGone();
        UndoToast.Hide();
    }
}
