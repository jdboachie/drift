using Microsoft.UI.Xaml;

namespace Drift;

/// <summary>Small conversions for x:Bind, so the models (in Core) needn't know about XAML.</summary>
public static class Ui
{
    public static Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility Hidden(bool hidden) => hidden ? Visibility.Collapsed : Visibility.Visible;
}
