using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Drift.Models;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace Drift;

/// <summary>
/// Ask AI, as in Raycast: Tab in the command bar sends what you typed (not the completion) to the
/// Claude Code or Codex you've installed, and the card becomes the answer, streaming in under the
/// field, which then takes follow-ups. Esc stops an answer that's coming, then goes back to search,
/// then closes. Tab in an empty bar brings the last conversation back, if it was in the last five
/// minutes; nothing is kept beyond that, nor written anywhere. Claude is asked first, Codex if
/// Claude isn't signed in (Settings → AI swaps them). The process for the next question starts as
/// the bar opens, so it's ready by the time you press Tab; it's ended after five minutes unused.
/// No animation: the bar's used too often from the keyboard for any to help.
/// </summary>
public sealed partial class MainWindow
{
    private sealed class AskTurn
    {
        public required string Question { get; init; }
        public StringBuilder Answer { get; } = new();
        public AiActivity? Activity { get; set; }
        public AiFailed? Failure { get; set; }
        public string? Notice { get; set; }
        public bool Done { get; set; }
        /// <summary>How much of the answer is drawn: all that's arrived, at the next frame.</summary>
        public int Shown { get; set; }
        /// <summary>Where it's drawn: the question, then the answer, then what it's doing or what went wrong.</summary>
        public StackPanel? View { get; set; }
        public StackPanel? AnswerView { get; set; }
        public StackPanel? StatusView { get; set; }
        public string? StatusShown { get; set; }
        public bool Revealed => Shown >= Answer.Length;
    }

    private sealed class AskConversation
    {
        public required AiSession? Session { get; init; }
        public List<AskTurn> Turns { get; } = new();
        public DateTimeOffset LastActivity { get; set; } = DateTimeOffset.Now;
    }

    private AskConversation? _conversation;
    // Warmed when the bar opens, for the next conversation.
    private AiSession? _spareSession;
    private bool _askMode;
    private DispatcherQueueTimer? _askRender;
    private DispatcherQueueTimer? _askIdle;
    // Keeps to the end as the answer streams in, until you scroll up to read; back at the end, it follows again.
    private bool _askFollow = true;
    // Your own scrolling (wheel, scroll bar, arrows), as opposed to the view's own following.
    private bool _askUserScrolling;
    private bool _askScrollWatched;

    // ───────────────────────── Warming up ─────────────────────────

    /// <summary>As the bar opens: the process for the next question, started now.</summary>
    private async void WarmAskAi()
    {
        if (!Settings.Current.AskAiWithTab) return;
        StartAskIdleWatch();
        if (await ChooseProviderAsync() is not { } provider) return;
        if (_spareSession is { Alive: true } spare && spare.Provider == provider && spare.Model == ModelFor(provider)) return;
        if (_spareSession is { } stale && (stale.Provider != provider || stale.Model != ModelFor(provider)))
        {
            stale.Dispose();
            _spareSession = null;
        }
        if (_spareSession is null)
        {
            _spareSession = NewSession(provider);
            _ = _spareSession?.StartAsync().ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }

    private static string ModelFor(AiProvider provider) =>
        provider == AiProvider.Claude ? Settings.Current.ClaudeModel : Settings.Current.CodexModel;

    private static AiSession? NewSession(AiProvider provider) =>
        AiLocator.Find(provider) is not { } exe ? null
        : provider == AiProvider.Claude ? new ClaudeSession(exe, Settings.Current.ClaudeModel)
        : new CodexSession(exe, Settings.Current.CodexModel);

    private async Task<AiReadiness> ReadinessAsync(AiProvider provider)
    {
        // Only ready is taken on trust: you may have signed in since.
        if (AiReadinessCheck.Known.TryGetValue(provider, out var known) && known == AiReadiness.Ready) return known;
        var found = await AiReadinessCheck.CheckAsync(provider);
        AiReadinessCheck.Known[provider] = found;
        return found;
    }

    /// <summary>The one you ask first if it's signed in, else the other; only asks the second if it must.</summary>
    private async Task<AiProvider?> ChooseProviderAsync()
    {
        var first = Settings.Current.AskFirst;
        if (await ReadinessAsync(first) == AiReadiness.Ready) return first;
        var other = AskProviders.Other(first);
        return await ReadinessAsync(other) == AiReadiness.Ready ? other : null;
    }

    /// <summary>Settings has checked again (or an answer said you're signed out): forget what was known.</summary>
    internal void ForgetAiReadiness()
    {
        AiReadinessCheck.Known.Clear();
        _spareSession?.Dispose();
        _spareSession = null;
    }

    /// <summary>Five minutes with nothing asked: the processes go, and the conversation with them.</summary>
    private void StartAskIdleWatch()
    {
        if (_askIdle is not null) return;
        _askIdle = DispatcherQueue.CreateTimer();
        _askIdle.Interval = TimeSpan.FromSeconds(30);
        _askIdle.Tick += (_, _) =>
        {
            var last = _conversation?.LastActivity ?? _askWarmedAt;
            if (AskRecall.CanReopen(last, DateTimeOffset.Now) || _conversation?.Session?.Busy == true) return;
            if (_askMode && _commandBarOpen) return;
            EndConversation();
            _spareSession?.Dispose();
            _spareSession = null;
            _askIdle.Stop();
            _askIdle = null;
        };
        _askIdle.Start();
        _askWarmedAt = DateTimeOffset.Now;
    }

    private DateTimeOffset _askWarmedAt;

    private void EndConversation()
    {
        _conversation?.Session?.Dispose();
        _conversation = null;
    }

    // ───────────────────────── Asking ─────────────────────────

    /// <summary>Tab in the search bar: what's typed to AI, or (empty) the last conversation back.</summary>
    private async void AskAi()
    {
        var question = CommandPristine ? "" : CommandQuery;
        if (question.Length == 0)
        {
            if (_conversation is { } last && AskRecall.CanReopen(last.LastActivity, DateTimeOffset.Now))
            {
                EnterAskMode();
                RenderAllTurns();
            }
            return;
        }
        _askWarmedAt = DateTimeOffset.Now;
        EndConversation();
        EnterAskMode();
        var turn = new AskTurn { Question = question };
        var provider = await ChooseProviderAsync();
        if (provider is null)
        {
            _conversation = new AskConversation { Session = null };
            _conversation.Turns.Add(turn);
            turn.Notice = AskProviders.NoneReady(
                await ReadinessAsync(AiProvider.Claude), await ReadinessAsync(AiProvider.Codex));
            turn.Done = true;
            RenderAllTurns();
            return;
        }
        StartConversation(provider.Value, turn);
    }

    private void StartConversation(AiProvider provider, AskTurn turn)
    {
        EndConversation();
        var session = _spareSession is { Alive: true } spare && spare.Provider == provider && spare.Model == ModelFor(provider)
            ? spare : NewSession(provider);
        if (session == _spareSession) _spareSession = null;
        _conversation = new AskConversation { Session = session };
        _conversation.Turns.Add(turn);
        StartAskIdleWatch();
        UpdateAskModelLabel();
        RenderAllTurns();
        Send(turn);
    }

    /// <summary>Enter (or Tab) in the answer view: a follow-up in the same conversation.</summary>
    private void AskFollowUp()
    {
        var question = CommandBox.Text.Trim();
        if (question.Length == 0 || _conversation is not { Session: { } session } conversation || session.Busy) return;
        SetCommandText("");
        _typed = "";
        var turn = new AskTurn { Question = question };
        conversation.Turns.Add(turn);
        RenderAllTurns();
        Send(turn);
    }

    private void Send(AskTurn turn)
    {
        var conversation = _conversation!;
        conversation.LastActivity = DateTimeOffset.Now;
        _askFollow = true;
        conversation.Session!.Ask(turn.Question, e => DispatcherQueue.TryEnqueue(() => OnAskEvent(conversation, turn, e)));
    }

    private void OnAskEvent(AskConversation conversation, AskTurn turn, AiEvent e)
    {
        conversation.LastActivity = DateTimeOffset.Now;
        switch (e)
        {
            case AiText text:
                // Not a paragraph break before anything's been said.
                if (turn.Answer.Length > 0 || text.Text.Trim().Length > 0) turn.Answer.Append(text.Text);
                turn.Activity = null;
                break;
            case AiStatus status:
                turn.Activity = status.Activity;
                break;
            case AiDone:
                turn.Done = true;
                turn.Activity = null;
                break;
            case AiFailed failed:
                turn.Done = true;
                turn.Activity = null;
                turn.Failure = failed;
                if (failed.Failure == AiFailure.SignedOut) AiReadinessCheck.Known.TryRemove(conversation.Session!.Provider, out _);
                break;
        }
        if (conversation != _conversation || !_askMode || !_commandBarOpen) return;
        RenderStatus(turn);
        StartAskReveal(turn);
    }

    /// <summary>Esc while an answer's coming: stop it there.</summary>
    private bool StopAsking()
    {
        if (_conversation?.Session is not { Busy: true } session) return false;
        session.Stop();
        return true;
    }

    /// <summary>Ctrl+Enter after a failure: the same question to the other one, as a new conversation.</summary>
    private async void AskOtherProvider()
    {
        if (_conversation is not { Session: { } session } conversation || session.Busy) return;
        if (conversation.Turns.LastOrDefault() is not { Failure: not null } failed) return;
        var other = AskProviders.Other(session.Provider);
        if (await ReadinessAsync(other) != AiReadiness.Ready) return;
        StartConversation(other, new AskTurn { Question = failed.Question });
    }

    // ───────────────────────── The answer view ─────────────────────────

    private void EnterAskMode()
    {
        if (!_askScrollWatched)
        {
            _askScrollWatched = true;
            WatchAskScrolling();
        }
        _askMode = true;
        _completion = null;
        SetCommandText("");
        _typed = "";
        CommandBox.PlaceholderText = "Ask a follow-up…";
        CommandResults.Visibility = Visibility.Collapsed;
        CommandDivider.Visibility = Visibility.Visible;
        AskScroller.Visibility = Visibility.Visible;
        AskScroller.MaxHeight = AskMaxHeight();
        CommandFavicon.Visibility = Visibility.Collapsed;
        CommandGlyph.Visibility = Visibility.Visible;
        CommandGlyph.Glyph = ""; // sparkle-ish: the AI's
        UpdateAskModelLabel();
        UpdateAskHint();
    }

    /// <summary>Esc from the answer: back to searching, with the question in the field.</summary>
    private void LeaveAskMode(bool restoreQuestion)
    {
        if (!_askMode) return;
        _askMode = false;
        _askRender?.Stop();
        AskScroller.Visibility = Visibility.Collapsed;
        AskTranscript.Children.Clear();
        foreach (var turn in _conversation?.Turns ?? []) turn.View = turn.AnswerView = turn.StatusView = null;
        CommandGlyph.Glyph = "";
        CommandBox.PlaceholderText = "Search or Enter URL...";
        AskModelLabel.Visibility = Visibility.Collapsed;
        if (!restoreQuestion) return;
        var question = _conversation?.Turns.LastOrDefault()?.Question ?? "";
        SetCommandText(question);
        _typed = question;
        CommandBox.Select(question.Length, 0);
        UpdateCommandResults();
        UpdateAskHint();
    }

    /// <summary>No taller than the suggestions get in this window: eight rows, or as many as fit.</summary>
    private double AskMaxHeight()
    {
        var room = Root.ActualHeight - CommandCard.Margin.Top - 52 - 1 - 12 - 16;
        var fit = Math.Max(3, (int)(room / CommandRowHeight));
        return Math.Min(CommandRowsMax, fit) * CommandRowHeight + 12;
    }

    private void UpdateAskModelLabel()
    {
        if (!_askMode || _conversation?.Session is not { } session)
        {
            AskModelLabel.Visibility = Visibility.Collapsed;
            return;
        }
        AskModelLabel.Text = AiNames.Model(session.Model);
        AskModelLabel.Visibility = Visibility.Visible;
    }

    /// <summary>"Ask AI  Tab", at the end of the field whenever there's something typed to ask.</summary>
    private void UpdateAskHint()
    {
        var show = _commandBarOpen && !_askMode && _siteSearch is null && Settings.Current.AskAiWithTab && !CommandPristine && CommandQuery.Length > 0
            && !CommandQuery.StartsWith('@');
        AskHint.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Draws what's arrived at most thirty times a second, rather than once per piece.</summary>
    private void StartAskReveal(AskTurn turn)
    {
        _askRendering = turn;
        if (_askRender is null)
        {
            _askRender = DispatcherQueue.CreateTimer();
            _askRender.Interval = TimeSpan.FromMilliseconds(33);
            _askRender.Tick += AskRender_Tick;
        }
        if (!_askRender.IsRunning) _askRender.Start();
    }

    private AskTurn? _askRendering;

    private void AskRender_Tick(DispatcherQueueTimer sender, object args)
    {
        if (_askRendering is not { } turn || !_askMode || !_commandBarOpen)
        {
            sender.Stop();
            return;
        }
        var answer = turn.Answer.ToString();
        if (!turn.Revealed)
        {
            turn.Shown = answer.Length;
            RenderAnswer(turn);
        }
        RenderStatus(turn);
        if (turn.Revealed && turn.Done)
        {
            sender.Stop();
            // Finished: it stays where it is from here on.
            _askFollow = false;
        }
    }

    private void RenderAllTurns()
    {
        AskTranscript.Children.Clear();
        var turns = _conversation?.Turns ?? [];
        foreach (var turn in turns)
        {
            // Earlier answers (and any brought back) show whole.
            if (turn != turns[^1] || turn.Done) turn.Shown = turn.Answer.Length;
            turn.View = new StackPanel { Spacing = 8 };
            turn.AnswerView = new StackPanel { Spacing = 8 };
            turn.StatusView = new StackPanel { Spacing = 4 };
            turn.StatusShown = null;
            // After the first, each question heads its answer.
            if (turn != turns[0])
                turn.View.Children.Add(new TextBlock
                {
                    Text = turn.Question,
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 12, 0, 0),
                    IsTextSelectionEnabled = true,
                });
            turn.View.Children.Add(turn.AnswerView);
            turn.View.Children.Add(turn.StatusView);
            AskTranscript.Children.Add(turn.View);
            RenderAnswer(turn);
            RenderStatus(turn);
        }
        _askFollow = true;
        ScrollAskToEnd();
        if (turns.Count > 0 && !(turns[^1].Done && turns[^1].Revealed)) StartAskReveal(turns[^1]);
    }

    /// <summary>
    /// What's showing of the answer, drawn over what was there: only what's changed is touched, and
    /// text that grew is updated where it is, so nothing under the pointer is pulled out from under it.
    /// </summary>
    private void RenderAnswer(AskTurn turn)
    {
        if (turn.AnswerView is not { } view) return;
        var shown = AskReveal.Close(turn.Answer.ToString(0, Math.Min(turn.Shown, turn.Answer.Length)).Trim());
        MarkdownView.Update(view, Markdown.Parse(shown), OpenAskLink);
        view.Visibility = view.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_askFollow) ScrollAskToEnd();
    }

    /// <summary>What it's doing, or what went wrong, under the answer; redrawn only when it changes.</summary>
    private void RenderStatus(AskTurn turn)
    {
        if (turn.StatusView is not { } view) return;
        var lines = new List<string>();
        var showing = turn.Shown > 0 && turn.Answer.ToString().Trim().Length > 0;
        string? status = turn.Activity switch
        {
            AiActivity.Searching => "Searching the web…",
            AiActivity.Thinking => "Thinking…",
            _ => !turn.Done && !showing ? "…" : null,
        };
        if (status is not null) lines.Add(status);
        if (turn.Notice is { } notice) lines.Add(notice);
        var conversation = _conversation;
        if (turn.Failure is { } failure && conversation?.Session is { } session)
        {
            lines.Add(AiErrors.Describe(session.Provider, failure, TimeZoneInfo.Local));
            var other = AskProviders.Other(session.Provider);
            if (AiReadinessCheck.Known.GetValueOrDefault(other) == AiReadiness.Ready && conversation.Turns[^1] == turn)
                lines.Add($"Ask {AskProviders.Name(other)} instead   Ctrl+Enter");
        }
        var key = string.Join("\n", lines);
        if (key == turn.StatusShown) return;
        turn.StatusShown = key;
        view.Children.Clear();
        foreach (var line in lines) view.Children.Add(Dim(line));
        view.Visibility = lines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_askFollow) ScrollAskToEnd();
    }

    private static TextBlock Dim(string text) => new()
    {
        Text = text,
        FontSize = 13,
        TextWrapping = TextWrapping.Wrap,
        Foreground = ThemeBrush("DriftSecondaryTextBrush"),
        IsTextSelectionEnabled = true,
    };

    private void ScrollAskToEnd()
    {
        AskScroller.UpdateLayout();
        AskScroller.ChangeView(null, AskScroller.ScrollableHeight, null, disableAnimation: true);
    }

    private void AskScroller_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        // Only your scrolling decides whether it follows; the answer growing under it doesn't.
        if (!_askUserScrolling) return;
        _askFollow = AskScroller.VerticalOffset >= AskScroller.ScrollableHeight - 8;
        if (!e.IsIntermediate) _askUserScrolling = false;
    }

    /// <summary>Wheel and scroll bar, seen even when the scroll viewer handles them itself.</summary>
    private void WatchAskScrolling()
    {
        AskScroller.AddHandler(UIElement.PointerWheelChangedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, e) =>
        {
            _askUserScrolling = true;
            // Up: stop following now, before the next bit of answer pulls it back down.
            if (e.GetCurrentPoint(AskScroller).Properties.MouseWheelDelta > 0) _askFollow = false;
        }), handledEventsToo: true);
        AskScroller.AddHandler(UIElement.PointerPressedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => _askUserScrolling = true), handledEventsToo: true);
    }

    /// <summary>A link in an answer: into a new tab, and the bar gets out of the way.</summary>
    private async void OpenAskLink(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return;
        CloseCommandBar(focusPage: false);
        var opened = await CreateTabAsync(uri, TabPlace.Regular, activate: true);
        opened.View.Focus(FocusState.Programmatic);
    }

    /// <summary>Ctrl+Shift+C in the answer view: the last answer, as Markdown.</summary>
    private bool CopyAskAnswer()
    {
        if (!_commandBarOpen || !_askMode) return false;
        var answer = _conversation?.Turns.LastOrDefault(t => t.Answer.Length > 0)?.Answer.ToString().Trim();
        if (string.IsNullOrEmpty(answer)) return true;
        var package = new DataPackage();
        package.SetText(answer);
        Clipboard.SetContent(package);
        ShowCopiedToast();
        return true;
    }

    /// <summary>The answer view's keys; true if it took the key.</summary>
    private bool AskKeyDown(Windows.System.VirtualKey key)
    {
        var ctrl = IsKeyDown(0x11);
        var shift = IsKeyDown(0x10);
        switch (key)
        {
            case Windows.System.VirtualKey.Enter when ctrl:
                AskOtherProvider();
                return true;
            case Windows.System.VirtualKey.Enter:
            case Windows.System.VirtualKey.Tab:
                AskFollowUp();
                return true;
            case Windows.System.VirtualKey.Escape:
                if (!StopAsking()) LeaveAskMode(restoreQuestion: true);
                return true;
            case Windows.System.VirtualKey.C when ctrl && shift:
                CopyAskAnswer();
                return true;
            case Windows.System.VirtualKey.Up:
            case Windows.System.VirtualKey.Down:
                _askUserScrolling = true;
                AskScroller.ChangeView(null, AskScroller.VerticalOffset + (key == Windows.System.VirtualKey.Down ? 60 : -60), null);
                return true;
        }
        return false;
    }
}

/// <summary>Markdown blocks (Markdown.cs) as WinUI text, in the command bar's type.</summary>
internal static class MarkdownView
{
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    /// <summary>
    /// The panel brought up to date with these blocks: a block that hasn't changed is left alone,
    /// one that has is updated in place if it's the same kind (text that grew, mostly), and only
    /// otherwise replaced.
    /// </summary>
    public static void Update(Panel panel, IReadOnlyList<MdBlock> blocks, Action<string> openLink)
    {
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            var key = Key(block);
            if (i < panel.Children.Count && panel.Children[i] is FrameworkElement existing)
            {
                if (existing.Tag as string == key) continue;
                if (existing.Tag is string old && Shape(old) == Shape(key) && TryUpdate(existing, block, openLink))
                {
                    existing.Tag = key;
                    continue;
                }
                var replacement = Create(block, openLink);
                replacement.Tag = key;
                panel.Children[i] = replacement;
                continue;
            }
            var element = Create(block, openLink);
            element.Tag = key;
            panel.Children.Add(element);
        }
        while (panel.Children.Count > blocks.Count) panel.Children.RemoveAt(panel.Children.Count - 1);
    }

    /// <summary>Everything that shows, as a string: the same key, the same look.</summary>
    private static string Key(MdBlock block) =>
        $"{block.Kind}|{block.Level}|{block.Number}|{block.Language}|{block.Code}|" + Key(block.Spans)
        + (block.Rows is { } rows ? "\u0003" + string.Join("\u0004", rows.Select(r => string.Join("\u0005", r.Select(Key)))) : "")
        + (block.Align is { } align ? "\u0003" + string.Join(",", align) : "");

    private static string Key(IEnumerable<MdSpan> spans) =>
        string.Join("\u0001", spans.Select(s => $"{(int)s.Style}\u0002{s.Link}\u0002{s.Text}"));

    /// <summary>The part of a key that decides which element draws it.</summary>
    private static string Shape(string key)
    {
        var parts = key.Split('|', 4);
        // A heading's level sets its size, so a different one is a different element; a list item's
        // depth is only a margin, updated in place.
        return parts[0] == nameof(MdKind.Heading) ? $"{parts[0]}|{parts[1]}" : parts[0];
    }

    private static bool TryUpdate(FrameworkElement element, MdBlock block, Action<string> openLink)
    {
        switch (block.Kind)
        {
            case MdKind.Code when element is Border { Child: TextBlock code }:
                code.Text = block.Code ?? "";
                return true;
            case MdKind.Rule:
                return true;
            case MdKind.Table when element is Border { Child: Grid table } && table.ColumnDefinitions.Count == block.Align!.Count:
                FillTable(table, block, openLink);
                return true;
            case MdKind.Bullet or MdKind.Numbered when element is Grid { Children.Count: 2 } item
                && item.Children[0] is TextBlock marker && item.Children[1] is RichTextBlock itemText:
                item.Margin = new Thickness(block.Level * 18, 0, 0, 0);
                marker.Text = block.Kind == MdKind.Bullet ? "•" : $"{block.Number}.";
                SetSpans(itemText, block.Spans, openLink);
                return true;
            case MdKind.Quote when element is Border { Child: RichTextBlock quote }:
                SetSpans(quote, block.Spans, openLink);
                return true;
            case MdKind.Paragraph or MdKind.Heading when element is RichTextBlock text:
                SetSpans(text, block.Spans, openLink);
                return true;
        }
        return false;
    }

    private static FrameworkElement Create(MdBlock block, Action<string> openLink)
    {
        switch (block.Kind)
        {
            case MdKind.Code:
                return new Border
                {
                    Background = Brush("DriftPillBrush"),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 8, 10, 8),
                    Child = new TextBlock
                    {
                        Text = block.Code ?? "",
                        FontFamily = Mono,
                        FontSize = 13,
                        TextWrapping = TextWrapping.Wrap,
                        IsTextSelectionEnabled = true,
                    },
                };
            case MdKind.Table:
                var grid = new Grid();
                foreach (var _ in block.Align!) grid.ColumnDefinitions.Add(new ColumnDefinition());
                FillTable(grid, block, openLink);
                return new Border
                {
                    BorderBrush = Brush("DriftDividerBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Child = grid,
                };
            case MdKind.Rule:
                return new Border { Height = 1, Background = Brush("DriftDividerBrush"), Margin = new Thickness(0, 4, 0, 4) };
            case MdKind.Bullet or MdKind.Numbered:
                var item = new Grid { Margin = new Thickness(block.Level * 18, 0, 0, 0), ColumnSpacing = 8 };
                item.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(block.Kind == MdKind.Bullet ? 10 : 20) });
                item.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                item.Children.Add(new TextBlock
                {
                    Text = block.Kind == MdKind.Bullet ? "•" : $"{block.Number}.",
                    FontSize = 14,
                    Foreground = Brush("DriftSecondaryTextBrush"),
                    HorizontalAlignment = block.Kind == MdKind.Bullet ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                });
                var text = Text(block.Spans, 14, openLink);
                Grid.SetColumn(text, 1);
                item.Children.Add(text);
                return item;
            case MdKind.Quote:
                var quote = Text(block.Spans, 14, openLink);
                quote.Foreground = Brush("DriftSecondaryTextBrush");
                return new Border
                {
                    BorderBrush = Brush("DriftDividerBrush"),
                    BorderThickness = new Thickness(2, 0, 0, 0),
                    Padding = new Thickness(10, 0, 0, 0),
                    Child = quote,
                };
            case MdKind.Heading:
                var heading = Text(block.Spans, block.Level switch { 1 => 18, 2 => 16, _ => 14 }, openLink);
                heading.FontWeight = FontWeights.SemiBold;
                heading.Margin = new Thickness(0, 4, 0, 0);
                return heading;
            default:
                return Text(block.Spans, 14, openLink);
        }
    }

    /// <summary>
    /// A table's cells into its grid: the header in semibold on a tint, a hairline between rows,
    /// each column as wide as its longest cell asks (within reason) and the text wrapping in it.
    /// Cells already there are updated where they are, so rows can stream in.
    /// </summary>
    private static void FillTable(Grid grid, MdBlock block, Action<string> openLink)
    {
        var rows = block.Rows!;
        var columns = block.Align!.Count;
        for (var c = 0; c < columns; c++)
        {
            var longest = rows.Max(r => r[c].Sum(s => s.Text.Length));
            grid.ColumnDefinitions[c].Width = new GridLength(Math.Clamp(longest, 4, 40), GridUnitType.Star);
        }
        while (grid.RowDefinitions.Count < rows.Count) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        while (grid.RowDefinitions.Count > rows.Count) grid.RowDefinitions.RemoveAt(grid.RowDefinitions.Count - 1);

        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < columns; c++)
            {
                var index = r * columns + c;
                RichTextBlock text;
                if (index < grid.Children.Count && grid.Children[index] is Border { Child: RichTextBlock existing })
                    text = existing;
                else
                {
                    text = new RichTextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
                    var cell = new Border
                    {
                        Padding = new Thickness(10, 6, 10, 6),
                        BorderBrush = Brush("DriftDividerBrush"),
                        // A hairline above every row but the header, so adding rows never touches the ones before.
                        BorderThickness = new Thickness(0, r > 0 ? 1 : 0, 0, 0),
                        Background = r == 0 ? Brush("DriftPillBrush") : null,
                        // The header's tint keeps inside the table's rounded corners.
                        CornerRadius = r == 0 ? new CornerRadius(c == 0 ? 5 : 0, c == columns - 1 ? 5 : 0, 0, 0) : default,
                        Child = text,
                    };
                    if (r == 0) text.FontWeight = FontWeights.SemiBold;
                    Grid.SetRow(cell, r);
                    Grid.SetColumn(cell, c);
                    grid.Children.Add(cell);
                }
                text.TextAlignment = block.Align[c] switch
                {
                    MdAlign.Center => TextAlignment.Center,
                    MdAlign.Right => TextAlignment.Right,
                    _ => TextAlignment.Left,
                };
                SetSpans(text, rows[r][c], openLink);
            }
        }
        while (grid.Children.Count > rows.Count * columns) grid.Children.RemoveAt(grid.Children.Count - 1);
    }

    private static RichTextBlock Text(IEnumerable<MdSpan> spans, double size, Action<string> openLink)
    {
        var block = new RichTextBlock { FontSize = size, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        SetSpans(block, spans, openLink);
        return block;
    }

    private static void SetSpans(RichTextBlock block, IEnumerable<MdSpan> spans, Action<string> openLink)
    {
        var size = block.FontSize;
        var paragraph = new Paragraph();
        foreach (var span in spans)
        {
            var run = new Run { Text = span.Text };
            if (span.Style.HasFlag(MdStyle.Bold)) run.FontWeight = FontWeights.SemiBold;
            if (span.Style.HasFlag(MdStyle.Italic)) run.FontStyle = Windows.UI.Text.FontStyle.Italic;
            if (span.Style.HasFlag(MdStyle.Code))
            {
                run.FontFamily = Mono;
                run.FontSize = size - 1;
            }
            if (span.Link is { } url)
            {
                var link = new Hyperlink();
                link.Inlines.Add(run);
                link.Click += (_, _) => openLink(url);
                ToolTipService.SetToolTip(link, url);
                paragraph.Inlines.Add(link);
            }
            else paragraph.Inlines.Add(run);
        }
        block.Blocks.Clear();
        block.Blocks.Add(paragraph);
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
