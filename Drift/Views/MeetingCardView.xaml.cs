namespace Drift;

/// <summary>The next meeting at the foot of the sidebar (MeetingCardView.xaml); MeetingCard.cs fills, opens and shuts it.</summary>
public sealed partial class MeetingCardView : Microsoft.UI.Xaml.Controls.UserControl
{
    public MeetingCardView() => InitializeComponent();

    /// <summary>The window it's in, whose code handles what's done in it (set before it loads).</summary>
    internal MainWindow Owner { get; set; } = null!;
}
