using Drift.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Drift;

/// <summary>A Profile in the sidebar's switcher (ProfileIconView.xaml).</summary>
public sealed partial class ProfileIconView : UserControl
{
    public static readonly DependencyProperty ProfileProperty = DependencyProperty.Register(
        nameof(Profile), typeof(Profile), typeof(ProfileIconView), new PropertyMetadata(null));

    public ProfileIconView() => InitializeComponent();

    /// <summary>The Profile it's the icon of.</summary>
    public Profile? Profile
    {
        get => (Profile?)GetValue(ProfileProperty);
        set => SetValue(ProfileProperty, value);
    }

    /// <summary>The Profile you're on full, the others faded (ProfileOrder.IconStrength).</summary>
    public static double Strength(bool current) => ProfileOrder.IconStrength(current);

    /// <summary>Its symbol: one chosen for it, while there's room for icons.</summary>
    public static Visibility ShowsGlyph(string? icon, bool dot) => Ui.Shown(icon is not null && CustomIcon.IsSymbol(icon) && !dot);
    public static Visibility ShowsEmoji(bool emoji, bool dot) => Ui.Shown(emoji && !dot);
    /// <summary>A dot: no icon of its own, or no room for icons.</summary>
    public static Visibility ShowsDot(string? icon, bool dot) => Ui.Shown(icon is null || dot);

    // What's done in it, handled by the window it's in.
    private void Profile_Click(object sender, RoutedEventArgs e) => MainWindow.Of(this).ProfileIcon_Click(sender, e);
    private void ProfileMenu_Opening(object? sender, object e) => MainWindow.Of(this).ProfileMenu_Opening(sender, e);
    private void SidebarMenu_Closed(object? sender, object e) => MainWindow.Of(this).SidebarMenu_Closed(sender, e);
    private void SidebarMenu_Opened(object? sender, object e) => MainWindow.Of(this).SidebarMenu_Opened(sender, e);
}
