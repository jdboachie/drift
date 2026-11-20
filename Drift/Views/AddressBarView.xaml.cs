
namespace Drift;

/// <summary>The address pill and site info (AddressBarView.xaml).</summary>
public sealed partial class AddressBarView : Microsoft.UI.Xaml.Controls.UserControl
{
    public AddressBarView() => InitializeComponent();

    /// <summary>The window it's in, whose code handles what's done in it (set before it loads).</summary>
    internal MainWindow Owner { get; set; } = null!;
}
