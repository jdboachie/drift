using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Drift;

/// <summary>A card's message (CardMessage.xaml): shown with a message, its button only with an action too.</summary>
public sealed partial class CardMessage : UserControl
{
    public CardMessage() => InitializeComponent();

    /// <summary><paramref name="message"/>, or none (the card has something to show); <paramref name="action"/> labels the button.</summary>
    public void Show(string? message, string? action)
    {
        MessageText.Text = message ?? "";
        Visibility = Ui.Shown(message is not null);
        ActionButton.Content = action;
        ActionButton.Visibility = Ui.Shown(message is not null && action is not null);
    }

    /// <summary>Its button, pressed.</summary>
    public event RoutedEventHandler? ActionClick;

    private void Action_Click(object sender, RoutedEventArgs e) => ActionClick?.Invoke(this, e);
}
