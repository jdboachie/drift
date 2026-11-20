using Microsoft.UI.Xaml.Input;

namespace Drift;

/// <summary>The tab switcher (SwitcherView.xaml); TabSwitcher.cs runs it.</summary>
public sealed partial class SwitcherView : Microsoft.UI.Xaml.Controls.UserControl
{
    public SwitcherView() => InitializeComponent();

    /// <summary>The window it's in, whose code handles what's done in it (set before it loads).</summary>
    internal MainWindow Owner { get; set; } = null!;

    // Its lists' rows, handled by the window.
    private void SwitcherItem_PointerPressed(object sender, PointerRoutedEventArgs e) => Owner.SwitcherItem_PointerPressed(sender, e);
}
