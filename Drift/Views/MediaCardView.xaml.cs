namespace Drift;

/// <summary>The mini player (MediaCardView.xaml); MediaControls.cs runs it.</summary>
public sealed partial class MediaCardView : Microsoft.UI.Xaml.Controls.UserControl
{
    public MediaCardView() => InitializeComponent();

    /// <summary>The window it's in, whose code handles what's done in it (set before it loads).</summary>
    internal MainWindow Owner { get; set; } = null!;
}
