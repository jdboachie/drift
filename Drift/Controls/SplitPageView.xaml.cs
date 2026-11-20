using Drift.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Drift;

/// <summary>A page's place in a split's row (SplitPageView.xaml).</summary>
public sealed partial class SplitPageView : UserControl
{
    public static readonly DependencyProperty PageProperty = DependencyProperty.Register(
        nameof(Page), typeof(BrowserTab), typeof(SplitPageView),
        new PropertyMetadata(null, (d, _) => ((SplitPageView)d).Tag = ((SplitPageView)d).Page));

    public SplitPageView() => InitializeComponent();

    /// <summary>The page shown; also its <see cref="FrameworkElement.Tag"/>, which Split.cs reads.</summary>
    public BrowserTab? Page
    {
        get => (BrowserTab?)GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    /// <summary>Its ×, pressed (MainWindow.CloseSplitHalf_Click); the sender is this place.</summary>
    public event RoutedEventHandler? CloseClick;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseClick?.Invoke(this, e);
}
