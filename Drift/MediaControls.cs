using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using Drift.Models;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;

namespace Drift;

/// <summary>
/// Arc's mini player. A tab playing sound has a speaker on its row (click it to mute the tab, and
/// again to unmute), and a controller at the bottom of the sidebar is for the tab playing (MediaBar,
/// in Core, says which): its icon (click to go to it), back, play/pause, forward, and mute. Back and
/// forward go to the previous or next track where the site has them (as its own media keys would),
/// otherwise 10 seconds back or on. Paused, the controller stays a while, then goes.
/// Hovered, it grows upward over the tab list into a card: the cover or thumbnail where the site gives
/// one (MediaArtwork), the title (scrolling by when it's too long to fit), the artist or site, picture-in-picture, close (stops it and puts the controller away), and
/// a bar showing how far through it is, to seek with. A video still floats when you leave its tab
/// (Pip.cs). The page side is MediaScript, injected into every page.
/// </summary>
public sealed partial class MainWindow
{
    private const int SkipSeconds = 10;
    private const float CompactHeight = 40;

    // The tab the controller is for, if it's showing.
    private BrowserTab? _mediaTab;
    private DispatcherQueueTimer? _mediaTimer;

    // Closed with ×: it stays away until the tab starts playing again.
    private BrowserTab? _mediaDismissed;

    /// <summary>
    /// Page side: the media element last played (an Audio() never put in the page too), and the
    /// site's media-session handlers (its media keys). window.__driftMedia plays or pauses it, skips,
    /// seeks, floats its video, and says what's playing and how far through; Drift calls it through
    /// DevTools, as if clicked, since playing needs a user gesture.
    /// </summary>
    private static string MediaScript() => """
          const mediaKeys = {}; let lastMedia = null;
          try {
            const session = navigator.mediaSession;
            const setKey = session.setActionHandler.bind(session);
            session.setActionHandler = function (action, handler) { mediaKeys[action] = handler; return setKey(action, handler); };
          } catch { }
          document.addEventListener('play', (e) => { if (e.target instanceof HTMLMediaElement) lastMedia = e.target; }, true);
          try {
            const playMedia = HTMLMediaElement.prototype.play;
            HTMLMediaElement.prototype.play = function () { lastMedia = this; return playMedia.apply(this, arguments); };
          } catch { }
          const media = () => {
            if (lastMedia && (lastMedia.isConnected || !lastMedia.paused)) return lastMedia;
            return [...document.querySelectorAll('video, audio')].find((m) => !m.paused) || lastMedia;
          };
          const press = (action, extra) => {
            if (!mediaKeys[action]) return false;
            try { mediaKeys[action]({ action, ...extra }); return true; } catch { return false; }
          };
          const video = () => {
            const m = media();
            if (m instanceof HTMLVideoElement) return m;
            return [...document.querySelectorAll('video')].sort((a, b) => b.clientWidth * b.clientHeight - a.clientWidth * a.clientHeight)[0] || null;
          };
          Object.defineProperty(window, '__driftMedia', { value: Object.freeze({
            toggle: () => {
              const m = media();
              const playing = m ? !m.paused : navigator.mediaSession && navigator.mediaSession.playbackState === 'playing';
              const action = playing ? 'pause' : 'play';
              if (!press(action) && m) { if (playing) m.pause(); else m.play(); }
              return playing ? 'paused' : 'playing';
            },
            pause: () => { const m = media(); if (!press('pause') && m) m.pause(); },
            skip: (direction) => {
              if (press(direction < 0 ? 'previoustrack' : 'nexttrack')) return 'track';
              const m = media();
              if (m && isFinite(m.duration)) {
                m.currentTime = Math.max(0, Math.min(m.duration, m.currentTime + direction * __SKIP__));
                return 'seek';
              }
              return press(direction < 0 ? 'seekbackward' : 'seekforward', { seekOffset: __SKIP__ }) ? 'seek' : 'none';
            },
            seek: (time) => {
              const m = media();
              if (m && isFinite(m.duration)) m.currentTime = Math.max(0, Math.min(m.duration, time));
              else press('seekto', { seekTime: time });
            },
            pip: () => {
              if (document.pictureInPictureElement) { document.exitPictureInPicture(); return 'left'; }
              const v = video();
              return v ? v.requestPictureInPicture().then(() => 'ok', (e) => e.name) : 'none';
            },
            state: () => {
              const m = media();
              const about = navigator.mediaSession && navigator.mediaSession.metadata;
              return JSON.stringify({
                title: (about && about.title) || document.title,
                artist: (about && about.artist) || '',
                art: ((about && about.artwork) || []).map((a) => { try { return [new URL(a.src, location.href).href, a.sizes || '']; } catch { return null; } }).filter(Boolean),
                time: m ? m.currentTime : 0,
                duration: m && isFinite(m.duration) ? m.duration : 0,
                video: !!video(),
              });
            },
          }) });
        """.Replace("__SKIP__", SkipSeconds.ToString());

    /// <summary>A tab's sound: whether it's playing and muted, kept on the tab for its row and the controller.</summary>
    private void WireAudio(BrowserTab tab, CoreWebView2 core)
    {
        void Update()
        {
            if (tab.View.CoreWebView2 != core) return;
            var playing = core.IsDocumentPlayingAudio;
            if (playing != tab.IsPlayingAudio || tab.AudioSince is null && playing)
            {
                tab.AudioSince = Environment.TickCount64;
                // Playing again after its controller was closed: it's back.
                if (playing && _mediaDismissed == tab) _mediaDismissed = null;
            }
            tab.IsPlayingAudio = playing;
            tab.IsMuted = core.IsMuted;
            UpdateMediaBar();
        }
        core.IsDocumentPlayingAudioChanged += (_, _) => Update();
        core.IsMutedChanged += (_, _) => Update();
        tab.IsMuted = core.IsMuted;
        if (core.IsDocumentPlayingAudio) Update();
    }

    /// <summary>The controller, for the tab MediaBar picks, or gone.</summary>
    private void UpdateMediaBar()
    {
        var now = Environment.TickCount64;
        var tabs = AllTabs.Where(t => t.AudioSince is not null && !t.IsUnloaded && t.View.CoreWebView2 is not null && t != _mediaDismissed)
            .Select(t => new MediaTab(t, t.IsPlayingAudio, t.AudioSince!.Value)).ToList();
        var tab = MediaBar.Pick(tabs, now) as BrowserTab;
        if (tab != _mediaTab)
        {
            var arriving = _mediaTab is null && tab is not null;
            _mediaTab = tab;
            if (tab is null) CollapseMediaNow();
            MediaBarRow.Visibility = MediaCard.Visibility = tab is null ? Visibility.Collapsed : Visibility.Visible;
            if (arriving)
            {
                MediaCard.CenterPoint = new Vector3(100, CompactHeight / 2, 0);
                Spring.To(MediaCard, "Scale", Vector3.One, 0.3, 0, from: new Vector3(0.94f, 0.94f, 1));
                if (!_uiSettings.AnimationsEnabled)
                {
                    MediaCard.Opacity = 0;
                    Ui.Fade(MediaCard, 1);
                }
            }
            if (_mediaExpanded) _ = RefreshMediaStateAsync();
        }
        if (tab is null) return;

        MediaIcon.Source = tab.Favicon;
        MediaIconGlyph.Visibility = tab.PlaceholderVisibility;
        ToolTipService.SetToolTip(MediaTabButton, tab.Title);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(MediaTabButton, "Go to " + tab.Title);
        SetMediaButton(MediaPlayButton, MediaPlayGlyph, tab.IsPlayingAudio ? "" : "", tab.IsPlayingAudio ? "Pause" : "Play");
        SetMediaButton(MediaMuteButton, MediaMuteGlyph, tab.IsMuted ? "" : "", tab.IsMuted ? "Unmute" : "Mute");

        // Paused: gone once it's been paused long enough, unless something plays meanwhile.
        if (!tab.IsPlayingAudio)
        {
            _mediaTimer ??= CreateMediaTimer();
            _mediaTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1000, MediaBar.KeepPausedMs - (now - tab.AudioSince!.Value) + 50));
            _mediaTimer.Start();
        }
    }

    private DispatcherQueueTimer CreateMediaTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.IsRepeating = false;
        timer.Tick += (_, _) => Safely(UpdateMediaBar);
        return timer;
    }

    /// <summary>
    /// A timer's tick: what goes wrong is noted in the crash log rather than taking Drift down, which an
    /// exception escaping a DispatcherQueue timer does (it never reaches App's handler).
    /// </summary>
    private static void Safely(Action action)
    {
        try { action(); }
        catch (Exception error) { App.LogCrash(error); }
    }

    private static void SetMediaButton(Button button, FontIcon icon, string glyph, string name)
    {
        icon.Glyph = glyph;
        ToolTipService.SetToolTip(button, name);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, name);
    }

    private void MediaTab_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaTab is { } tab) Activate(tab);
    }

    private void MediaTitle_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_mediaTab is { } tab) Activate(tab);
    }

    private async void MediaPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaTab is not { View.CoreWebView2: { } core } tab) return;
        try
        {
            var result = (await EvaluateAsync(core, "window.__driftMedia ? window.__driftMedia.toggle() : 'none'", gesture: true))?.GetString();
            // Shown at once; the engine's own measure catches up a moment later.
            if (result is "playing" or "paused" && tab == _mediaTab)
                SetMediaButton(MediaPlayButton, MediaPlayGlyph, result == "playing" ? "" : "", result == "playing" ? "Pause" : "Play");
        }
        catch
        {
            // The page went away meanwhile.
        }
    }

    private void MediaBack_Click(object sender, RoutedEventArgs e) => _ = RunMediaAsync("window.__driftMedia && window.__driftMedia.skip(-1)");
    private void MediaForward_Click(object sender, RoutedEventArgs e) => _ = RunMediaAsync("window.__driftMedia && window.__driftMedia.skip(1)");
    private void MediaPip_Click(object sender, RoutedEventArgs e) => _ = RunMediaAsync("window.__driftMedia && window.__driftMedia.pip()");

    /// <summary>×: it stops, and the controller goes until the tab plays again.</summary>
    private async void MediaClose_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaTab is not { } tab) return;
        _mediaDismissed = tab;
        UpdateMediaBar();
        await RunMediaAsync("window.__driftMedia && window.__driftMedia.pause()", tab);
    }

    /// <summary>Runs one of the page's media commands (MediaScript) as if clicked, and refreshes the card.</summary>
    private async Task RunMediaAsync(string expression, BrowserTab? tab = null)
    {
        if ((tab ?? _mediaTab) is not { View.CoreWebView2: { } core }) return;
        try { await EvaluateAsync(core, expression, gesture: true); }
        catch { }
        if (_mediaExpanded) await RefreshMediaStateAsync();
    }

    private void MediaMute_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaTab is { } tab) ToggleMute(tab);
    }

    /// <summary>A tab row's speaker: mutes the tab, or unmutes it.</summary>
    private void TabAudio_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is BrowserTab tab) ToggleMute(tab);
    }

    private static void ToggleMute(BrowserTab tab)
    {
        if (tab.View.CoreWebView2 is { } core) core.IsMuted = !core.IsMuted;
    }

    // ── Expanded: the card ──

    private bool _mediaExpanded;
    private bool _pointerOnMedia;
    private int _mediaGeneration;
    private DispatcherQueueTimer? _mediaHoverTimer;
    private DispatcherQueueTimer? _mediaPollTimer;
    private CompositionPropertySet? _mediaGrow;
    private double _mediaDuration;
    private bool _scrubbing;
    private string? _mediaTitleText;
    private CompositionAnimation? _marquee;

    private void MediaCard_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerOnMedia = true;
        // A pointer passing by on its way elsewhere doesn't open it.
        HoverMedia(TimeSpan.FromMilliseconds(150));
    }

    private void MediaCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerOnMedia = false;
        if (!_scrubbing) HoverMedia(TimeSpan.FromMilliseconds(300));
    }

    private void HoverMedia(TimeSpan after)
    {
        if (_mediaHoverTimer is null)
        {
            _mediaHoverTimer = DispatcherQueue.CreateTimer();
            _mediaHoverTimer.IsRepeating = false;
            _mediaHoverTimer.Tick += (_, _) => Safely(() =>
            {
                if (_mediaTab is not null && _pointerOnMedia != _mediaExpanded) SetMediaExpanded(_pointerOnMedia);
            });
        }
        _mediaHoverTimer.Interval = after;
        _mediaHoverTimer.Start();
    }

    /// <summary>
    /// Grows the controller up into the card, or back. The card takes its full height at once and is
    /// cut back to the bar, and the cut springs open (or shut).
    /// </summary>
    private void SetMediaExpanded(bool expand)
    {
        _mediaExpanded = expand;
        var generation = ++_mediaGeneration;
        var visual = ElementCompositionPreview.GetElementVisual(MediaCardBody);
        var compositor = visual.Compositor;
        var animate = _uiSettings.AnimationsEnabled;

        if (expand)
        {
            MediaHeader.Visibility = MediaProgress.Visibility = Visibility.Visible;
            MediaCard.Height = double.NaN;
            _ = RefreshMediaStateAsync();
            _mediaPollTimer ??= CreatePollTimer();
            _mediaPollTimer.Start();
        }
        else
        {
            _mediaPollTimer?.Stop();
        }
        MediaCard.UpdateLayout();
        float width = (float)MediaCard.ActualWidth, height = (float)MediaCard.ActualHeight;

        if (_mediaGrow is null || visual.Clip is null)
        {
            _mediaGrow = compositor.CreatePropertySet();
            _mediaGrow.InsertScalar("T", 0);
        }
        var shape = compositor.CreateRoundedRectangleGeometry();
        shape.CornerRadius = new Vector2(8);
        ExpressionAnimation Bind(string expression)
        {
            var animation = compositor.CreateExpressionAnimation(expression);
            animation.SetReferenceParameter("p", _mediaGrow);
            animation.SetScalarParameter("w", width);
            animation.SetScalarParameter("h", height);
            animation.SetScalarParameter("c", CompactHeight);
            return animation;
        }
        shape.StartAnimation("Offset", Bind("Vector2(0, (h - c) * (1 - p.T))"));
        shape.StartAnimation("Size", Bind("Vector2(w, c + (h - c) * p.T)"));
        visual.Clip = compositor.CreateGeometricClip(shape);

        // Past open, the cut rises above the card's top, and the card stretches up to fill it, from
        // its foot; its contents stay where they are.
        var back = ElementCompositionPreview.GetElementVisual(MediaCardBack);
        back.CenterPoint = new Vector3(0, height, 0);
        back.StartAnimation("Scale", Bind("Vector3(1, 1 + Max(p.T - 1, 0) * (h - c) / h, 1)"));

        // The card is see-through, like a selected tab, so the tabs it would cover are cut off above
        // it as it rises: under it there's only the sidebar.
        var tabs = ElementCompositionPreview.GetElementVisual(SidebarScroll);
        if (tabs.Clip is not InsetClip cut)
        {
            cut = compositor.CreateInsetClip();
            tabs.Clip = cut;
        }
        cut.StartAnimation("BottomInset", Bind("Max(p.T, 0) * (h - c)"));

        // The solid card and the new rows fade in as it opens, and out as it shuts.
        MediaCardPlate.OpacityTransition ??= new ScalarTransition { Duration = TimeSpan.FromMilliseconds(150) };
        MediaHeader.OpacityTransition ??= new ScalarTransition { Duration = TimeSpan.FromMilliseconds(150) };
        MediaProgress.OpacityTransition ??= new ScalarTransition { Duration = TimeSpan.FromMilliseconds(150) };
        MediaCardPlate.Opacity = MediaHeader.Opacity = MediaProgress.Opacity = expand ? 1 : 0;

        if (animate)
        {
            // From wherever it's got to: hovering again while it shuts opens it again from there.
            var spring = compositor.CreateSpringScalarAnimation();
            spring.FinalValue = expand ? 1 : 0;
            // Opening overshoots a little; shutting dips just under the bar and back.
            spring.DampingRatio = expand ? 0.68f : 0.75f;
            spring.Period = TimeSpan.FromSeconds(0.3 / (2 * Math.PI));
            _mediaGrow.StartAnimation("T", spring);
        }
        else
        {
            _mediaGrow.InsertScalar("T", expand ? 1 : 0);
        }
        if (!expand) _ = FinishCollapseAsync(generation, animate ? 450 : 0);
    }

    /// <summary>Shut: back to the bar's own height, once the cut has closed over the card.</summary>
    private async Task FinishCollapseAsync(int generation, int afterMs)
    {
        if (afterMs > 0) await Task.Delay(afterMs);
        if (generation != _mediaGeneration || _mediaExpanded) return;
        MediaHeader.Visibility = MediaProgress.Visibility = Visibility.Collapsed;
        MediaCard.Height = CompactHeight;
        ElementCompositionPreview.GetElementVisual(MediaCardBody).Clip = null;
        var back = ElementCompositionPreview.GetElementVisual(MediaCardBack);
        back.StopAnimation("Scale");
        back.Scale = Vector3.One;
        ElementCompositionPreview.GetElementVisual(SidebarScroll).Clip = null;
        StopMarquee();
    }

    /// <summary>The controller's gone (nothing playing): the card shuts at once.</summary>
    private void CollapseMediaNow()
    {
        if (!_mediaExpanded) return;
        _mediaExpanded = false;
        var generation = ++_mediaGeneration;
        _mediaPollTimer?.Stop();
        MediaCardPlate.Opacity = MediaHeader.Opacity = MediaProgress.Opacity = 0;
        _ = FinishCollapseAsync(generation, 0);
    }

    private DispatcherQueueTimer CreatePollTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(500);
        timer.IsRepeating = true;
        timer.Tick += (_, _) => _ = RefreshMediaStateAsync();
        return timer;
    }

    /// <summary>The card's title, site, times and bar, from the page.</summary>
    private async Task RefreshMediaStateAsync()
    {
        if (_mediaTab is not { View.CoreWebView2: { } core } tab) return;
        try
        {
            if ((await EvaluateAsync(core, "window.__driftMedia ? window.__driftMedia.state() : ''", gesture: false))?.GetString() is not { Length: > 0 } text
                || tab != _mediaTab)
                return;
            using var json = JsonDocument.Parse(text);
            var root = json.RootElement;
            var title = root.GetProperty("title").GetString() ?? "";
            var artist = root.GetProperty("artist").GetString() ?? "";
            var time = root.GetProperty("time").GetDouble();
            _mediaDuration = root.GetProperty("duration").GetDouble();
            SetMediaTitle(string.IsNullOrWhiteSpace(title) ? tab.Title : title.Trim());
            MediaSite.Text = artist.Length > 0 ? artist : SiteOf(tab);
            SetMediaArt(MediaArtwork.Pick(root.GetProperty("art").EnumerateArray()
                .Select(a => (a[0].GetString() ?? "", a[1].GetString())).ToList(), ArtPixels));
            MediaPipButton.Visibility = root.GetProperty("video").GetBoolean() ? Visibility.Visible : Visibility.Collapsed;
            MediaDuration.Text = _mediaDuration > 0 ? MediaTime.Format(_mediaDuration) : "";
            if (!_scrubbing) ShowMediaTime(time);
        }
        catch
        {
            // The page went away, or is busy: next time.
        }
    }

    // The artwork's square (36 across) in pixels on a 150% screen: what to ask the page's list for.
    // Decoded at its height, so a wide thumbnail fills the square, cropped at the sides.
    private const int ArtPixels = 54;
    private string? _mediaArtSrc;

    /// <summary>The album cover or thumbnail; none, the title has the room.</summary>
    private void SetMediaArt(string? src)
    {
        if (src == _mediaArtSrc) return;
        _mediaArtSrc = src;
        MediaArt.Source = src is null ? null : new BitmapImage(new Uri(src)) { DecodePixelHeight = 36, DecodePixelType = DecodePixelType.Logical };
        MediaArtFrame.Visibility = src is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void MediaArt_ImageFailed(object sender, ExceptionRoutedEventArgs e) => MediaArtFrame.Visibility = Visibility.Collapsed;

    private static string SiteOf(BrowserTab tab)
    {
        if (!Uri.TryCreate(tab.Url, UriKind.Absolute, out var url)) return "";
        var host = url.Host;
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }

    private void ShowMediaTime(double time)
    {
        MediaElapsed.Text = MediaTime.Format(time);
        MediaScrubFill.Width = MediaTime.Fraction(time, _mediaDuration) * MediaScrub.ActualWidth;
    }

    // ── Seeking: press or drag on the bar ──

    private void MediaScrub_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_mediaDuration <= 0) return;
        _scrubbing = MediaScrub.CapturePointer(e.Pointer);
        ShowMediaTime(TimeAt(e));
        e.Handled = true;
    }

    private void MediaScrub_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_scrubbing) return;
        ShowMediaTime(TimeAt(e));
        e.Handled = true;
    }

    private void MediaScrub_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_scrubbing) return;
        var time = TimeAt(e);
        _scrubbing = false;
        MediaScrub.ReleasePointerCapture(e.Pointer);
        ShowMediaTime(time);
        _ = RunMediaAsync($"window.__driftMedia && window.__driftMedia.seek({time.ToString(CultureInfo.InvariantCulture)})");
        if (!_pointerOnMedia) HoverMedia(TimeSpan.FromMilliseconds(300));
        e.Handled = true;
    }

    private void MediaScrub_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _scrubbing = false;
        if (!_pointerOnMedia) HoverMedia(TimeSpan.FromMilliseconds(300));
    }

    private double TimeAt(PointerRoutedEventArgs e) =>
        MediaTime.At(e.GetCurrentPoint(MediaScrub).Position.X, MediaScrub.ActualWidth, _mediaDuration);

    // ── The title: scrolling by when it's too long to fit ──

    private void SetMediaTitle(string title)
    {
        if (title == _mediaTitleText) return;
        _mediaTitleText = title;
        MediaTitle.Text = MediaTitleCopy.Text = title;
        UpdateMarquee();
    }

    private void MediaTitleBox_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        MediaTitleBox.Clip = new RectangleGeometry { Rect = new Rect(0, -4, e.NewSize.Width, e.NewSize.Height + 8) };
        UpdateMarquee();
    }

    /// <summary>
    /// Too long to fit, the title moves along at a steady pace with a copy following it, so it comes
    /// round again without a jump; it rests a moment at the start of each loop. It fits: it's still.
    /// </summary>
    private void UpdateMarquee()
    {
        StopMarquee();
        MediaTitle.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var textWidth = MediaTitle.DesiredSize.Width;
        var scrolls = MediaMarquee.Scrolls(textWidth, MediaTitleBox.ActualWidth) && _uiSettings.AnimationsEnabled;
        MediaTitleCopy.Visibility = scrolls ? Visibility.Visible : Visibility.Collapsed;
        MediaTitleCopy.Margin = new Thickness(MediaMarquee.Gap, 0, 0, 0);
        if (!scrolls) return;

        var compositor = ElementCompositionPreview.GetElementVisual(MediaTitleStrip).Compositor;
        const double rest = 1.5;
        var loop = MediaMarquee.LoopSeconds(textWidth);
        var total = rest + loop;
        var move = compositor.CreateVector3KeyFrameAnimation();
        move.Target = "Translation";
        move.InsertKeyFrame(0, Vector3.Zero);
        move.InsertKeyFrame((float)(rest / total), Vector3.Zero);
        move.InsertKeyFrame(1, new Vector3(-(float)(textWidth + MediaMarquee.Gap), 0, 0), compositor.CreateLinearEasingFunction());
        move.Duration = TimeSpan.FromSeconds(total);
        move.IterationBehavior = AnimationIterationBehavior.Forever;
        _marquee = move;
        MediaTitleStrip.StartAnimation(move);
    }

    private void StopMarquee()
    {
        if (_marquee is not null) MediaTitleStrip.StopAnimation(_marquee);
        _marquee = null;
        MediaTitleStrip.Translation = Vector3.Zero;
    }
}
