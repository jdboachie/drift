
namespace Drift;

/// <summary>The meeting reminder (MeetingNoticeView.xaml); GoogleCalendar.cs fills and shows it.</summary>
public sealed partial class MeetingNoticeView : Microsoft.UI.Xaml.Controls.UserControl
{
    public MeetingNoticeView() => InitializeComponent();

    /// <summary>The window it's in, whose code handles what's done in it (set before it loads).</summary>
    internal MainWindow Owner { get; set; } = null!;
}
