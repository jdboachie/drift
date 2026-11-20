using System;
using System.Numerics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Drift;

/// <summary>A toast's button: what it says, what it does, and whether the toast goes when it's pressed.</summary>
public sealed record ToastAction(string Label, Action Act, bool Closes = true);

/// <summary>
/// A toast (Toast.xaml): it rises in, stays for a while (longer while the pointer's on it, if it has buttons), and
/// leaves the way it came. Its padding follows what it holds: a line alone, a line with a button (6px round the
/// button, so its 4px corners sit evenly inside the toast's 10px), or a notice.
/// </summary>
public sealed partial class Toast : UserControl
{
    // How far it travels in and out: up from below, or down from above (FromAbove).
    private const float Rise = 8, Drop = 6;

    private ToastAction[] _actions = [];
    private DispatcherQueueTimer? _timer;
    private bool _shown;

    public Toast() => InitializeComponent();

    /// <summary>Under the window buttons: it comes down from above, rather than up from below.</summary>
    public bool FromAbove { get; set; }

    /// <summary>Kept up while the pointer's on it, if it has buttons (so you can reach them).</summary>
    public bool HoldWhilePointerOver { get; set; } = true;

    /// <summary>Asked when its time's up: true keeps it (Bookmarked, while its editor's open).</summary>
    public Func<bool>? KeepOpen { get; set; }

    /// <summary>Its time ran out and it went (not a button, and not <see cref="Hide"/>).</summary>
    public event Action? Expired;

    public bool IsShown => _shown;

    /// <summary>
    /// Shows <paramref name="text"/> for <paramref name="time"/>, with its buttons, the first in the accent colour.
    /// A notice (<paramref name="notice"/>) has <paramref name="icon"/>, a bolder title over
    /// <paramref name="detail"/>, and full-size buttons; a line can lead with a <paramref name="glyph"/>.
    /// </summary>
    public void Show(string text, TimeSpan time, ToastAction[]? actions = null, string? glyph = null, bool accentGlyph = false,
        bool notice = false, FrameworkElement? icon = null, string? detail = null)
    {
        _actions = actions ?? [];
        TitleText.Text = text;
        TitleText.FontWeight = notice ? FontWeights.SemiBold : FontWeights.Normal;
        DetailText.Text = detail ?? "";
        DetailText.Visibility = detail is null ? Visibility.Collapsed : Visibility.Visible;
        IconPresenter.Content = icon;
        IconPresenter.Visibility = icon is null ? Visibility.Collapsed : Visibility.Visible;
        PlainGlyph.Visibility = AccentGlyph.Visibility = Visibility.Collapsed;
        if (glyph is not null)
        {
            var shown = accentGlyph ? AccentGlyph : PlainGlyph;
            shown.Glyph = glyph;
            shown.Visibility = Visibility.Visible;
        }

        Button[] buttons = [Action1, Action2];
        for (var i = 0; i < buttons.Length; i++)
        {
            var button = buttons[i];
            button.Visibility = i < _actions.Length ? Visibility.Visible : Visibility.Collapsed;
            if (i < _actions.Length) button.Content = _actions[i].Label;
            if (notice) button.ClearValue(Control.PaddingProperty);
            else button.Padding = i == 0 ? new Thickness(12, 3, 12, 4) : new Thickness(8, 3, 8, 3);
        }
        Action2.Style = (Style)Application.Current.Resources[notice ? "DefaultButtonStyle" : "SubtleButtonStyle"];

        var hasButtons = _actions.Length > 0;
        Surface.Padding = notice ? new Thickness(14, 10, 10, 10) : hasButtons ? new Thickness(14, 6, 6, 6) : new Thickness(14, 8, 14, 8);
        Surface.CornerRadius = new CornerRadius(notice ? 12 : 10);
        // Room between what it says and what you can do about it.
        Texts.Margin = new Thickness(0, 0, hasButtons ? (notice ? 12 : 10) : 0, 0);

        _shown = true;
        Visibility = Visibility.Visible;
        Surface.Opacity = 1;
        Surface.IsHitTestVisible = hasButtons;
        Spring.To(Surface, "Translation", new Vector3(0, 0, 32), 0.25, 0, from: Away);

        if (_timer is null)
        {
            _timer = DispatcherQueue.CreateTimer();
            _timer.IsRepeating = false;
            _timer.Tick += (_, _) => TimeUp();
        }
        _timer.Interval = time;
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>Gone, out the way it came in, without its buttons doing anything.</summary>
    public void Hide()
    {
        _timer?.Stop();
        if (!_shown) return;
        _shown = false;
        _actions = [];
        Surface.Opacity = 0;
        Surface.IsHitTestVisible = false;
        Spring.To(Surface, "Translation", Away, 0.25, 0);
    }

    /// <summary>Its time starts again, if it's up (Bookmarked, once its editor closes).</summary>
    public void RestartTimer()
    {
        if (!_shown) return;
        _timer?.Stop();
        _timer?.Start();
    }

    /// <summary>
    /// Its window is closing: its time stops for good. A toast whose time ran out after its window closed (the
    /// process can take a while to end) touched a closed window's element, and WinUI ended the process for it.
    /// </summary>
    public void Stop()
    {
        _stopped = true;
        _timer?.Stop();
    }

    private bool _stopped;

    private Vector3 Away => new(0, FromAbove ? -Drop : Rise, 32);

    private void TimeUp()
    {
        if (_stopped || KeepOpen?.Invoke() == true) return;
        Hide();
        Expired?.Invoke();
    }

    private void Action_Click(object sender, RoutedEventArgs e)
    {
        var index = sender == Action2 ? 1 : 0;
        if (index >= _actions.Length) return;
        var action = _actions[index];
        if (action.Closes) Hide();
        action.Act();
    }

    private void Surface_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (HoldWhilePointerOver) _timer?.Stop();
    }

    private void Surface_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (HoldWhilePointerOver && _shown && _actions.Length > 0) _timer?.Start();
    }
}
