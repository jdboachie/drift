namespace Drift;

/// <summary>Create a Profile (ProfileCreationView.xaml); Profiles.cs shows it and makes the Profile.</summary>
public sealed partial class ProfileCreationView : Microsoft.UI.Xaml.Controls.UserControl
{
    public ProfileCreationView() => InitializeComponent();

    /// <summary>The window it's in, whose code handles what's done in it (set before it loads).</summary>
    internal MainWindow Owner { get; set; } = null!;
}
