using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace Drift;

/// <summary>
/// One choice of a setting shown as pictures (PictureChoice.xaml): <see cref="Label"/> above, <see cref="Picture"/>
/// (what's written inside the tag) in a frame, a radio button under it. Choices with the same
/// <see cref="GroupName"/> go together, as radio buttons do (arrow keys move between them).
/// </summary>
[ContentProperty(Name = nameof(Picture))]
public sealed partial class PictureChoice : UserControl
{
    public PictureChoice()
    {
        InitializeComponent();
    }

    /// <summary>Chosen, by a click or the keyboard (not when <see cref="IsChecked"/> is set from code).</summary>
    public event EventHandler? Picked;

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(PictureChoice),
        new PropertyMetadata(null, (d, e) =>
        {
            var choice = (PictureChoice)d;
            choice.LabelText.Text = (string?)e.NewValue ?? "";
            AutomationProperties.SetName(choice.Radio, (string?)e.NewValue ?? "");
        }));

    public static readonly DependencyProperty PictureProperty = DependencyProperty.Register(
        nameof(Picture), typeof(UIElement), typeof(PictureChoice),
        new PropertyMetadata(null, (d, e) => ((PictureChoice)d).PicturePresenter.Content = e.NewValue));

    public static readonly DependencyProperty GroupNameProperty = DependencyProperty.Register(
        nameof(GroupName), typeof(string), typeof(PictureChoice),
        new PropertyMetadata(null, (d, e) => ((PictureChoice)d).Radio.GroupName = (string?)e.NewValue ?? ""));

    public string? Label
    {
        get => (string?)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public UIElement? Picture
    {
        get => (UIElement?)GetValue(PictureProperty);
        set => SetValue(PictureProperty, value);
    }

    public string? GroupName
    {
        get => (string?)GetValue(GroupNameProperty);
        set => SetValue(GroupNameProperty, value);
    }

    private bool _settingFromCode;

    public bool IsChecked
    {
        get => Radio.IsChecked == true;
        set
        {
            _settingFromCode = true;
            Radio.IsChecked = value;
            _settingFromCode = false;
            ShowFrame();
        }
    }

    private bool _hovered;

    private void Choice_Tapped(object sender, TappedRoutedEventArgs e) => Radio.IsChecked = true;

    private void Choice_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _hovered = true;
        ShowFrame();
    }

    private void Choice_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _hovered = false;
        ShowFrame();
    }

    private void Radio_Checked(object sender, RoutedEventArgs e)
    {
        ShowFrame();
        if (!_settingFromCode) Picked?.Invoke(this, EventArgs.Empty);
    }

    private void Radio_Unchecked(object sender, RoutedEventArgs e) => ShowFrame();

    /// <summary>Dark around the chosen one; a little darker than at rest under the pointer.</summary>
    private void ShowFrame() =>
        Frame.BorderBrush = (Brush)Application.Current.Resources[
            IsChecked ? "TextFillColorPrimaryBrush"
            : _hovered ? "ControlStrongStrokeColorDefaultBrush"
            : "ControlStrongStrokeColorDisabledBrush"];
}
