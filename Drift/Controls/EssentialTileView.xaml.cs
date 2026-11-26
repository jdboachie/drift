using Drift.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Drift;

/// <summary>An Essential's tile in the sidebar (EssentialTileView.xaml).</summary>
public sealed partial class EssentialTileView : UserControl
{
    public static readonly DependencyProperty TabProperty = DependencyProperty.Register(
        nameof(Tab), typeof(BrowserTab), typeof(EssentialTileView), new PropertyMetadata(null));

    public EssentialTileView() => InitializeComponent();

    /// <summary>The tab it's the tile of.</summary>
    public BrowserTab? Tab
    {
        get => (BrowserTab?)GetValue(TabProperty);
        set => SetValue(TabProperty, value);
    }

    // What's done in it, handled by the window it's in.
    private void IntegrationAnchor_PointerEntered(object sender, PointerRoutedEventArgs e) => MainWindow.Of(this).IntegrationAnchor_PointerEntered(sender, e);
    private void IntegrationAnchor_PointerExited(object sender, PointerRoutedEventArgs e) => MainWindow.Of(this).IntegrationAnchor_PointerExited(sender, e);
    private void SidebarMenu_Closed(object? sender, object e) => MainWindow.Of(this).SidebarMenu_Closed(sender, e);
    private void SidebarMenu_Opened(object? sender, object e) => MainWindow.Of(this).SidebarMenu_Opened(sender, e);
    private void TabAudio_Click(object sender, RoutedEventArgs e) => MainWindow.Of(this).TabAudio_Click(sender, e);
    private void TabMenu_Opening(object? sender, object e) => MainWindow.Of(this).TabMenu_Opening(sender, e);
}
