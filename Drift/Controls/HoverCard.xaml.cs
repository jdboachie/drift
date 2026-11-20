using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace Drift;

/// <summary>An integration's hover card (HoverCard.xaml): a header, then what's written inside the tag (Body).</summary>
[ContentProperty(Name = nameof(Body))]
public sealed partial class HoverCard : UserControl
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(ImageSource), typeof(HoverCard), new PropertyMetadata(null));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(HoverCard), new PropertyMetadata(""));

    public static readonly DependencyProperty ActionNameProperty =
        DependencyProperty.Register(nameof(ActionName), typeof(string), typeof(HoverCard), new PropertyMetadata(""));

    public static readonly DependencyProperty BodyProperty =
        DependencyProperty.Register(nameof(Body), typeof(object), typeof(HoverCard), new PropertyMetadata(null));

    public HoverCard() => InitializeComponent();

    /// <summary>The site's icon (its tab's favicon).</summary>
    public ImageSource? Icon
    {
        get => (ImageSource?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>What it shows: "Inbox", "3 unread", "Friday, November 20".</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>What + does, for its tooltip and screen readers: "Compose", "New event".</summary>
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
