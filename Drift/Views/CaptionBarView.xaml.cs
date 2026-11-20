
namespace Drift;

/// <summary>The window buttons (CaptionBarView.xaml); CaptionBar.cs shows and hides them.</summary>
public sealed partial class CaptionBarView : Microsoft.UI.Xaml.Controls.UserControl
{
    public CaptionBarView() => InitializeComponent();

    /// <summary>The window it's in, whose code handles what's done in it (set before it loads).</summary>
    internal MainWindow Owner { get; set; } = null!;
}
