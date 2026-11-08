using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Drift.Models;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;

namespace Drift;

/// <summary>
/// Arc's automatic picture-in-picture. Leave a tab with a video playing (with its sound on) and the
/// video floats in Chromium's own picture-in-picture window, over everything, with its controls:
/// play/pause, back to tab, close. Come back to the tab and it's back in the page; Back to tab in
/// the window brings you back to it. A page that floats itself, like a Google Meet call (it says
/// so through the media session), does that instead. Which video, and when, is AutoPip (Core, tested).
/// The page side is in PipScript, injected into every page.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The tab whose video (or page) is floating, if any.</summary>
    private BrowserTab? _pipTab;

    /// <summary>
    /// The tab being asked to float itself (a meeting), for the moment it takes: its window arrives
    /// as a new about:blank window, which Drift hosts (PlayerWindow) instead of opening it in Peek
    /// or a tab.
    /// </summary>
    private BrowserTab? _pipOpening;

    /// <summary>
    /// Page side: notes when each video last started playing, and whether the page has a
    /// media-session "enterpictureinpicture" handler (a meeting floating itself); reports entering
    /// and leaving picture-in-picture, leaving with whether the video plays on (Back to tab) or not
    /// (Close). window.__driftPip lists the videos and floats one, or the page, or ends it; Drift
    /// calls it through DevTools, with a user gesture, since picture-in-picture needs one.
    /// A page's own floating window (Document Picture-in-Picture, a meeting's) reports as "enter:page".
    /// </summary>
    private static string PipScript() => """
          const played = new WeakMap(); let clock = 0;
          document.addEventListener('play', (e) => { if (e.target instanceof HTMLVideoElement) played.set(e.target, ++clock); }, true);
          let floatsItself = null;
          try {
            const session = navigator.mediaSession;
            const set = session.setActionHandler.bind(session);
            session.setActionHandler = function (action, handler) {
              if (action === 'enterpictureinpicture') floatsItself = handler;
              return set(action, handler);
            };
          } catch { }
          document.addEventListener('enterpictureinpicture', () => post(token + ':pip:enter'), true);
          // The window's Close pauses the video only just after this event: look a moment later.
          document.addEventListener('leavepictureinpicture', (e) => {
            const video = e.target;
            setTimeout(() => post(token + ':pip:leave:' + (video.paused ? 0 : 1)), 150);
          }, true);
          if (window.documentPictureInPicture) {
            documentPictureInPicture.addEventListener('enter', (e) => {
              post(token + ':pip:enter:page');
              e.window.addEventListener('pagehide', () => post(token + ':pip:leave:0'));
            });
          }
          const videos = () => [...document.querySelectorAll('video')];
          Object.defineProperty(window, '__driftPip', { value: Object.freeze({
            state: () => JSON.stringify({
              self: !!floatsItself,
              videos: videos().map((v) => {
                const r = v.getBoundingClientRect();
                return [!v.paused && !v.ended && v.readyState > 2, v.muted || v.volume === 0, r.width, r.height,
                        !v.disablePictureInPicture, played.get(v) || 0];
              }),
            }),
            float: (i) => {
              const v = videos()[i];
              if (!v) return 'gone';
              return v.requestPictureInPicture().then(() => 'ok', (e) => e.name);
            },
            floatPage: () => { try { floatsItself?.({ action: 'enterpictureinpicture' }); return 'ok'; } catch (e) { return e.name; } },
            end: () => {
              if (document.pictureInPictureElement) document.exitPictureInPicture();
              else if (window.documentPictureInPicture && documentPictureInPicture.window) documentPictureInPicture.window.close();
            },
          }) });
        """;

    /// <summary>You've left a tab: if it's playing a video, the video floats (AutoPip decides).</summary>
    private async Task FloatOnLeaveAsync(BrowserTab tab)
    {
        if (!Settings.Current.AutoPictureInPicture || tab.View.CoreWebView2 is not { } core) return;
        try
        {
            if (await EvaluateAsync(core, "window.__driftPip && window.__driftPip.state()", gesture: false) is not { } state) return;
            using var json = JsonDocument.Parse(state.GetString() ?? "{}");
            var self = json.RootElement.GetProperty("self").GetBoolean();
            var videos = json.RootElement.GetProperty("videos").EnumerateArray().Select(v => new PageVideo(
                v[0].GetBoolean(), v[1].GetBoolean(), v[2].GetDouble(), v[3].GetDouble(), v[4].GetBoolean(), v[5].GetDouble())).ToList();
            var action = AutoPip.OnLeave(Settings.Current.AutoPictureInPicture, self, videos);
            // Back already, or the tab's going (unloaded or closed).
            // (Or still showing, beside the tab in front in a split.)
            if (action.Kind == PipKind.None || tab == _active || tab.View.Visibility == Visibility.Visible
                || tab.IsUnloaded || !AllTabs.Contains(tab)) return;
            _pipOpening = tab;
            try
            {
                await EvaluateAsync(core, action.Kind == PipKind.Video
                    ? $"window.__driftPip.float({action.Video})"
                    : "window.__driftPip.floatPage()", gesture: true);
            }
            finally
            {
                _pipOpening = null;
            }
        }
        catch
        {
            // The page went away meanwhile, or wouldn't: it just doesn't float.
        }
    }

    /// <summary>Back on the tab whose video's floating: it's back in the page.</summary>
    private async Task EndPipAsync(BrowserTab tab)
    {
        if (tab.View.CoreWebView2 is not { } core) return;
        try { await EvaluateAsync(core, "window.__driftPip && window.__driftPip.end()", gesture: false); }
        catch { }
    }

    /// <summary>A page went into picture-in-picture, or came out ("leave:1" still playing).</summary>
    private void OnPipMessage(BrowserTab tab, string message)
    {
        if (message.StartsWith("enter", StringComparison.Ordinal))
        {
            _pipTab = tab;
            // Chromium's own window shows a hidden tab's video; a page's own window needs the tab drawn.
            if (message == "enter:page") KeepDrawing(tab, true);
            else if (tab.View.CoreWebView2 is { } core) Taskbar.HideBrowserPlayer(core.BrowserProcessId);
            return;
        }
        if (!message.StartsWith("leave:", StringComparison.Ordinal)) return;
        if (_pipTab == tab) _pipTab = null;
        KeepDrawing(tab, false);
        if (AutoPip.WentBackToTab(stillPlaying: message == "leave:1") && tab != _active && AllTabs.Contains(tab))
        {
            Activate(tab);
            BringToFront();
        }
    }

    /// <summary>
    /// A tab whose page floats itself (a meeting, in its own window) stays "shown", transparent and
    /// out of reach behind the tab in front: the engine stops drawing a hidden tab's video, and the
    /// window went black (Chromium's own video window is exempt). Back to normal when it stops floating.
    /// </summary>
    private void KeepDrawing(BrowserTab tab, bool floating)
    {
        if (tab == _active) floating = false;
        tab.View.Opacity = floating ? 0 : 1;
        tab.View.IsHitTestVisible = !floating;
        if (floating) tab.View.Visibility = Visibility.Visible;
        else if (tab != _active) tab.View.Visibility = Visibility.Collapsed;
    }

    /// <summary>Runs script in a page as if from the user (a user gesture), and returns its value.</summary>
    private static async Task<JsonElement?> EvaluateAsync(CoreWebView2 core, string expression, bool gesture)
    {
        var args = JsonSerializer.Serialize(new { expression, awaitPromise = true, returnByValue = true, userGesture = gesture });
        var reply = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", args);
        using var json = JsonDocument.Parse(reply);
        return json.RootElement.TryGetProperty("result", out var result) && result.TryGetProperty("value", out var value)
            ? value.Clone()
            : null;
    }

    private void BringToFront()
    {
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized } presenter)
            presenter.Restore();
        Activate();
    }
}
