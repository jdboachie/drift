using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Drift.Models;
using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;

namespace Drift;

/// <summary>
/// Outlook mail and calendar, as in Arc, for Outlook on the web (work and school accounts): pin
/// Outlook's mail and hovering it shows your unread emails; pin its calendar and it counts down to
/// your next meeting, with Join, shows today's meetings on hover, and reminds you a minute before,
/// as the Google ones do (GoogleCalendar.cs, Gmail.cs).
/// The data comes from Outlook's own API, the one its pages call on their own site. Outlook stays
/// open in a hidden view (Integrations.cs), signed in as your tabs are; when it calls the API, Drift
/// asks the same of it, with the same sign-in, which stays inside Drift. Every two minutes, on hover,
/// and soon after either tab moves on.
/// </summary>
public sealed partial class MainWindow
{
    private enum OutlookState { Loading, Ready, SignedOut, Failed }

    private static readonly TimeSpan OutlookRefresh = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan OutlookRetry = TimeSpan.FromSeconds(30);

    private OutlookState _outlookState = OutlookState.Loading;
    private BrowserTab? _outlookMailTab;
    private BrowserTab? _outlookCalendarTab;
    private string? _outlookMailUrl;
    private string? _outlookCalendarUrl;
    private List<CalendarEvent> _outlookEvents = [];
    private List<MailItem> _outlookMail = [];
    private int _outlookUnread;
    private DateTimeOffset _outlookNextFetch;
    private DateTimeOffset _outlookFetched;
    private bool _outlookFetching;
    private bool _outlookHasData;
    private DispatcherQueueTimer? _outlookTimer;

    /// <summary>The site Outlook's pinned tabs are on (it has a few names).</summary>
    private string OutlookHost =>
        (_outlookCalendarTab ?? _outlookMailTab) is { } tab && PageOf(tab) is { } uri && OutlookApi.IsOutlook(uri)
            ? uri.Host : "outlook.cloud.microsoft";

    private static Uri? PageOf(BrowserTab tab) => tab.HomeUri ?? (Uri.TryCreate(tab.Url, UriKind.Absolute, out var u) ? u : null);

    private void StartOutlook()
    {
        if (_private || _outlookTimer is not null) return;
        _outlookTimer = DispatcherQueue.CreateTimer();
        _outlookTimer.Interval = TimeSpan.FromSeconds(15);
        _outlookTimer.Tick += (_, _) => Safely(OutlookTick);
        _outlookTimer.Start();
        OutlookTick();
    }

    private void StopOutlook() => _outlookTimer?.Stop();

    private void OutlookTick()
    {
        var pinned = EssentialTabs.Concat(PinnedTabs).Concat(FolderTabs).Where(t => t.IsPinned).ToList();
        var mail = pinned.FirstOrDefault(t => OutlookApi.IsMail(PageOf(t)));
        var calendar = pinned.FirstOrDefault(t => OutlookApi.IsCalendar(PageOf(t)));
        if (calendar != _outlookCalendarTab)
        {
            _outlookCalendarTab?.SetMeeting(null, null);
            _outlookCalendarTab = calendar;
            _outlookCalendarUrl = null;
            _outlookNextFetch = default;
        }
        if (mail != _outlookMailTab)
        {
            _outlookMailTab = mail;
            _outlookMailUrl = null;
            _outlookNextFetch = default;
        }
        if (mail is null && calendar is null)
        {
            _outlookEvents = [];
            _outlookMail = [];
            if (_hoverTab is { } t && HoverCardFor(t) is null) HideHoverCard();
            foreach (var host in new[] { "outlook.cloud.microsoft", "outlook.office.com", "outlook.office365.com" }) CloseHiddenSite(host);
            return;
        }
        // Moved on in its tab (reading an email, saving a meeting): fetched again soon.
        if (mail is not null && mail.Url != _outlookMailUrl)
        {
            if (_outlookMailUrl is not null) _outlookNextFetch = Min(_outlookNextFetch, _outlookFetched + MailSoonest);
            _outlookMailUrl = mail.Url;
        }
        if (calendar is not null && calendar.Url != _outlookCalendarUrl)
        {
            if (_outlookCalendarUrl is not null) _outlookNextFetch = Min(_outlookNextFetch, _outlookFetched + MailSoonest);
            _outlookCalendarUrl = calendar.Url;
        }
        if (DateTimeOffset.Now >= _outlookNextFetch && !_outlookFetching) _ = FetchOutlookAsync();
        if (calendar is not null) ShowMeetings(calendar, _outlookEvents);
        ShowNoticeAndCardNow();
    }

    // ---- Fetching ----

    private async Task FetchOutlookAsync()
    {
        _outlookFetching = true;
        try
        {
            var host = OutlookHost;
            var core = await HiddenSiteAsync(OutlookApi.Start(host), ListenToOutlookAsync);
            // Left at Microsoft's sign-in last time: tried again, since signing in to Outlook in a tab
            // signs this in too.
            if (!await OutlookReadyAsync(core) && !OutlookApi.IsOutlook(Uri.TryCreate(core.Source, UriKind.Absolute, out var at) ? at : null))
                await GoToAsync(core, OutlookApi.Start(host), TimeSpan.FromSeconds(30));
            // Outlook calls its API as soon as it's up; until it has, there's nothing to ask with.
            var ready = false;
            for (var i = 0; i < 20 && !(ready = await OutlookReadyAsync(core)); i++) await Task.Delay(500);
            if (!ready)
            {
                var landed = Uri.TryCreate(core.Source, UriKind.Absolute, out var u) ? u : null;
                _outlookState = OutlookApi.IsOutlook(landed) ? OutlookState.Failed : OutlookState.SignedOut;
                if (_outlookState == OutlookState.Failed) App.LogCrash(new InvalidOperationException("Outlook: its page made no signed calls"));
                if (!_outlookHasData) { _outlookEvents = []; _outlookMail = []; }
                _outlookNextFetch = DateTimeOffset.Now + OutlookRetry;
                return;
            }

            var dayStart = new DateTimeOffset(DateTime.Today, TimeZoneInfo.Local.GetUtcOffset(DateTime.Today));
            var events = _outlookCalendarTab is not null ? await OutlookGetAsync(core, OutlookApi.CalendarView(host, dayStart, dayStart.AddDays(2))) : null;
            var messages = _outlookMailTab is not null ? await OutlookGetAsync(core, OutlookApi.UnreadMessages(host)) : null;
            var count = _outlookMailTab is not null ? await OutlookGetAsync(core, OutlookApi.Inbox(host)) : null;
            if (events is not null) _outlookEvents = OutlookApi.ParseEvents(events, TimeZoneInfo.Local);
            if (messages is not null) _outlookMail = OutlookApi.ParseMessages(messages);
            if (count is not null) _outlookUnread = OutlookApi.ParseUnread(count);
            var got = (_outlookCalendarTab is null || events is not null) && (_outlookMailTab is null || messages is not null);
            if (got)
            {
                _outlookState = OutlookState.Ready;
                _outlookHasData = true;
                _outlookNextFetch = DateTimeOffset.Now + OutlookRefresh;
            }
            else
            {
                if (!_outlookHasData) _outlookState = OutlookState.Failed;
                _outlookNextFetch = DateTimeOffset.Now + OutlookRetry;
            }
        }
        catch (OutlookSignInExpired)
        {
            // Its sign-in has run out: Outlook, opened again, signs in afresh, and calls with that.
            if (_hiddenSites.TryGetValue(OutlookHost, out var site))
                _ = GoToAsync(site.Core, OutlookApi.Start(OutlookHost), TimeSpan.FromSeconds(30));
            _outlookNextFetch = DateTimeOffset.Now + TimeSpan.FromSeconds(10);
        }
        catch (Exception error)
        {
            App.LogCrash(error);
            if (!_outlookHasData) _outlookState = OutlookState.Failed;
            _outlookNextFetch = DateTimeOffset.Now + OutlookRetry;
        }
        finally
        {
            _outlookFetching = false;
            _outlookFetched = DateTimeOffset.Now;
        }
        if (_outlookCalendarTab is { } calendar) ShowMeetings(calendar, _outlookEvents);
        ShowNoticeAndCardNow();
        if (_hoverCard == MailCard && _hoverTab == _outlookMailTab) RenderMailCard();
    }

    /// <summary>
    /// In the hidden Outlook page, before its own scripts: notes the sign-in its API calls carry, and
    /// can make a call with it. The sign-in stays in the page; Drift gets back only what's asked for.
    /// </summary>
    private const string OutlookListener = """
        (() => {
          if (window.__drift) return;
          let auth = null, mailbox = null;
          const pick = (h, name) => {
            if (!h) return null;
            if (h instanceof Headers) return h.get(name);
            if (Array.isArray(h)) { const e = h.find(p => String(p[0]).toLowerCase() === name.toLowerCase()); return e ? e[1] : null; }
            for (const k of Object.keys(h)) if (k.toLowerCase() === name.toLowerCase()) return h[k];
            return null;
          };
          const note = (h) => {
            try {
              const a = pick(h, 'authorization');
              if (a && /^Bearer /i.test(a)) { auth = a; const m = pick(h, 'x-anchormailbox'); if (m) mailbox = m; }
            } catch { }
          };
          const realFetch = window.fetch;
          window.fetch = function (input, init) {
            try { if (init && init.headers) note(init.headers); else if (input instanceof Request) note(input.headers); } catch { }
            return realFetch.apply(this, arguments);
          };
          const realSet = XMLHttpRequest.prototype.setRequestHeader;
          XMLHttpRequest.prototype.setRequestHeader = function (k, v) {
            try {
              if (/^authorization$/i.test(k) && /^Bearer /i.test(v)) auth = v;
              if (/^x-anchormailbox$/i.test(k)) mailbox = v;
            } catch { }
            return realSet.apply(this, arguments);
          };
          Object.defineProperty(window, '__drift', { value: Object.freeze({
            ready: () => auth !== null,
            get: async (url) => {
              if (!auth) return { status: 0, body: null };
              const headers = { Authorization: auth, Accept: 'application/json', Prefer: 'outlook.timezone="UTC"' };
              if (mailbox) headers['X-AnchorMailbox'] = mailbox;
              const r = await realFetch(url, { headers });
              return { status: r.status, body: r.ok ? await r.text() : null };
            },
          }) });
        })();
        """;

    private static async Task ListenToOutlookAsync(CoreWebView2 core) =>
        await core.AddScriptToExecuteOnDocumentCreatedAsync(OutlookListener);

    private async Task<bool> OutlookReadyAsync(CoreWebView2 core) =>
        await EvaluateAsync(core, "!!(window.__drift && window.__drift.ready())", gesture: false) is { ValueKind: System.Text.Json.JsonValueKind.True };

    private sealed class OutlookSignInExpired : Exception;

    /// <summary>One of Outlook's API addresses, asked from its page as it would; null if it didn't answer.</summary>
    private async Task<string?> OutlookGetAsync(CoreWebView2 core, Uri uri)
    {
        var reply = await EvaluateAsync(core, $"window.__drift.get({System.Text.Json.JsonSerializer.Serialize(uri.ToString())})", gesture: false);
        if (reply is not { ValueKind: System.Text.Json.JsonValueKind.Object } json) return null;
        var status = json.TryGetProperty("status", out var s) && s.TryGetInt32(out var n) ? n : 0;
        if (status == 401) throw new OutlookSignInExpired();
        if (status is < 200 or >= 300)
        {
            App.LogCrash(new InvalidOperationException($"Outlook {uri.AbsolutePath}: {status}"));
            return null;
        }
        return json.TryGetProperty("body", out var body) && body.ValueKind == System.Text.Json.JsonValueKind.String ? body.GetString() : null;
    }

    // ---- The cards ----

    private void ShowingOutlookCard()
    {
        if (DateTimeOffset.Now - _outlookFetched > TimeSpan.FromSeconds(30) && !_outlookFetching) _ = FetchOutlookAsync();
    }

    private string? OutlookMessage() => _outlookState switch
    {
        OutlookState.SignedOut => "Sign in to Outlook in its tab",
        OutlookState.Failed when !_outlookHasData => "Couldn't reach Outlook",
        OutlookState.Loading => "Loading…",
        _ => null,
    };
}
