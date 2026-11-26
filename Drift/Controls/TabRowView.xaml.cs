using Drift.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Drift;

/// <summary>A tab's row in the sidebar's lists (TabRowView.xaml).</summary>
public sealed partial class TabRowView : UserControl
{
    public static readonly DependencyProperty TabProperty = DependencyProperty.Register(
        nameof(Tab), typeof(BrowserTab), typeof(TabRowView), new PropertyMetadata(null));

    public TabRowView() => InitializeComponent();

    /// <summary>The tab it's the row of.</summary>
    public BrowserTab? Tab
    {
        get => (BrowserTab?)GetValue(TabProperty);
        set => SetValue(TabProperty, value);
    }

    // What's done in it, handled by the window it's in.
    private void CloseSplitHalf_Click(object sender, RoutedEventArgs e) => MainWindow.Of(this).CloseSplitHalf_Click(sender, e);
    private void RowAction_Click(object sender, RoutedEventArgs e) => MainWindow.Of(this).RowAction_Click(sender, e);
    private void SidebarMenu_Closed(object? sender, object e) => MainWindow.Of(this).SidebarMenu_Closed(sender, e);
    private void SidebarMenu_Opened(object? sender, object e) => MainWindow.Of(this).SidebarMenu_Opened(sender, e);
    private void SplitCard_Loaded(object sender, RoutedEventArgs e) => MainWindow.Of(this).SplitCard_Loaded(sender, e);
    private void SplitHalf_Loaded(object sender, RoutedEventArgs e) => MainWindow.Of(this).SplitHalf_Loaded(sender, e);
    private void SplitHalf_PointerEntered(object sender, PointerRoutedEventArgs e) => MainWindow.Of(this).SplitHalf_PointerEntered(sender, e);
    private void SplitHalf_PointerExited(object sender, PointerRoutedEventArgs e) => MainWindow.Of(this).SplitHalf_PointerExited(sender, e);
    private void SplitHalf_Tapped(object sender, TappedRoutedEventArgs e) => MainWindow.Of(this).SplitHalf_Tapped(sender, e);
    private void SplitRow_SizeChanged(object sender, SizeChangedEventArgs e) => MainWindow.Of(this).SplitRow_SizeChanged(sender, e);
    private void TabAudio_Click(object sender, RoutedEventArgs e) => MainWindow.Of(this).TabAudio_Click(sender, e);
    private void TabMenu_Opening(object? sender, object e) => MainWindow.Of(this).TabMenu_Opening(sender, e);
    private void TabName_KeyDown(object sender, KeyRoutedEventArgs e) => MainWindow.Of(this).TabName_KeyDown(sender, e);
    private void TabName_LostFocus(object sender, RoutedEventArgs e) => MainWindow.Of(this).TabName_LostFocus(sender, e);
    private void TabRow_PointerEntered(object sender, PointerRoutedEventArgs e) => MainWindow.Of(this).TabRow_PointerEntered(sender, e);
    private void TabRow_PointerExited(object sender, PointerRoutedEventArgs e) => MainWindow.Of(this).TabRow_PointerExited(sender, e);
    private void TabRow_Tapped(object sender, TappedRoutedEventArgs e) => MainWindow.Of(this).TabRow_Tapped(sender, e);
}
