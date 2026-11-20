using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Drift;

/// <summary>
/// A page's card (PageCardView.xaml): <see cref="Body"/>, what's written inside the tag, sits in its face. The card
/// itself is what moves (a split's springs, a drag), shadow and all.
/// </summary>
[ContentProperty(Name = nameof(Body))]
public sealed partial class PageCardView : UserControl
{
    public PageCardView() => InitializeComponent();

    public static readonly DependencyProperty BodyProperty = DependencyProperty.Register(
        nameof(Body), typeof(UIElement), typeof(PageCardView),
        new PropertyMetadata(null, (d, e) => ((PageCardView)d).Face.Child = (UIElement?)e.NewValue));

    public UIElement? Body { get => (UIElement?)GetValue(BodyProperty); set => SetValue(BodyProperty, value); }

    public static readonly DependencyProperty LiftProperty = DependencyProperty.Register(
        nameof(Lift), typeof(double), typeof(PageCardView),
        new PropertyMetadata(10.0, (d, e) => ((PageCardView)d).Caster.Translation = new System.Numerics.Vector3(0, 0, (float)(double)e.NewValue)));

    /// <summary>How high the card floats, so how far its shadow falls: 10 for a page in the window, more over a page.</summary>
    public double Lift { get => (double)GetValue(LiftProperty); set => SetValue(LiftProperty, value); }
}
