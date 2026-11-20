using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Drift;

/// <summary>
/// A row of a settings card (SettingsRow.xaml): <see cref="Header"/>, a quieter <see cref="Description"/> under it
/// (gone when there's none), the setting's control on the right (<see cref="Trailing"/>, what's written inside the
/// tag), and anything that goes under the row (<see cref="Below"/>). A switch on the right gets the settings look
/// (no On/Off words beside it), and a switch or list with no name of its own is named by the row, for screen readers.
/// </summary>
[ContentProperty(Name = nameof(Trailing))]
public sealed partial class SettingsRow : UserControl
{
    public SettingsRow()
    {
        InitializeComponent();
        // A row set apart (the box of Clear browsing data's choices) pads itself its own way.
        RegisterPropertyChangedCallback(PaddingProperty, (_, _) => Root.Padding = Padding);
    }

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(SettingsRow),
        new PropertyMetadata(null, (d, e) => ((SettingsRow)d).HeaderChanged((string?)e.OldValue)));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(SettingsRow),
        new PropertyMetadata(null, (d, _) => ((SettingsRow)d).Show(((SettingsRow)d).DescriptionText, ((SettingsRow)d).Description)));

    public static readonly DependencyProperty TrailingProperty = DependencyProperty.Register(
        nameof(Trailing), typeof(UIElement), typeof(SettingsRow),
        new PropertyMetadata(null, (d, _) => ((SettingsRow)d).TrailingChanged()));

    public static readonly DependencyProperty BelowProperty = DependencyProperty.Register(
        nameof(Below), typeof(UIElement), typeof(SettingsRow),
        new PropertyMetadata(null, (d, _) => ((SettingsRow)d).BelowChanged()));

    public string? Header { get => (string?)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public string? Description { get => (string?)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public UIElement? Trailing { get => (UIElement?)GetValue(TrailingProperty); set => SetValue(TrailingProperty, value); }
    public UIElement? Below { get => (UIElement?)GetValue(BelowProperty); set => SetValue(BelowProperty, value); }

    /// <summary>The hairline above it: every row of a card but the first one showing (SettingsGroup sets it).</summary>
    internal bool Divided
    {
        get => Line.BorderThickness.Top > 0;
        set => Line.BorderThickness = new Thickness(0, value ? 1 : 0, 0, 0);
    }

    private void Show(TextBlock text, string? value)
    {
        text.Text = value ?? "";
        text.Visibility = Ui.Shown(!string.IsNullOrEmpty(value));
    }

    private void HeaderChanged(string? old)
    {
        Show(HeaderText, Header);
        foreach (var input in Inputs())
        {
            // Named by the row unless it has a name of its own.
            var name = AutomationProperties.GetName(input);
            if (string.IsNullOrEmpty(name) || name == old) AutomationProperties.SetName(input, Header ?? "");
        }
    }

    private void TrailingChanged()
    {
        TrailingPresenter.Content = Trailing;
        foreach (var input in Inputs())
        {
            if (input is ToggleSwitch toggle)
            {
                toggle.MinWidth = 0;
                toggle.OnContent = "";
                toggle.OffContent = "";
            }
            if (string.IsNullOrEmpty(AutomationProperties.GetName(input)) && Header is { Length: > 0 } header)
                AutomationProperties.SetName(input, header);
        }
    }

    private void BelowChanged()
    {
        BelowPresenter.Content = Below;
        BelowPresenter.Visibility = Ui.Shown(Below is not null);
        Root.RowSpacing = Below is not null && (Trailing is not null || !string.IsNullOrEmpty(Header)) ? 10 : 0;
    }

    /// <summary>The switches and lists on the right: the control itself, or the ones side by side there.</summary>
    private IEnumerable<FrameworkElement> Inputs()
    {
        IEnumerable<UIElement> all = Trailing switch
        {
            Panel panel => panel.Children,
            { } one => [one],
            null => [],
        };
        return all.OfType<FrameworkElement>().Where(e => e is ToggleSwitch or ComboBox);
    }
}
