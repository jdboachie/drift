using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Drift;

/// <summary>
/// A settings card (Styles/Settings.xaml's SettingsGroupStyle): its rows one under another, a hairline between each two that
/// show. The lines follow the rows, so a row hidden or added (in code, say) never leaves one doubled or dangling.
/// </summary>
public sealed partial class SettingsGroup : StackPanel
{
    public SettingsGroup() => Style = (Style)Application.Current.Resources["SettingsGroupStyle"];

    protected override Size MeasureOverride(Size availableSize)
    {
        var first = true;
        foreach (var child in Children)
        {
            if (child.Visibility != Visibility.Visible) continue;
            if (child is SettingsRow row) row.Divided = !first;
            first = false;
        }
        return base.MeasureOverride(availableSize);
    }
}
