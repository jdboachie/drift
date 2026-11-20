using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Drift;

/// <summary>
/// A page's ring and handle (PageMarkerView.xaml). <see cref="Label"/> names the handle until there's a page to name
/// it after; <see cref="Click"/> comes from the handle itself (its sender is the button).
/// </summary>
public sealed partial class PageMarkerView : UserControl
{
    public PageMarkerView() => InitializeComponent();

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(PageMarkerView),
        new PropertyMetadata(null, (d, e) => AutomationProperties.SetName(((PageMarkerView)d).Button, (string?)e.NewValue ?? "")));

    public string? Label { get => (string?)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    /// <summary>The handle, clicked.</summary>
    public event RoutedEventHandler? Click;

    private void Button_Click(object sender, RoutedEventArgs e) => Click?.Invoke(sender, e);
}
