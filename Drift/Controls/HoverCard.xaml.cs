using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Drift;

/// <summary>An integration's hover card (HoverCard.xaml): what's written inside the tag (Body), with + over its corner.</summary>
[ContentProperty(Name = nameof(Body))]
public sealed partial class HoverCard : UserControl
{
    public static readonly DependencyProperty ActionNameProperty =
        DependencyProperty.Register(nameof(ActionName), typeof(string), typeof(HoverCard), new PropertyMetadata(""));

    public static readonly DependencyProperty BodyProperty =
        DependencyProperty.Register(nameof(Body), typeof(object), typeof(HoverCard), new PropertyMetadata(null));

    public HoverCard() => InitializeComponent();

    /// <summary>What + does, for its tooltip and screen readers: "New email", "New event".</summary>
    public string ActionName
    {
        get => (string)GetValue(ActionNameProperty);
        set => SetValue(ActionNameProperty, value);
    }

    public object? Body
    {
        get => GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    /// <summary>+, pressed.</summary>
    public event RoutedEventHandler? ActionClick;

    private void Action_Click(object sender, RoutedEventArgs e) => ActionClick?.Invoke(this, e);
}
