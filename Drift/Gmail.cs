using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Drift.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.Web.WebView2.Core;

namespace Drift;

/// <summary>
/// Gmail, as in Arc: pin mail.google.com (in Essentials, pinned tabs or a folder) and hovering it
/// shows your unread emails, newest first, beside it (a click opens one in the Gmail tab), with +
/// to write a new one. They come from Gmail's unread feed, asked for in the background by a hidden
/// view on Gmail's site (Integrations.cs), every two minutes, on hover, and soon after the Gmail
/// tab moves on (reading an email there).
/// </summary>
public sealed partial class MainWindow
{
    private enum MailState { Loading, Ready, SignedOut, Failed }

    private static readonly TimeSpan MailRefresh = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MailRetry = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MailSoonest = TimeSpan.FromSeconds(20);

    private MailState _mailState = MailState.Loading;
    private List<MailItem> _mailItems = [];
    private DateTimeOffset _mailNextFetch;
    private DateTimeOffset _mailFetched;
    private bool _mailFetching;
    private BrowserTab? _mailTab;
    private string? _mailTabUrl;
    private DispatcherQueueTimer? _mailTimer;

    private void StartMail()
    {
        if (_private || _mailTimer is not null) return;
        _mailTimer = DispatcherQueue.CreateTimer();
        _mailTimer.Interval = TimeSpan.FromSeconds(10);
        _mailTimer.Tick += (_, _) => Safely(MailTick);
        _mailTimer.Start();
        MailTick();
    }

    private void StopMail() => _mailTimer?.Stop();

    private static bool IsMailTab(BrowserTab tab) =>
        tab.IsPinned && GmailFeed.IsGmail(tab.HomeUri ?? (Uri.TryCreate(tab.Url, UriKind.Absolute, out var u) ? u : null));

    private int MailAccount => GoogleCalendarLinks.Account(_mailTab?.HomeUri);

    private void MailTick()
    {
        var tab = Settings.Current.PreviewGmail ? EssentialTabs.Concat(PinnedPages).Concat(FolderTabs).FirstOrDefault(IsMailTab) : null;
        if (tab != _mailTab)
        {
            _mailTab = tab;
            _mailTabUrl = null;
            _mailNextFetch = default;
        }
        if (tab is null)
        {
            _mailItems = [];
            if (_hoverCard == MailCard && (_hoverTab is null || HoverCardFor(_hoverTab) is null)) HideHoverCard();
            CloseHiddenSite(GmailFeed.Host);
            return;
        }
        // Moved on in its tab (reading an email marks it read): fetched again soon.
        if (tab.Url != _mailTabUrl)
        {
            if (_mailTabUrl is not null) _mailNextFetch = Min(_mailNextFetch, _mailFetched + MailSoonest);
            _mailTabUrl = tab.Url;
        }
        if (DateTimeOffset.Now >= _mailNextFetch && !_mailFetching) _ = FetchMailAsync();
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    // ---- Fetching ----

    private async Task FetchMailAsync()
    {
        _mailFetching = true;
        try
        {
            var core = await HiddenSiteAsync(GmailFeed.Base);
            var xml = await MailFeedAsync(core);
            if (xml is null)
            {
                // Refused: Gmail's own sign-in for the site has lapsed, and opening Gmail renews it, if
                // you're signed in to Google; else it ends up at Google's sign-in.
                await GoToAsync(core, GmailFeed.Inbox(MailAccount), TimeSpan.FromSeconds(30));
                var landed = Uri.TryCreate(core.Source, UriKind.Absolute, out var u) ? u : null;
                if (GmailFeed.IsGmail(landed)) xml = await MailFeedAsync(core);
                // Back off Gmail itself, which is heavy to keep open.
                await GoToAsync(core, GmailFeed.Base, TimeSpan.FromSeconds(10));
                if (!GmailFeed.IsGmail(landed)) _mailState = MailState.SignedOut;
            }
            if (xml is not null && GmailFeed.Parse(xml) is { } feed)
            {
                _mailItems = feed.Items;
                _mailState = MailState.Ready;
                _mailNextFetch = DateTimeOffset.Now + MailRefresh;
            }
            else
            {
                if (_mailState != MailState.SignedOut) _mailState = MailState.Failed;
                _mailItems = [];
                _mailNextFetch = DateTimeOffset.Now + MailRetry;
            }
        }
        catch (Exception error)
        {
            App.LogCrash(error);
            if (_mailItems.Count == 0) _mailState = MailState.Failed;
            _mailNextFetch = DateTimeOffset.Now + MailRetry;
            CloseHiddenSite(GmailFeed.Host);
        }
        finally
        {
            _mailFetching = false;
            _mailFetched = DateTimeOffset.Now;
        }
        if (_hoverCard == MailCard) RenderMailCard();
    }

    /// <summary>The feed's text, or null if Gmail won't give it (signed out, or not answering).</summary>
    private async Task<string?> MailFeedAsync(CoreWebView2 core)
    {
        var script = $$"""
            (async () => {
              const stop = new AbortController();
              setTimeout(() => stop.abort(), 15000);
              try {
                const r = await fetch('{{GmailFeed.FeedPath(MailAccount)}}', { credentials: 'include', signal: stop.signal });
                return r.ok ? await r.text() : null;
              } catch { return null; }
            })()
            """;
        var reply = await EvaluateAsync(core, script, gesture: false);
        return reply is { ValueKind: System.Text.Json.JsonValueKind.String } text ? text.GetString() : null;
    }

    // ---- The card ----

    /// <summary>The card's about to show: from the last fetch at once, a fresh one following if that's a while ago.</summary>
    private void ShowingMailCard()
    {
        if (_hoverTab is { } tab && tab == _outlookMailTab) ShowingOutlookCard();
        else if (DateTimeOffset.Now - _mailFetched > TimeSpan.FromSeconds(30) && !_mailFetching) _ = FetchMailAsync();
        RenderMailCard();
        MailScroll.ChangeView(null, 0, null, disableAnimation: true);
    }

    /// <summary>What the mail card shows for the tab it's for: its unread emails, or why there aren't any and what to do about it.</summary>
    private (List<MailItem> Items, string? Message, string? Action) MailCardData()
    {
        if (_hoverTab is { } tab && tab == _outlookMailTab) return (_outlookMail, OutlookMessage(), OutlookAction());
        return _mailState switch
        {
            MailState.SignedOut => (_mailItems, "You're signed out of Gmail", "Sign in"),
            MailState.Failed => (_mailItems, "We couldn't reach Gmail", "Try again"),
            MailState.Loading => (_mailItems, "Loading…", null),
            _ => (_mailItems, null, null),
        };
    }

    /// <summary>The emails, a hairline between each two, as Arc's card lists them.</summary>
    private void RenderMailCard()
    {
        MailList.Children.Clear();
        var (items, message, action) = MailCardData();
        if (message is null && items.Count == 0) message = "No unread email";
        MailMessage.Show(message, action);
        if (message is not null) return;

        var primary = ThemeBrush("TextFillColorPrimaryBrush");
        var secondary = ThemeBrush("DriftSecondaryTextBrush");
        var divider = ThemeBrush("DriftDividerBrush");
        var now = DateTimeOffset.Now;
        foreach (var item in items)
        {
            if (MailList.Children.Count > 0)
                MailList.Children.Add(new Rectangle { Height = 1, Fill = divider, Margin = new Thickness(8, 2, 8, 2) });
            MailList.Children.Add(MailRow(item, now, primary, secondary));
        }
    }

    /// <summary>An email, as Arc's: who it's from and when; its subject, on one line; and the start of it, up to two.</summary>
    private Button MailRow(MailItem item, DateTimeOffset now, Brush primary, Brush secondary)
    {
        var grid = new Grid { RowSpacing = 1, ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var sender = new TextBlock { Text = item.Sender, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = primary, TextTrimming = TextTrimming.CharacterEllipsis };
        var age = new TextBlock { Text = GmailFeed.Age(item.Received, now), FontSize = 12, Foreground = secondary, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(age, 1);
        var subject = new TextBlock { Text = item.Subject, FontSize = 13, Foreground = primary, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetRow(subject, 1);
        // A conversation of several (Outlook): how many, after its subject, as Outlook shows it.
        if (item.Count > 1)
        {
            var count = new TextBlock { Text = $"({item.Count})", FontSize = 13, Foreground = primary, VerticalAlignment = VerticalAlignment.Top };
            Grid.SetRow(count, 1);
            Grid.SetColumn(count, 1);
            grid.Children.Add(count);
        }
        else Grid.SetColumnSpan(subject, 2);
        var snippet = new TextBlock { Text = item.Snippet, FontSize = 12, Foreground = secondary, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetRow(snippet, 2);
        Grid.SetColumnSpan(snippet, 2);
        grid.Children.Add(sender);
        grid.Children.Add(age);
        grid.Children.Add(subject);
        if (item.Snippet.Length > 0) grid.Children.Add(snippet);

        var row = new Button { Style = (Style)Application.Current.Resources["CardRowButtonStyle"], Content = grid };
        AutomationProperties.SetName(row, item.Count > 1 ? $"{item.Sender}: {item.Subject}, {item.Count} emails" : $"{item.Sender}: {item.Subject}");
        var tab = _hoverTab;
        row.Click += (_, _) =>
        {
            if (item.Link is { } link && Uri.TryCreate(link, UriKind.Absolute, out var page)) OpenInTab(tab, page);
            else OpenInTab(tab, item.MessageId is { } id ? GmailFeed.Message(id, MailAccount) : GmailFeed.Inbox(MailAccount));
        };
        return row;
    }

    /// <summary>Signed out: Gmail in its tab, to sign in. Couldn't reach it: asked again, by a fresh view.</summary>
    private void MailAction_Click(object sender, RoutedEventArgs e)
    {
        if (RetryOutlook()) return;
        if (_mailState == MailState.SignedOut)
        {
            OpenInTab(_mailTab, GmailFeed.Inbox(MailAccount));
            return;
        }
        _mailState = MailState.Loading;
        RenderMailCard();
        if (_mailFetching) return;
        CloseHiddenSite(GmailFeed.Host);
        _ = FetchMailAsync();
    }

    private void MailCompose_Click(object sender, RoutedEventArgs e)
    {
        if (_hoverTab is { } tab && tab == _outlookMailTab) OpenInTab(tab, OutlookApi.NewMail(OutlookHost));
        else OpenInTab(_mailTab, GmailFeed.Compose(MailAccount));
    }
}
