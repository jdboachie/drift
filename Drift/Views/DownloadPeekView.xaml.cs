using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Drift;

/// <summary>The downloads peek (DownloadPeekView.xaml); DownloadsPeek.cs fills and shows it.</summary>
public sealed partial class DownloadPeekView : Microsoft.UI.Xaml.Controls.UserControl
{
    public DownloadPeekView() => InitializeComponent();

    /// <summary>The window it's in, whose code handles what's done in it (set before it loads).</summary>
    internal MainWindow Owner { get; set; } = null!;

    // Its lists' rows, handled by the window.
    private void DownloadRow_ContextRequested(UIElement sender, ContextRequestedEventArgs e) => Owner.DownloadRow_ContextRequested(sender, e);
}
