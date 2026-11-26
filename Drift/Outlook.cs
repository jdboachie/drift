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
/// asks the same of it, with the same sign-in, which stays inside Drift. It asks as soon as Outlook pushes a change
/// to its own page (OutlookPushScript: new mail, mail read anywhere), each time a card opens, soon after either tab moves
/// on, and every two minutes in case the push was missed.
/// </summary>
public sealed partial class MainWindow
{
    private enum OutlookState { Loading, Ready, SignedOut, Failed }

    private static readonly TimeSpan OutlookRefresh = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan OutlookRetry = TimeSpan.FromSeconds(30);
    // The conversations whose senders and emails are asked for: more than the card shows before scrolling.
    private const int OutlookConversationsAsked = 20;

    private OutlookState _outlookState = OutlookState.Loading;
    private BrowserTab? _outlookMailTab;
    private BrowserTab? _outlookCalendarTab;
    private string? _outlookMailUrl;
    private string? _outlookCalendarUrl;
    private List<CalendarEvent> _outlookEvents = [];
    private List<MailItem> _outlookMail = [];
    private DateTimeOffset _outlookNextFetch;
    private DateTimeOffset _outlookFetched;
    private bool _outlookFetching;
    // Tries in a row where Outlook's page made no signed calls.
    private int _outlookMisses;
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

    /// <summary>The pinned tabs with Outlook's mail and calendar whose previews are on: the ones with cards.</summary>
    private (BrowserTab? Mail, BrowserTab? Calendar) PinnedOutlook()
    {
        var pinned = PinnedInEveryProfile.Where(t => t.IsPinned).ToList();
        return (Settings.Current.PreviewOutlookMail ? pinned.FirstOrDefault(t => OutlookApi.IsMail(PageOf(t))) : null,
            Settings.Current.PreviewOutlookCalendar ? pinned.FirstOrDefault(t => OutlookApi.IsCalendar(PageOf(t))) : null);
    }

    private void OutlookTick()
    {
        var (mail, calendar) = PinnedOutlook();
        if (calendar != _outlookCalendarTab)
        {
            _outlookCalendarTab?.SetMeeting(null);
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
            UpdateMeetingCard();
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
            var core = await HiddenSiteAsync(OutlookApi.Start(host), EngineOfIntegration(_outlookMailTab ?? _outlookCalendarTab), ListenToOutlookAsync);
            // Left at Microsoft's sign-in last time: tried again, since signing in to Outlook in a tab
            // signs this in too.
            if (!await OutlookReadyAsync(core) && !OutlookApi.IsOutlook(Uri.TryCreate(core.Source, UriKind.Absolute, out var at) ? at : null))
                await GoToAsync(core, OutlookApi.Start(host), TimeSpan.FromSeconds(30));
            // Outlook calls its API as soon as it's up; until it has, there's nothing to ask with.
            // A cold start (Drift just opened, Outlook's tab booting alongside) can take well over 10s.
            var ready = false;
            for (var i = 0; i < 30 && !(ready = await OutlookReadyAsync(core)); i++) await Task.Delay(500);
            if (!ready)
            {
                var landed = Uri.TryCreate(core.Source, UriKind.Absolute, out var u) ? u : null;
                if (!OutlookApi.IsOutlook(landed))
                {
                    _outlookState = OutlookState.SignedOut;
                    _outlookNextFetch = DateTimeOffset.Now + OutlookRetry;
                }
                else
                {
                    // On Outlook but silent: an Outlook already loaded and idle makes no calls to note,
                    // and asking the same page again never changed that (it failed for an hour at a
                    // time), so the next try boots it afresh. Once, it's only "Loading…" and soon tried
                    // again; it says it couldn't reach Outlook when that fails too.
                    _outlookMisses++;
                    App.LogCrash(new InvalidOperationException("Outlook: its page made no signed calls"));
                    core.Reload();
                    _outlookState = _outlookMisses > 1 ? OutlookState.Failed : OutlookState.Loading;
                    _outlookNextFetch = DateTimeOffset.Now + (_outlookMisses > 1 ? OutlookRetry : TimeSpan.FromSeconds(10));
                }
                if (!_outlookHasData) { _outlookEvents = []; _outlookMail = []; }
                return;
            }
            _outlookMisses = 0;

            var dayStart = new DateTimeOffset(DateTime.Today, TimeZoneInfo.Local.GetUtcOffset(DateTime.Today));
            var events = _outlookCalendarTab is not null ? await OutlookGetAsync(core, OutlookApi.CalendarView(host, dayStart, dayStart.AddDays(2))) : null;
            var messages = _outlookMailTab is not null ? await OutlookGetAsync(core, OutlookApi.UnreadMessages(host)) : null;
            if (events is not null) _outlookEvents = OutlookApi.ParseEvents(events, TimeZoneInfo.Local);
            if (messages is not null)
            {
                var rows = OutlookApi.ParseMessages(messages, host);
                // Everyone in each conversation and all its emails, read ones too, as Outlook lists it; without
                // that answer, those of its unread ones.
                var shown = rows.Select(r => r.Conversation).OfType<string>().Take(OutlookConversationsAsked).ToList();
                if (shown.Count > 0 && await OutlookGetAsync(core, OutlookApi.ConversationEmails(host, shown)) is { } inConversations)
                    rows = OutlookApi.WithConversations(rows, inConversations);
                _outlookMail = rows;
            }
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
        // Outlook said something changed while this was asking: asked again, for what this may have missed.
        if (_outlookPushedMeanwhile)
        {
            _outlookPushedMeanwhile = false;
            OutlookPushed();
        }
    }

    // ---- Outlook's push ----

    // A change comes in a few pieces over under a second: asked once they're in.
    private static readonly TimeSpan OutlookPushSettle = TimeSpan.FromSeconds(1);
    private DispatcherQueueTimer? _outlookPushTimer;
    private bool _outlookPushedMeanwhile;

    /// <summary>The most a keep-alive on Outlook's channel takes: about 21 bytes every 15–20s (a change is hundreds or more).</summary>
    private const int OutlookKeepAliveBytes = 64;

    /// <summary>
    /// In Outlook's pages (a tab's, in InputScript, and the hidden view's): Outlook's push. One Outlook page in a
    /// profile (your tab, or the hidden view when no tab has Outlook open) keeps a request to Outlook's notification
    /// channel open, and Outlook sends down it as things change: new mail, mail read anywhere. Only how much arrives
    /// is looked at, never what; more than a keep-alive tells Drift (OutlookPushed). Needs post and token.
    /// It's in the page rather than read from Drift's side (the page's network events) because that sent Drift an
    /// event for each of Outlook's requests, hundreds a second as it loads, and one arriving as the window closed
    /// crashed WebView2.
    /// </summary>
    private static string OutlookPushScript() => """
          if (/^outlook\.(cloud\.microsoft|office\.com|office365\.com)$/i.test(location.hostname)) {
            const channelFetch = window.fetch;
            const isChannel = (input, init) => {
              try {
                const method = String((init && init.method) || (input instanceof Request ? input.method : 'GET')).toUpperCase();
                const url = new URL(input instanceof Request ? input.url : String(input), location.href);
                return method === 'GET' && url.pathname.replace(/\/+$/, '').toLowerCase() === '/owa/notificationchannel';
              } catch { return false; }
            };
            window.fetch = function (input, init) {
              const answer = channelFetch.apply(this, arguments);
              if (!isChannel(input, init)) return answer;
              return answer.then((response) => {
                try {
                  // The page reads the channel as it arrives: each piece it reads is measured on its way past.
                  const body = response.body, getReader = body && body.getReader;
                  if (getReader) body.getReader = function () {
                    const reader = getReader.apply(body, arguments), read = reader.read;
                    reader.read = function () {
                      return read.apply(reader, arguments).then((part) => {
                        try { if (part && part.value && part.value.byteLength > __KEEPALIVE__) post(token + ':outlook-pushed'); } catch { }
                        return part;
                      });
                    };
                    return reader;
                  };
                } catch { }
                return response;
              });
            };
          }
        """.Replace("__KEEPALIVE__", OutlookKeepAliveBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Outlook said something changed: asked again once its pieces are in, or after the ask under way.</summary>
    private void OutlookPushed()
    {
        if (_outlookMailTab is null && _outlookCalendarTab is null) return;
        _outlookPushTimer ??= OneShot(OutlookPushSettle, () =>
        {
            if (_outlookFetching) _outlookPushedMeanwhile = true;
            else _ = FetchOutlookAsync();
        });
        _outlookPushTimer.Stop();
        _outlookPushTimer.Start();
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

    private async Task ListenToOutlookAsync(CoreWebView2 core)
    {
        await core.AddScriptToExecuteOnDocumentCreatedAsync(OutlookListener);
        await core.AddScriptToExecuteOnDocumentCreatedAsync($$"""
            (() => {
              if (window.top !== window || !window.chrome || !window.chrome.webview) return;
              const post = window.chrome.webview.postMessage.bind(window.chrome.webview);
              const token = '{{ShortcutToken}}';
            {{OutlookPushScript()}}
            })();
            """);
        // Subscribed once, for the view's life (HiddenSiteAsync).
        core.WebMessageReceived += (_, e) =>
        {
            string message;
            try { message = e.TryGetWebMessageAsString(); }
            catch { return; }
            if (message == ShortcutToken + ":outlook-pushed") OutlookPushed();
        };
    }

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

    /// <summary>
    /// Every opening of the card asks again (it shows what it has meanwhile, and changes in place), unless it's only just
    /// asked: going from tile to tile shouldn't ask over and over.
    /// </summary>
    private void ShowingOutlookCard()
    {
        if (DateTimeOffset.Now - _outlookFetched > TimeSpan.FromSeconds(5) && !_outlookFetching) _ = FetchOutlookAsync();
    }

    private string? OutlookMessage() => _outlookState switch
    {
        OutlookState.SignedOut => "Sign in to Outlook in its tab",
        OutlookState.Failed when !_outlookHasData => "We couldn't reach Outlook",
        OutlookState.Loading => "Loading…",
        _ => null,
    };

    /// <summary>What needs you about a preview, for Settings › General › Previews; null when nothing does.</summary>
    internal string? PreviewTrouble(PreviewKind kind)
    {
        static PreviewState As<T>(T state) where T : Enum => Enum.Parse<PreviewState>(state.ToString());
        // Outlook that failed after it had something still shows that, so it's no trouble yet.
        var outlook = _outlookState == OutlookState.Failed && _outlookHasData ? PreviewState.Ready : As(_outlookState);
        return kind switch
        {
            PreviewKind.Gmail => Previews.Trouble(kind, _mailTab is not null, As(_mailState)),
            PreviewKind.GoogleCalendar => Previews.Trouble(kind, _calendarTab is not null, As(_calendarState)),
            PreviewKind.OutlookMail => Previews.Trouble(kind, _outlookMailTab is not null, outlook),
            PreviewKind.OutlookCalendar => Previews.Trouble(kind, _outlookCalendarTab is not null, outlook),
            _ => null,
        };
    }

    /// <summary>The card's button for Outlook: Try again, when it couldn't be reached.</summary>
    private string? OutlookAction() => _outlookState == OutlookState.Failed && !_outlookHasData ? "Try again" : null;

    /// <summary>Try again, on an Outlook card: fetches now rather than at the next turn. False on other cards.</summary>
    private bool RetryOutlook()
    {
        if (_hoverTab is not { } tab || (tab != _outlookMailTab && tab != _outlookCalendarTab)) return false;
        _outlookState = OutlookState.Loading;
        if (tab == _outlookMailTab) RenderMailCard();
        else RenderCalendarCard(scroll: true);
        if (!_outlookFetching) _ = FetchOutlookAsync();
        return true;
    }
}
