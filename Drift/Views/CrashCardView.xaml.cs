
namespace Drift;

/// <summary>The card over a page whose process died or hung (CrashCardView.xaml).</summary>
public sealed partial class CrashCardView : Microsoft.UI.Xaml.Controls.UserControl
{
    public CrashCardView() => InitializeComponent();

    /// <summary>The window it's in, whose code handles what's done in it (set before it loads).</summary>
    internal MainWindow Owner { get; set; } = null!;
}
