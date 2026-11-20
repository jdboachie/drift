using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Drift;

/// <summary>The library (LibraryView.xaml); Library.cs, Bookmarks.cs and Downloads run it.</summary>
public sealed partial class LibraryView : Microsoft.UI.Xaml.Controls.UserControl
{
    public LibraryView() => InitializeComponent();

    /// <summary>The window it's in, whose code handles what's done in it (set before it loads).</summary>
    internal MainWindow Owner { get; set; } = null!;

    // Its lists' rows, handled by the window.
    private void DownloadCancel_Click(object sender, RoutedEventArgs e) => Owner.DownloadCancel_Click(sender, e);
    private void DownloadFolder_Click(object sender, RoutedEventArgs e) => Owner.DownloadFolder_Click(sender, e);
    private void DownloadPause_Click(object sender, RoutedEventArgs e) => Owner.DownloadPause_Click(sender, e);
    private void DownloadRemove_Click(object sender, RoutedEventArgs e) => Owner.DownloadRemove_Click(sender, e);
    private void DownloadRow_ContextRequested(UIElement sender, ContextRequestedEventArgs e) => Owner.DownloadRow_ContextRequested(sender, e);
}
