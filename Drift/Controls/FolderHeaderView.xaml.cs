using Drift.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Drift;

/// <summary>A folder's row in the sidebar (FolderHeaderView.xaml).</summary>
public sealed partial class FolderHeaderView : UserControl
{
    public static readonly DependencyProperty FolderProperty = DependencyProperty.Register(
        nameof(Folder), typeof(TabFolder), typeof(FolderHeaderView), new PropertyMetadata(null));

    public FolderHeaderView() => InitializeComponent();

    /// <summary>The folder it's the row of.</summary>
    public TabFolder? Folder
    {
        get => (TabFolder?)GetValue(FolderProperty);
        set => SetValue(FolderProperty, value);
    }

    // What's done in it, handled by the window it's in.
    private void FolderDelete_Click(object sender, RoutedEventArgs e) => MainWindow.Of(this).FolderDelete_Click(sender, e);
    private void FolderHeader_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => MainWindow.Of(this).FolderHeader_DoubleTapped(sender, e);
    private void FolderHeader_PointerEntered(object sender, PointerRoutedEventArgs e) => MainWindow.Of(this).FolderHeader_PointerEntered(sender, e);
    private void FolderHeader_PointerExited(object sender, PointerRoutedEventArgs e) => MainWindow.Of(this).FolderHeader_PointerExited(sender, e);
    private void FolderHeader_SizeChanged(object sender, SizeChangedEventArgs e) => MainWindow.Of(this).FolderHeader_SizeChanged(sender, e);
    private void FolderMenu_Opening(object? sender, object e) => MainWindow.Of(this).FolderMenu_Opening(sender, e);
    private void FolderName_KeyDown(object sender, KeyRoutedEventArgs e) => MainWindow.Of(this).FolderName_KeyDown(sender, e);
    private void FolderName_LostFocus(object sender, RoutedEventArgs e) => MainWindow.Of(this).FolderName_LostFocus(sender, e);
    private void FolderSpinner_Loaded(object sender, RoutedEventArgs e) => MainWindow.Of(this).FolderSpinner_Loaded(sender, e);
    private void SidebarMenu_Closed(object? sender, object e) => MainWindow.Of(this).SidebarMenu_Closed(sender, e);
    private void SidebarMenu_Opened(object? sender, object e) => MainWindow.Of(this).SidebarMenu_Opened(sender, e);
}
