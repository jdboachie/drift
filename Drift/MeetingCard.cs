using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Drift.Models;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Drift;

/// <summary>
/// The next meeting, at the foot of the sidebar above the mini player (MeetingBar, in Core, has its
/// rules). From up to an hour before, a row on a tint of the accent with its name and countdown.
/// Hovered, it grows upward over the tabs into a card, as the mini player does: when (the countdown,
/// then the hours), the call and who's coming, each with its icon in the calendar icon's column, and
/// Join once it can be joined. Ten minutes before, it opens itself and stays open. ⌄ collapses it back
/// to the row, × closes it; either way it's back, open, with Join five minutes before. The mini player
/// growing pushes it up rather than covering it. Clicking it opens the meeting in its calendar.
/// </summary>
public sealed partial class MainWindow
{
    // The meeting the card is for, and the calendar tab it's from.
    private CalendarEvent? _meeting;
    private BrowserTab? _meetingTab;
    private MeetingBar.Away? _meetingAway;

    private bool _meetingExpanded;
    private bool _pointerOnMeeting;
    // Collapsed with the pointer on it: it stays shut until the pointer leaves and comes back.
    private bool _meetingHoverHeld;
    private int _meetingGeneration;
    private DispatcherQueueTimer? _meetingHoverTimer;
    private CompositionPropertySet? _meetingGrow;
    private string? _meetingFaces;

    // How much taller than their rows the cards are open, for the cut over the tabs and the push.
    private float _meetingGrowth;
    private float _mediaGrowth;

    /// <summary>The card, for the next meeting on a pinned Google or Outlook calendar, as of now.</summary>
    private void UpdateMeetingCard()
    {
        if (MeetingCard is null) return;
        var now = DateTimeOffset.Now;
        IEnumerable<CalendarEvent> google = _calendarTab is not null ? _calendarEvents : [];
        IReadOnlyList<CalendarEvent> outlook = _outlookCalendarTab is not null ? _outlookEvents : [];
        var next = MeetingClock.Upcoming(google.Concat(outlook), now);
        if (next is not null && _meetingAway is { Closed: true } && MeetingBar.StillAway(_meetingAway, next, now)) next = null;
        if (next is null)
        {
            HideMeetingCard();
            return;
        }
        var arriving = _meeting is null;
        _meeting = next;
        _meetingTab = outlook.Contains(next) ? _outlookCalendarTab : _calendarTab;
        FillMeetingCard(next, now);
        if (arriving) ShowMeetingCard();
        SyncMeetingCard();
    }

    private void FillMeetingCard(CalendarEvent m, DateTimeOffset now)
    {
        var card = MeetingCard;
        card.Title.Text = m.Title;
        card.Countdown.Text = MeetingBar.Short(m, now);
        card.Soon.Text = MeetingBar.Soon(m, now);
        card.Hours.Text = MeetingBar.Hours(m, TimeZoneInfo.Local, CultureInfo.CurrentCulture);
        var call = MeetingBar.CallName(m.JoinUrl);
        card.Call.Text = call ?? "";
        card.CallRow.Visibility = call is null ? Visibility.Collapsed : Visibility.Visible;
        // Only with someone besides you.
        card.PeopleRow.Visibility = m.People.Count >= 2 ? Visibility.Visible : Visibility.Collapsed;
        card.People.Text = MeetingBar.PeopleText(m.People.Count);
        ShowFaces(m.People);
        card.Join.Visibility = MeetingClock.CanJoin(m, now) ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(card, $"{m.Title}, {MeetingBar.Soon(m, now).ToLower(CultureInfo.CurrentCulture)}");
    }

    /// <summary>
    /// Each person's initial on their colour, overlapping, ringed in the card's colour; then "+3". Each face
    /// has its own tooltip, the person's name; "+3" has none.
    /// </summary>
    private void ShowFaces(IReadOnlyList<string> people)
    {
        var key = string.Join("\n", people);
        if (key == _meetingFaces) return;
        _meetingFaces = key;
        MeetingCard.Faces.Children.Clear();
        var faces = MeetingBar.FacesOf(people);
        for (var i = 0; i < faces.Count; i++)
        {
            var (text, color) = faces[i];
            var face = new Border
            {
                Height = 22,
                MinWidth = 22,
                Padding = new Thickness(text.Length > 1 ? 4 : 0, 0, text.Length > 1 ? 4 : 0, 0),
                CornerRadius = new CornerRadius(11),
                BorderThickness = new Thickness(2),
                BorderBrush = ThemeBrush("DriftFaceRingBrush"),
                Background = ThemeBrush(color < 0 ? "DriftFaceMoreBrush" : $"DriftFace{color}Brush"),
                Margin = new Thickness(i == 0 ? 0 : -6, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 1),
                },
            };
            if (color >= 0)
            {
                ToolTipService.SetToolTip(face, people[i]);
                AutomationProperties.SetName(face, people[i]);
            }
            MeetingCard.Faces.Children.Add(face);
        }
    }

    private void ShowMeetingCard()
    {
        MeetingBarRow.Visibility = MeetingCard.Visibility = Visibility.Visible;
        PlaceMeetingCard();
        MeetingCard.CenterPoint = new Vector3(100, CompactHeight / 2, 0);
        Spring.To(MeetingCard, "Scale", Vector3.One, 0.3, 0, from: new Vector3(0.94f, 0.94f, 1));
        if (!_uiSettings.AnimationsEnabled)
        {
            MeetingCard.Opacity = 0;
            Ui.Fade(MeetingCard, 1);
        }
        BindMeetingPush();
    }

    private void HideMeetingCard()
    {
        if (_meeting is null) return;
        _meeting = null;
        _meetingTab = null;
        CollapseMeetingNow();
        MeetingBarRow.Visibility = MeetingCard.Visibility = Visibility.Collapsed;
        MeetingCard.Opacity = 1;
        CutTabs();
    }

    /// <summary>
    /// The card is showing this meeting where it can be seen: the sidebar's out, the Library isn't in its
    /// place, and it hasn't been closed. Otherwise the reminder comes instead (ShowMeetings).
    /// </summary>
    private bool MeetingCardShows(CalendarEvent m) =>
        !_sidebarHidden && !_libraryOpen && MeetingCard.Visibility == Visibility.Visible
        && _meeting is { } shown && MeetingClock.Key(shown) == MeetingClock.Key(m);

    /// <summary>Above the mini player's row when it's showing, else at the foot.</summary>
    private void PlaceMeetingCard() =>
        MeetingCard.Margin = new Thickness(0, 4, 4, MediaBarRow.Visibility == Visibility.Visible ? CompactHeight + 4 : 0);

    /// <summary>Open or shut, as it should be now: hovered, or opening itself and not put away.</summary>
    private void SyncMeetingCard()
    {
        if (_meeting is not { } m) return;
        var now = DateTimeOffset.Now;
        var open = (_pointerOnMeeting && !_meetingHoverHeld)
            || (MeetingBar.OpensItself(m, now) && !MeetingBar.StillAway(_meetingAway, m, now));
        if (open != _meetingExpanded) SetMeetingExpanded(open);
        // Open, the row's countdown gives way to close (the card has the countdown); shut, the other way round.
        MeetingCard.Countdown.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        MeetingCard.Close.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        MeetingCard.Title.FontWeight = open ? FontWeights.SemiBold : FontWeights.Normal;
    }

    internal void MeetingCard_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerOnMeeting = true;
        // A pointer passing by on its way elsewhere doesn't open it.
        HoverMeeting(TimeSpan.FromMilliseconds(150));
    }

    internal void MeetingCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerOnMeeting = false;
        _meetingHoverHeld = false;
        HoverMeeting(TimeSpan.FromMilliseconds(300));
    }

    private void HoverMeeting(TimeSpan after)
    {
        if (_meetingHoverTimer is null)
        {
            _meetingHoverTimer = DispatcherQueue.CreateTimer();
            _meetingHoverTimer.IsRepeating = false;
            _meetingHoverTimer.Tick += (_, _) => Safely(SyncMeetingCard);
        }
        _meetingHoverTimer.Interval = after;
        _meetingHoverTimer.Start();
    }

    /// <summary>The meeting's page in its calendar, or the calendar where there's none.</summary>
    internal void MeetingCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // A tap on Join, ⌄ or × is theirs alone: the card's buttons don't stop it reaching the card.
        for (var at = e.OriginalSource as DependencyObject; at is not null && at != MeetingCard.Body; at = VisualTreeHelper.GetParent(at))
            if (at is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase) return;
        if (_meeting is not { } m || _meetingTab is not { } tab) return;
        if (m.WebLink is { } link && Uri.TryCreate(link, UriKind.Absolute, out var page)) OpenInTab(tab, page);
        else if (m.GoogleEid is { } eid && tab == _calendarTab) OpenInCalendarTab(GoogleCalendarLinks.Event(eid, CalendarAccount));
        else Activate(tab);
    }

    internal void MeetingJoinCard_Click(object sender, RoutedEventArgs e)
    {
        if (_meeting?.JoinUrl is { } url) JoinMeeting(url);
    }

    /// <summary>⌄: back to the row, which stays, until Join.</summary>
    internal void MeetingCollapse_Click(object sender, RoutedEventArgs e)
    {
        if (_meeting is not { } m) return;
        _meetingAway = new MeetingBar.Away(MeetingClock.Key(m), Closed: false, DateTimeOffset.Now);
        _meetingHoverHeld = _pointerOnMeeting;
        SyncMeetingCard();
    }

    /// <summary>×: gone, until Join.</summary>
    internal void MeetingClose_Click(object sender, RoutedEventArgs e)
    {
        if (_meeting is not { } m) return;
        _meetingAway = new MeetingBar.Away(MeetingClock.Key(m), Closed: true, DateTimeOffset.Now);
        Ui.Fade(MeetingCard, 0, () => Safely(UpdateMeetingCard));
    }

    /// <summary>
    /// Grows the row up into the card, or back, as the mini player does (SetMediaExpanded): the card
    /// takes its full height at once and is cut back to the row, and the cut springs open (or shut).
    /// </summary>
    private void SetMeetingExpanded(bool expand)
    {
        _meetingExpanded = expand;
        var generation = ++_meetingGeneration;
        var card = MeetingCard;
        var visual = ElementCompositionPreview.GetElementVisual(card.Body);
        var compositor = visual.Compositor;
        var animate = _uiSettings.AnimationsEnabled;

        if (expand)
        {
            card.Details.Visibility = Visibility.Visible;
            card.Height = double.NaN;
        }
        card.UpdateLayout();
        float width = (float)card.ActualWidth, height = (float)card.ActualHeight;
        _meetingGrowth = Math.Max(0, height - CompactHeight);

        if (_meetingGrow is null || visual.Clip is null)
        {
            _meetingGrow = compositor.CreatePropertySet();
            _meetingGrow.InsertScalar("T", 0);
        }
        BindGrow(card.Body, card.Back, _meetingGrow, width, height);
        CutTabs();

        // The card and its lines fade in as it opens, and the row's tint out.
        card.Plate.OpacityTransition ??= new ScalarTransition { Duration = TimeSpan.FromMilliseconds(150) };
        card.Details.OpacityTransition ??= new ScalarTransition { Duration = TimeSpan.FromMilliseconds(150) };
        card.Tint.OpacityTransition ??= new ScalarTransition { Duration = TimeSpan.FromMilliseconds(150) };
        card.Plate.Opacity = card.Details.Opacity = expand ? 1 : 0;
        card.Tint.Opacity = expand ? 0 : 1;

        if (animate)
        {
            // From wherever it's got to: hovering again while it shuts opens it again from there.
            var spring = compositor.CreateSpringScalarAnimation();
            spring.FinalValue = expand ? 1 : 0;
            spring.DampingRatio = expand ? 0.68f : 0.75f;
            spring.Period = TimeSpan.FromSeconds(0.3 / (2 * Math.PI));
            _meetingGrow.StartAnimation("T", spring);
        }
        else
        {
            _meetingGrow.InsertScalar("T", expand ? 1 : 0);
        }
        if (!expand) _ = FinishMeetingCollapseAsync(generation, animate ? 450 : 0);
    }

    /// <summary>
    /// A card growing from its row (the meeting's, the mini player's): it's cut back to the row's height at
    /// <c>T</c> 0 and shown whole at 1, the cut rising from its foot; past 1 the cut rises above its top and
    /// the card stretches up to fill it, its contents staying where they are. Bound to its size, so it's
    /// bound again when that changes (the sidebar resized while it's open).
    /// </summary>
    private static void BindGrow(UIElement bodyElement, UIElement backElement, CompositionPropertySet grow, float width, float height)
    {
        var body = ElementCompositionPreview.GetElementVisual(bodyElement);
        var compositor = body.Compositor;
        var shape = compositor.CreateRoundedRectangleGeometry();
        shape.CornerRadius = new Vector2(8);
        ExpressionAnimation Bind(string expression)
        {
            var animation = compositor.CreateExpressionAnimation(expression);
            animation.SetReferenceParameter("p", grow);
            animation.SetScalarParameter("w", width);
            animation.SetScalarParameter("h", height);
            animation.SetScalarParameter("c", CompactHeight);
            return animation;
        }
        shape.StartAnimation("Offset", Bind("Vector2(0, (h - c) * (1 - p.T))"));
        shape.StartAnimation("Size", Bind("Vector2(w, c + (h - c) * p.T)"));
        body.Clip = compositor.CreateGeometricClip(shape);

        var back = ElementCompositionPreview.GetElementVisual(backElement);
        back.CenterPoint = new Vector3(0, height, 0);
        back.StartAnimation("Scale", Bind("Vector3(1, 1 + Max(p.T - 1, 0) * (h - c) / h, 1)"));
    }

    /// <summary>Resized while open or shutting (the sidebar dragged wider): its cut, the tabs' and the push follow.</summary>
    internal void MeetingCard_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_meetingGrow is null || ElementCompositionPreview.GetElementVisual(MeetingCard.Body).Clip is null) return;
        var (width, height) = ((float)e.NewSize.Width, (float)e.NewSize.Height);
        BindGrow(MeetingCard.Body, MeetingCard.Back, _meetingGrow, width, height);
        _meetingGrowth = Math.Max(0, height - CompactHeight);
        CutTabs();
    }

    /// <summary>The same for the mini player's card (MediaControls.cs).</summary>
    internal void MediaCard_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_mediaGrow is null || ElementCompositionPreview.GetElementVisual(MediaCard.Body).Clip is null) return;
        var (width, height) = ((float)e.NewSize.Width, (float)e.NewSize.Height);
        BindGrow(MediaCard.Body, MediaCard.Back, _mediaGrow, width, height);
        _mediaGrowth = Math.Max(0, height - CompactHeight);
        CutTabs();
        BindMeetingPush();
    }

    /// <summary>Shut: back to the row's own height, once the cut has closed over the card.</summary>
    private async Task FinishMeetingCollapseAsync(int generation, int afterMs)
    {
        if (afterMs > 0) await Task.Delay(afterMs);
        if (generation != _meetingGeneration || _meetingExpanded) return;
        MeetingCard.Details.Visibility = Visibility.Collapsed;
        MeetingCard.Height = CompactHeight;
        ElementCompositionPreview.GetElementVisual(MeetingCard.Body).Clip = null;
        var back = ElementCompositionPreview.GetElementVisual(MeetingCard.Back);
        back.StopAnimation("Scale");
        back.Scale = Vector3.One;
        _meetingGrow?.StopAnimation("T");
        _meetingGrow?.InsertScalar("T", 0);
        CutTabs();
    }

    /// <summary>Gone (no meeting): the card shuts at once.</summary>
    private void CollapseMeetingNow()
    {
        if (!_meetingExpanded) return;
        _meetingExpanded = false;
        var generation = ++_meetingGeneration;
        MeetingCard.Plate.Opacity = MeetingCard.Details.Opacity = 0;
        MeetingCard.Tint.Opacity = 1;
        _ = FinishMeetingCollapseAsync(generation, 0);
    }

    /// <summary>
    /// The cards are see-through, like a selected tab, so the tabs they rise over are cut off above
    /// them: the mini player's growth (which pushes the meeting up) and the meeting's own, together.
    /// </summary>
    private void CutTabs()
    {
        var tabs = ElementCompositionPreview.GetElementVisual(ProfileViewport);
        var media = MediaCard.Visibility == Visibility.Visible && _mediaGrow is not null;
        var meeting = MeetingCard.Visibility == Visibility.Visible && _meetingGrow is not null;
        if (tabs.Clip is not InsetClip cut)
        {
            if (!media && !meeting) return;
            cut = tabs.Compositor.CreateInsetClip();
            tabs.Clip = cut;
        }
        // The Profiles' viewport keeps its clip (it cuts off the other Profiles' sidebars): only the cut changes.
        cut.StopAnimation("BottomInset");
        if (!media && !meeting)
        {
            cut.BottomInset = 0;
            return;
        }
        var parts = new List<string>();
        var animation = tabs.Compositor.CreateExpressionAnimation();
        if (media)
        {
            parts.Add("Max(p.T, 0) * pg");
            animation.SetReferenceParameter("p", _mediaGrow!);
            animation.SetScalarParameter("pg", _mediaGrowth);
        }
        if (meeting)
        {
            parts.Add("Max(q.T, 0) * qg");
            animation.SetReferenceParameter("q", _meetingGrow!);
            animation.SetScalarParameter("qg", _meetingGrowth);
        }
        animation.Expression = string.Join(" + ", parts);
        cut.StartAnimation("BottomInset", animation);
    }

    /// <summary>The mini player growing into its card pushes the meeting up by as much, so neither covers the other.</summary>
    private void BindMeetingPush()
    {
        if (MeetingCard.Visibility != Visibility.Visible) return;
        ElementCompositionPreview.SetIsTranslationEnabled(MeetingCard.Mover, true);
        var visual = ElementCompositionPreview.GetElementVisual(MeetingCard.Mover);
        visual.StopAnimation("Translation");
        if (MediaCard.Visibility != Visibility.Visible || _mediaGrow is null)
        {
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
            return;
        }
        var push = visual.Compositor.CreateExpressionAnimation("Vector3(0, -Max(p.T, 0) * pg, 0)");
        push.SetReferenceParameter("p", _mediaGrow);
        push.SetScalarParameter("pg", _mediaGrowth);
        visual.StartAnimation("Translation", push);
    }
}
