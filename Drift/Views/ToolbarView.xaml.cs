
namespace Drift;

/// <summary>The sidebar's top row (ToolbarView.xaml): menu, sidebar toggle and navigation.</summary>
public sealed partial class ToolbarView : Microsoft.UI.Xaml.Controls.UserControl
{
    public ToolbarView() => InitializeComponent();

    /// <summary>The window it's in, whose code handles what's done in it (set before it loads).</summary>
    internal MainWindow Owner { get; set; } = null!;
}
