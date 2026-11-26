using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Drift.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.Web.WebView2.Core;

namespace Drift;

/// <summary>
/// Google Calendar, as in Arc: pin calendar.google.com (in Essentials, pinned tabs or a folder) and
/// sign in, and
/// - its tile counts down to your next meeting ("in 5m");
/// - the meeting card at the foot of the sidebar has the meeting, opening itself with Join (MeetingCard.cs);
/// - hovering it shows today's meetings, beside it, with Join on the one about to start;
/// - a minute before a meeting, if the card can't be seen (the sidebar's hidden, or the card closed),
///   a notice says what it is, with Join.
/// Your calendars come from Google's own export (Settings › Export), asked for in the background as
/// the signed-in page would, by a hidden view on the calendar's site (CalendarFeed.cs reads it): as soon as
/// Google tells your calendar tab something changed (GooglePushScript), and every five minutes in case
/// that was missed. Nothing to set up, and nothing leaves Drift.
/// </summary>
public sealed partial class MainWindow
{
    private enum CalendarState { Loading, Ready, SignedOut, Failed }

    private static readonly TimeSpan CalendarRefresh = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CalendarRetry = TimeSpan.FromMinutes(1);
    private const double HourHeight = 44;
    private const double TimeColumn = 46;

    private CalendarState _calendarState = CalendarState.Loading;
    private List<string> _calendarFiles = [];
    private List<CalendarEvent> _calendarEvents = [];
    private DateTime _calendarDay;
    private DateTimeOffset _calendarNextFetch;
    private bool _calendarFetching;
    // The card opened and should show now; done once its timeline has been laid out (ScrollCalendarToNow).
    private bool _calendarScrollPending;
    private BrowserTab? _calendarTab;
    private string? _calendarTabUrl;
    private DispatcherQueueTimer? _calendarTimer;
    private readonly HashSet<string> _remindedMeetings = [];
    private CalendarEvent? _noticeMeeting;

    /// <summary>Starts watching for a pinned Google Calendar, once the tabs are back.</summary>
    private void StartCalendar()
    {
        if (_private || _calendarTimer is not null) return;
        _calendarTimer = DispatcherQueue.CreateTimer();
        _calendarTimer.Interval = TimeSpan.FromSeconds(15);
        _calendarTimer.Tick += (_, _) => Safely(CalendarTick);
        _calendarTimer.Start();
        CalendarTick();
    }

    private void StopCalendar()
    {
        _calendarTimer?.Stop();
        CloseHiddenSites();
    }

    private static bool IsCalendarTab(BrowserTab tab) =>
        tab.IsPinned && GoogleCalendarLinks.IsCalendar(tab.HomeUri ?? (Uri.TryCreate(tab.Url, UriKind.Absolute, out var u) ? u : null));

    private BrowserTab? FindCalendarTab() =>
        Settings.Current.PreviewGoogleCalendar ? PinnedInEveryProfile.FirstOrDefault(IsCalendarTab) : null;

    private int CalendarAccount => GoogleCalendarLinks.Account(_calendarTab?.HomeUri);

    private void CalendarTick()
    {
        var tab = FindCalendarTab();
        if (tab != _calendarTab)
        {
            _calendarTab?.SetMeeting(null);
            _calendarTab = tab;
            _calendarTabUrl = null;
            _calendarNextFetch = default;
        }
        if (tab is null)
        {
            _calendarEvents = [];
            if (_hoverCard == CalendarCard && (_hoverTab is null || HoverCardFor(_hoverTab) is null)) HideHoverCard();
            CloseHiddenSite(GoogleCalendarLinks.Host);
            UpdateMeetingCard();
            return;
        }
        // Moved on in its tab (as saving an event there does): fetched again, to show what changed.
        if (tab.Url != _calendarTabUrl)
        {
            if (_calendarTabUrl is not null) _calendarNextFetch = default;
            _calendarTabUrl = tab.Url;
        }
        if (DateTimeOffset.Now >= _calendarNextFetch && !_calendarFetching) _ = FetchCalendarAsync();
        if (DateTime.Today != _calendarDay) WorkOutCalendarDay();
        ShowCalendarNow();
    }

    /// <summary>Today's and tomorrow's meetings, from the calendars last fetched.</summary>
    private void WorkOutCalendarDay()
    {
        _calendarDay = DateTime.Today;
        var start = new DateTimeOffset(_calendarDay, TimeZoneInfo.Local.GetUtcOffset(_calendarDay));
        _calendarEvents = CalendarFeed.Between(_calendarFiles, start, start.AddDays(2), TimeZoneInfo.Local);
    }

    /// <summary>The tile's countdown, the reminder, and the card if it's open, as of now.</summary>
    private void ShowCalendarNow()
    {
        if (_calendarTab is { } tab) ShowMeetings(tab, _calendarEvents);
        ShowNoticeAndCardNow();
    }

    /// <summary>
    /// A calendar tab's countdown, the meeting card, and reminders of its meetings that are due. The card
    /// has the meeting, with Join, wherever it can be seen, so the reminder is only for when it can't.
    /// </summary>
    private void ShowMeetings(BrowserTab tab, IReadOnlyList<CalendarEvent> events)
    {
        var now = DateTimeOffset.Now;
        var next = MeetingClock.Upcoming(events, now);
        tab.SetMeeting(next is null ? null : MeetingClock.Countdown(next, now));
        UpdateMeetingCard();
        foreach (var due in MeetingClock.DueReminders(events, now, _remindedMeetings))
        {
            if (!Settings.Current.MeetingReminders) break;
            // Not marked as reminded: hide the sidebar or close the card while it's due, and it comes then.
            if (MeetingCardShows(due)) continue;
            _remindedMeetings.Add(MeetingClock.Key(due));
            ShowMeetingNotice(due, tab);
        }
    }

    private void ShowNoticeAndCardNow()
    {
        var now = DateTimeOffset.Now;
        if (_noticeMeeting is { } shown)
        {
            if (now >= shown.Start + MeetingClock.JoinEarly || now >= shown.End) HideMeetingNotice();
            else MeetingNotice.When.Text = MeetingClock.Countdown(shown, now);
        }
        if (_hoverCard == CalendarCard) RenderCalendarCard(scroll: false);
        UpdateMeetingCard();
    }

    // ---- Fetching ----

    private async Task FetchCalendarAsync()
    {
        // The demo copy (.claude/skills/demo) isn't signed in to a calendar: it can be handed one to show, read
        // again every 15 seconds, so a test can move its meetings about.
        if (Environment.GetEnvironmentVariable("DRIFT_DEMO_CALENDAR") is { Length: > 0 } demo && File.Exists(demo))
        {
            _calendarFiles = [File.ReadAllText(demo)];
            _calendarState = CalendarState.Ready;
            _calendarNextFetch = DateTimeOffset.Now + TimeSpan.FromSeconds(15);
            WorkOutCalendarDay();
            ShowCalendarNow();
            return;
        }
        _calendarFetching = true;
        try
        {
            var core = await HiddenSiteAsync(GoogleCalendarLinks.Base, EngineOfIntegration(_calendarTab));
            var script = $$"""
                (async () => {
                  const r = await fetch('{{GoogleCalendarLinks.ExportPath(CalendarAccount)}}', { credentials: 'include', cache: 'no-store' });
                  const type = r.headers.get('content-type') || '';
                  if (!r.ok || !type.includes('zip')) return { ok: false, status: r.status, type };
                  const bytes = new Uint8Array(await r.arrayBuffer());
                  let s = '';
                  for (let i = 0; i < bytes.length; i += 0x8000) s += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
                  return { ok: true, data: btoa(s) };
                })()
                """;
            var reply = await EvaluateAsync(core, script, gesture: false);
            if (reply is { } json && json.TryGetProperty("ok", out var ok) && ok.GetBoolean()
                && json.GetProperty("data").GetString() is { } data)
            {
                _calendarFiles = Unzip(Convert.FromBase64String(data));
                _calendarState = CalendarState.Ready;
                _calendarNextFetch = DateTimeOffset.Now + CalendarRefresh;
                WorkOutCalendarDay();
            }
            else
            {
                // Google sends its sign-in page instead: the calendar tab is where to sign in.
                var html = reply is { } r && r.TryGetProperty("type", out var t) && (t.GetString() ?? "").Contains("html");
                _calendarState = html ? CalendarState.SignedOut : CalendarState.Failed;
                _calendarFiles = [];
                _calendarEvents = [];
                _calendarNextFetch = DateTimeOffset.Now + CalendarRetry;
            }
        }
        catch (Exception error)
        {
            App.LogCrash(error);
            _calendarState = _calendarFiles.Count > 0 ? CalendarState.Ready : CalendarState.Failed;
            _calendarNextFetch = DateTimeOffset.Now + CalendarRetry;
            CloseHiddenSite(GoogleCalendarLinks.Host);
        }
        finally
        {
            _calendarFetching = false;
        }
        ShowCalendarNow();
        // Google said something changed while this was asking: asked again, for what this may have missed.
        if (_calendarPushedMeanwhile)
        {
            _calendarPushedMeanwhile = false;
            CalendarPushed();
        }
    }

    // ---- Google's push ----

    // The change reaches the export a moment after the page hears of it: asked once it's there, and once for a burst.
    private static readonly TimeSpan CalendarPushSettle = TimeSpan.FromSeconds(2);
    private DispatcherQueueTimer? _calendarPushTimer;
    private bool _calendarPushedMeanwhile;

    /// <summary>The most a keep-alive (or other small note) on Google's channel takes: 43–116 bytes seen; a change is 276 or more.</summary>
    private const int GoogleKeepAliveBytes = 200;

    /// <summary>
    /// In Google Calendar's and Gmail's pages (a tab's, in InputScript): Google's push. Each page keeps a channel open
    /// to Google (signaler-pa…/punctual/multi-watch/channel), and when your calendar or mail changes (anywhere: your
    /// phone, another browser), Google says so down it. Only how much arrives is looked at, never what; more than a
    /// keep-alive tells Drift (CalendarPushed, MailPushed). Not the page's own ask for what changed that follows (Calendar's
    /// sync.sync, Gmail's sync i/s): a page in the background can put it off for minutes, and Gmail's in front is held open
    /// and asked again every 30s or so, change or not. Needs post and token.
    /// </summary>
    private static string GooglePushScript() => """
          if (location.hostname === 'calendar.google.com' || location.hostname === 'mail.google.com') {
            const pushed = token + (location.hostname === 'mail.google.com' ? ':mail-pushed' : ':calendar-pushed');
            const measured = new WeakSet();
            const googleOpen = XMLHttpRequest.prototype.open;
            XMLHttpRequest.prototype.open = function (method, url) {
              try {
                const u = new URL(String(url), location.href);
                if (/^signaler-pa\./.test(u.hostname) && /\/multi-watch\/channel$/.test(u.pathname)
                  && String(method).toUpperCase() === 'GET' && !measured.has(this)) {
                  // The channel arrives in pieces as Google sends them: each one measured on its way past.
                  measured.add(this);
                  let seen = 0;
                  this.addEventListener('loadstart', () => { seen = 0; });
                  this.addEventListener('progress', (e) => {
                    try { const piece = e.loaded - seen; seen = e.loaded; if (piece > __KEEPALIVE__) post(pushed); } catch { }
                  });
                }
              } catch { }
              return googleOpen.apply(this, arguments);
            };
          }
        """.Replace("__KEEPALIVE__", GoogleKeepAliveBytes.ToString(CultureInfo.InvariantCulture));

    /// <summary>Google said a calendar changed: asked again once it's in the export, or after the ask under way.</summary>
    private void CalendarPushed()
    {
        if (_calendarTab is null) return;
        _calendarPushTimer ??= OneShot(CalendarPushSettle, () =>
        {
            if (_calendarFetching) _calendarPushedMeanwhile = true;
            else _ = FetchCalendarAsync();
        });
        _calendarPushTimer.Stop();
        _calendarPushTimer.Start();
    }

    private static List<string> Unzip(byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        return archive.Entries.Where(e => e.Name.EndsWith(".ics", StringComparison.OrdinalIgnoreCase)).Select(e =>
        {
            using var reader = new StreamReader(e.Open(), Encoding.UTF8);
            return reader.ReadToEnd();
        }).ToList();
    }

    // ---- Join, open ----

    private void JoinMeeting(string url)
    {
        HideHoverCard();
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) OpenInNewTab(uri);
    }

    private void OpenInCalendarTab(Uri uri) => OpenInTab(_calendarTab, uri);

    private void CalendarNew_Click(object sender, RoutedEventArgs e)
    {
        if (_hoverTab is { } tab && tab == _outlookCalendarTab) OpenInTab(tab, OutlookApi.NewEvent(OutlookHost));
        else OpenInCalendarTab(GoogleCalendarLinks.NewEvent(CalendarAccount));
    }

    // ---- The card ----

    /// <summary>The card's about to show: from the last fetch at once, a fresh one following if that's a while ago.</summary>
    private void ShowingCalendarCard()
    {
        if (_hoverTab is { } tab && tab == _outlookCalendarTab) ShowingOutlookCard();
        else if (DateTimeOffset.Now >= _calendarNextFetch - CalendarRefresh + TimeSpan.FromMinutes(1) && !_calendarFetching)
            _ = FetchCalendarAsync();
        RenderCalendarCard(scroll: true);
    }

    /// <summary>What the calendar card shows for the tab it's for: its meetings, or why there aren't any and what to do about it.</summary>
    private (List<CalendarEvent> Events, string? Message, string? Action) CalendarCardData()
    {
        if (_hoverTab is { } tab && tab == _outlookCalendarTab) return (_outlookEvents, OutlookMessage(), OutlookAction());
        return _calendarState switch
        {
            CalendarState.SignedOut => (_calendarEvents, "You're signed out of Google Calendar", "Sign in"),
            CalendarState.Failed when _calendarFiles.Count == 0 => (_calendarEvents, "We couldn't reach Google Calendar", "Try again"),
            CalendarState.Loading => (_calendarEvents, "Loading…", null),
            _ => (_calendarEvents, null, null),
        };
    }

    /// <summary>Signed out: the calendar's tab, to sign in. Couldn't reach it: asked again, by a fresh view.</summary>
    private void CalendarAction_Click(object sender, RoutedEventArgs e)
    {
        if (RetryOutlook()) return;
        if (_calendarState == CalendarState.SignedOut)
        {
            HideHoverCard();
            if (_calendarTab is { } tab) Activate(tab);
            return;
        }
        _calendarState = CalendarState.Loading;
        RenderCalendarCard(scroll: true);
        if (_calendarFetching) return;
        CloseHiddenSite(GoogleCalendarLinks.Host);
        _ = FetchCalendarAsync();
    }

    private void RenderCalendarCard(bool scroll)
    {
        if (scroll) _calendarScrollPending = true;
        var now = DateTimeOffset.Now;
        var dayStart = new DateTimeOffset(DateTime.Today, TimeZoneInfo.Local.GetUtcOffset(DateTime.Today));
        var dayEnd = dayStart.AddDays(1);
        CalendarAllDay.Children.Clear();
        CalendarTimeline.Children.Clear();

        var (events, message, action) = CalendarCardData();
        var today = events.Where(e => e.Start < dayEnd && e.End > dayStart).ToList();
        CalendarMessage.Show(message, action);
        CalendarScroll.Visibility = message is null ? Visibility.Visible : Visibility.Collapsed;
        if (message is not null) return;

        var primary = ThemeBrush("TextFillColorPrimaryBrush");
        var secondary = ThemeBrush("DriftSecondaryTextBrush");
        var divider = ThemeBrush("DriftDividerBrush");
        var accent = ThemeBrush("AccentFillColorDefaultBrush");

        foreach (var e in today.Where(e => e.AllDay))
            CalendarAllDay.Children.Add(EventBlock(e, now, primary, height: 24));
        // With none, the hours start at the card's top, as Arc's do.
        CalendarAllDay.Visibility = CalendarAllDay.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // The hours, each with its line.
        var width = CalendarCard.Width - 2;
        CalendarTimeline.Width = width;
        CalendarTimeline.Height = HourHeight * 24 + 8;
        for (var h = 0; h < 24; h++)
        {
            var y = h * HourHeight + 4;
            var label = new TextBlock
            {
                Text = DateTime.Today.AddHours(h).ToString("h tt", CultureInfo.CurrentCulture),
                FontSize = 11,
                Foreground = secondary,
                Width = TimeColumn - 8,
                TextAlignment = TextAlignment.Right,
            };
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, y - 8);
            if (h > 0) CalendarTimeline.Children.Add(label);
            var line = new Rectangle { Height = 1, Width = width - TimeColumn - 8, Fill = divider };
            Canvas.SetLeft(line, TimeColumn);
            Canvas.SetTop(line, y);
            CalendarTimeline.Children.Add(line);
        }

        var timed = today.Where(e => !e.AllDay).ToList();
        var lanes = MeetingClock.Lanes(timed);
        var area = width - TimeColumn - 10;
        foreach (var e in timed)
        {
            var (lane, count) = lanes[e];
            var from = e.Start < dayStart ? dayStart : e.Start;
            var to = e.End > dayEnd ? dayEnd : e.End;
            var top = (from - dayStart).TotalHours * HourHeight + 4;
            var height = Math.Max(22, (to - from).TotalHours * HourHeight - 2);
            var block = EventBlock(e, now, primary, height);
            block.Width = area / count - 2;
            Canvas.SetLeft(block, TimeColumn + 2 + lane * area / count);
            Canvas.SetTop(block, top + 1);
            CalendarTimeline.Children.Add(block);
        }

        // Now: a line across, with a dot at its start.
        var nowY = (now - dayStart).TotalHours * HourHeight + 4;
        var nowLine = new Rectangle { Height = 2, Width = width - TimeColumn - 6, Fill = accent, RadiusX = 1, RadiusY = 1 };
        Canvas.SetLeft(nowLine, TimeColumn);
        Canvas.SetTop(nowLine, nowY - 1);
        var dot = new Ellipse { Width = 8, Height = 8, Fill = accent };
        Canvas.SetLeft(dot, TimeColumn - 4);
        Canvas.SetTop(dot, nowY - 4);
        CalendarTimeline.Children.Add(nowLine);
        CalendarTimeline.Children.Add(dot);

        if (timed.Count == 0 && today.Count == 0)
        {
            CalendarMessage.Show("Nothing today", null);
        }
        ScrollCalendarToNow();
    }

    /// <summary>
    /// Opens the timeline on the hour before now, once it's been laid out. Before that (the card's first showing,
    /// or while "Loading…" stood in for it) the scroller has no height yet, and would stay at midnight.
    /// </summary>
    private void ScrollCalendarToNow()
    {
        CalendarScroll.LayoutUpdated -= CalendarScroll_LayoutUpdated;
        if (!_calendarScrollPending || CalendarScroll.Visibility != Visibility.Visible) return;
        if (CalendarScroll.ExtentHeight <= 0 || CalendarScroll.ViewportHeight <= 0)
        {
            CalendarScroll.LayoutUpdated += CalendarScroll_LayoutUpdated;
            return;
        }
        _calendarScrollPending = false;
        var dayStart = new DateTimeOffset(DateTime.Today, TimeZoneInfo.Local.GetUtcOffset(DateTime.Today));
        var nowY = (DateTimeOffset.Now - dayStart).TotalHours * HourHeight + 4;
        CalendarScroll.ChangeView(null, Math.Max(0, nowY - HourHeight), null, disableAnimation: true);
    }

    private void CalendarScroll_LayoutUpdated(object? sender, object e) => ScrollCalendarToNow();

    /// <summary>
    /// A meeting on the card, as Arc's: only its name, on a tint of the theme's colour (the hour grid says when),
    /// and Join when it can be joined. The whole block is a button that opens the meeting's page; Join sits over
    /// it, not inside it.
    /// </summary>
    private Grid EventBlock(CalendarEvent e, DateTimeOffset now, Brush primary, double height)
    {
        var text = new TextBlock
        {
            Text = e.Title,
            FontSize = 12,
            Foreground = primary,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = height < 30 ? VerticalAlignment.Center : VerticalAlignment.Top,
            IsHitTestVisible = false,
        };
        var top = height >= 30 ? 4 : 1;
        text.Margin = new Thickness(8, top, 0, 1);
        var row = new Grid { ColumnSpacing = 6 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(text);
        if (!e.AllDay && MeetingClock.CanJoin(e, now) && e.JoinUrl is { } url)
        {
            var join = new Button
            {
                Content = "Join",
                FontSize = 11,
                Height = 18,
                Padding = new Thickness(10, 0, 10, 1),
                MinHeight = 0,
                MinWidth = 0,
                CornerRadius = new CornerRadius(9),
                VerticalAlignment = height < 30 ? VerticalAlignment.Center : VerticalAlignment.Top,
                Margin = new Thickness(0, top, 4, 1),
                Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            };
            join.Click += (_, _) => JoinMeeting(url);
            Grid.SetColumn(join, 1);
            row.Children.Add(join);
        }
        var open = new Button
        {
            Style = (Style)Application.Current.Resources["CalendarEventButtonStyle"],
        };
        Grid.SetColumnSpan(open, 2);
        AutomationProperties.SetName(open, e.Title);
        ToolTipService.SetToolTip(open, e.Title);
        var tab = _hoverTab;
        if (e.WebLink is { } link && Uri.TryCreate(link, UriKind.Absolute, out var page))
            open.Click += (_, _) => OpenInTab(tab, page);
        else if (e.GoogleEid is { } eid)
            open.Click += (_, _) => OpenInCalendarTab(GoogleCalendarLinks.Event(eid, CalendarAccount));
        else
        {
            // Nowhere to open it: just the card, not a button.
            open.IsHitTestVisible = false;
            open.IsTabStop = false;
        }
        // Under the text and Join.
        row.Children.Insert(0, open);
        row.Height = height;
        row.Opacity = !e.AllDay && e.End <= now ? 0.5 : 1;
        return row;
    }

    // ---- The reminder ----

    private void ShowMeetingNotice(CalendarEvent meeting, BrowserTab tab)
    {
        _noticeMeeting = meeting;
        MeetingNotice.Title.Text = meeting.Title;
        MeetingNotice.When.Text = MeetingClock.Countdown(meeting, DateTimeOffset.Now);
        MeetingNotice.Icon.Source = tab.Favicon;
        MeetingNotice.Join.Visibility = meeting.JoinUrl is null ? Visibility.Collapsed : Visibility.Visible;
        MeetingNotice.Panel.Visibility = Visibility.Visible;
        MeetingNotice.Panel.Opacity = 1;
    }

    private void HideMeetingNotice()
    {
        _noticeMeeting = null;
        MeetingNotice.Panel.Opacity = 0;
        MeetingNotice.Panel.Visibility = Visibility.Collapsed;
    }

    internal void MeetingNoticeJoin_Click(object sender, RoutedEventArgs e)
    {
        var url = _noticeMeeting?.JoinUrl;
        HideMeetingNotice();
        if (url is not null) JoinMeeting(url);
    }

    internal void MeetingNoticeClose_Click(object sender, RoutedEventArgs e) => HideMeetingNotice();
}
