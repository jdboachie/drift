
namespace Drift;

/// <summary>The command bar (CommandBarView.xaml); CommandBar.cs and AskAiBar.cs run it.</summary>
public sealed partial class CommandBarView : Microsoft.UI.Xaml.Controls.UserControl
{
    public CommandBarView() => InitializeComponent();

    /// <summary>The window it's in, whose code handles what's done in it (set before it loads).</summary>
    internal MainWindow Owner { get; set; } = null!;
}
