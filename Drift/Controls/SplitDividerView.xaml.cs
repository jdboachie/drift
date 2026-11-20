using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace Drift;

/// <summary>
/// The gap between two pages of a split (SplitDividerView.xaml), with its grip. The resize pointer is the control's
/// own, so it stays while the divider's held, wherever the pointer goes.
/// </summary>
public sealed partial class SplitDividerView : UserControl
{
    public SplitDividerView()
    {
        InitializeComponent();
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
    }
}
