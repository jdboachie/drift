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
/// - its tile counts down to your next meeting ("in 5m"), with Join once it can be joined;
/// - hovering it shows today's meetings, beside it, with Join on the one about to start;
/// - a minute before a meeting a notice says what it is, with Join.
/// Your calendars come from Google's own export (Settings › Export), asked for in the background as
/// the signed-in page would, by a hidden view on the calendar's site, every five minutes
/// (CalendarFeed.cs reads it). Nothing to set up, and nothing leaves Drift.
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

    private BrowserTab? FindCalendarTab() => EssentialTabs.Concat(PinnedTabs).Concat(FolderTabs).FirstOrDefault(IsCalendarTab);

    private int CalendarAccount => GoogleCalendarLinks.Account(_calendarTab?.HomeUri);

    private void CalendarTick()
    {
        var tab = FindCalendarTab();
        if (tab != _calendarTab)
        {
            _calendarTab?.SetMeeting(null, null);
            _calendarTab = tab;
            _calendarTabUrl = null;
            _calendarNextFetch = default;
        }
        if (tab is null)
        {
            _calendarEvents = [];
            if (_hoverCard == CalendarCard && (_hoverTab is null || HoverCardFor(_hoverTab) is null)) HideHoverCard();
            CloseHiddenSite(GoogleCalendarLinks.Host);
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

    /// <summary>A calendar tab's countdown (or Join), and reminders of its meetings that are due.</summary>
    private void ShowMeetings(BrowserTab tab, IReadOnlyList<CalendarEvent> events)
    {
        var now = DateTimeOffset.Now;
        var next = MeetingClock.Upcoming(events, now);
        tab.SetMeeting(next is null ? null : MeetingClock.Countdown(next, now),
            next is not null && MeetingClock.CanJoin(next, now) ? next.JoinUrl : null);
        foreach (var due in MeetingClock.DueReminders(events, now, _remindedMeetings))
        {
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
            else MeetingNoticeWhen.Text = MeetingClock.Countdown(shown, now);
        }
        if (_hoverCard == CalendarCard) RenderCalendarCard(scroll: false);
    }

    // ---- Fetching ----

    private async Task FetchCalendarAsync()
    {
        _calendarFetching = true;
        try
        {
            var core = await HiddenSiteAsync(GoogleCalendarLinks.Base);
            var script = $$"""
                (async () => {
                  const r = await fetch('{{GoogleCalendarLinks.ExportPath(CalendarAccount)}}', { credentials: 'include' });
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

    private void MeetingJoin_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is BrowserTab { MeetingJoinUrl: { } url }) JoinMeeting(url);
    }

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

    /// <summary>What the calendar card shows for the tab it's for: its meetings, or why there aren't any.</summary>
    private (List<CalendarEvent> Events, string? Message) CalendarCardData()
    {
        if (_hoverTab is { } tab && tab == _outlookCalendarTab) return (_outlookEvents, OutlookMessage());
        return (_calendarEvents, _calendarState switch
        {
            CalendarState.SignedOut => "Sign in to Google Calendar in its tab",
            CalendarState.Failed when _calendarFiles.Count == 0 => "Couldn't reach Google Calendar",
            CalendarState.Loading => "Loading…",
            _ => null,
        });
    }

    private void RenderCalendarCard(bool scroll)
    {
        var now = DateTimeOffset.Now;
        var dayStart = new DateTimeOffset(DateTime.Today, TimeZoneInfo.Local.GetUtcOffset(DateTime.Today));
        var dayEnd = dayStart.AddDays(1);
        CalendarDay.Text = now.ToString("dddd, MMMM d", CultureInfo.CurrentCulture);
        CalendarAllDay.Children.Clear();
        CalendarTimeline.Children.Clear();

        var (events, message) = CalendarCardData();
        var today = events.Where(e => e.Start < dayEnd && e.End > dayStart).ToList();
        CalendarMessage.Text = message ?? "";
        CalendarMessage.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
        CalendarScroll.Visibility = message is null ? Visibility.Visible : Visibility.Collapsed;
        if (message is not null) return;

        var primary = ThemeBrush("TextFillColorPrimaryBrush");
        var secondary = ThemeBrush("DriftSecondaryTextBrush");
        var fill = ThemeBrush("DriftSelectedBrush");
        var divider = ThemeBrush("DriftDividerBrush");
        var accent = ThemeBrush("AccentFillColorDefaultBrush");

        foreach (var e in today.Where(e => e.AllDay))
            CalendarAllDay.Children.Add(EventBlock(e, now, fill, primary, secondary, height: 24, showTime: false));

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
            var block = EventBlock(e, now, fill, primary, secondary, height, showTime: height >= 38);
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
            CalendarMessage.Text = "Nothing today";
            CalendarMessage.Visibility = Visibility.Visible;
        }
        // Opens on the hour before now.
        if (scroll) CalendarScroll.ChangeView(null, Math.Max(0, nowY - HourHeight), null, disableAnimation: true);
    }

    /// <summary>A meeting on the card: its name, its time if there's room, and Join when it can be joined.</summary>
    private Border EventBlock(CalendarEvent e, DateTimeOffset now, Brush fill, Brush primary, Brush secondary, double height, bool showTime)
    {
        var text = new StackPanel { VerticalAlignment = height < 30 ? VerticalAlignment.Center : VerticalAlignment.Top };
        text.Children.Add(new TextBlock
        {
            Text = e.Title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = primary,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (showTime)
            text.Children.Add(new TextBlock
            {
                Text = $"{e.Start.ToLocalTime():h:mm} – {e.End.ToLocalTime():h:mm tt}",
                FontSize = 11,
                Foreground = secondary,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
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
                Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            };
            join.Click += (_, _) => JoinMeeting(url);
            Grid.SetColumn(join, 1);
            row.Children.Add(join);
        }
        var block = new Border
        {
            Height = height,
            Padding = new Thickness(8, height >= 30 ? 4 : 1, 4, 1),
            CornerRadius = new CornerRadius(6),
            Background = fill,
            Child = row,
            Opacity = !e.AllDay && e.End <= now ? 0.5 : 1,
        };
        AutomationProperties.SetName(block, e.Title);
        var tab = _hoverTab;
        if (e.WebLink is { } link && Uri.TryCreate(link, UriKind.Absolute, out var page))
            block.Tapped += (_, args) =>
            {
                if (args.OriginalSource is DependencyObject source && IsInsideButton(source)) return;
                OpenInTab(tab, page);
            };
        else if (e.GoogleEid is { } eid)
            block.Tapped += (_, args) =>
            {
                if (args.OriginalSource is DependencyObject source && IsInsideButton(source)) return;
                OpenInCalendarTab(GoogleCalendarLinks.Event(eid, CalendarAccount));
            };
        return block;
    }

    // ---- The reminder ----

    private void ShowMeetingNotice(CalendarEvent meeting, BrowserTab tab)
    {
        _noticeMeeting = meeting;
        MeetingNoticeTitle.Text = meeting.Title;
        MeetingNoticeWhen.Text = MeetingClock.Countdown(meeting, DateTimeOffset.Now);
        MeetingNoticeIcon.Source = tab.Favicon;
        MeetingNoticeJoin.Visibility = meeting.JoinUrl is null ? Visibility.Collapsed : Visibility.Visible;
        MeetingNotice.Visibility = Visibility.Visible;
        MeetingNotice.Opacity = 1;
    }

    private void HideMeetingNotice()
    {
        _noticeMeeting = null;
        MeetingNotice.Opacity = 0;
        MeetingNotice.Visibility = Visibility.Collapsed;
    }

    private void MeetingNoticeJoin_Click(object sender, RoutedEventArgs e)
    {
        var url = _noticeMeeting?.JoinUrl;
        HideMeetingNotice();
        if (url is not null) JoinMeeting(url);
    }

    private void MeetingNoticeClose_Click(object sender, RoutedEventArgs e) => HideMeetingNotice();
}
