using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace Drift;

/// <summary>
/// A list over which a touchpad swipe has to reach the Library's swipe (the sidebar's, the Library's own), set up the
/// one way seen to pass it on. A ScrollViewer takes a touchpad's gesture as its own (Microsoft's docs: "These gestures
/// are handled internally by the ScrollViewer control"), and the swipe's tracker on the window gets the sideways part
/// only out of one that scrolls up and down and never sideways. Seen on the user's touchpad: the sidebar's list, set up
/// so, passed it on; with nothing to scroll up and down it stalled; and the Library's lists, left as they were, kept it,
/// so the Library didn't move till the fingers lifted (2026-11-25: "closing via swipe takes a while to register").
/// So these always scroll up and down; and with too little in them, a swipe up or down moves nothing, the content
/// drawn moved back by however far it's been pulled (2026-11-25: "the sidebar shouldn't offer vertical scrolling ...
/// if the items don't overflow").
/// </summary>
internal static class ScrollHold
{
    /// <summary>A ScrollViewer and what's in it.</summary>
    public static void Attach(ScrollViewer scroll, UIElement content)
    {
        scroll.VerticalScrollMode = ScrollMode.Enabled;
        scroll.HorizontalScrollMode = ScrollMode.Disabled;
        scroll.IsHorizontalScrollChainingEnabled = true;
        if (scroll.IsLoaded) Hold(scroll, content);
        else
        {
            void Once(object sender, RoutedEventArgs e)
            {
                scroll.Loaded -= Once;
                Hold(scroll, content);
            }
            scroll.Loaded += Once;
        }
    }

    /// <summary>A list control's own ScrollViewer and what's in it, once it's built.</summary>
    public static void AttachInside(FrameworkElement list)
    {
        void Once(object sender, RoutedEventArgs e)
        {
            if (Find(list) is not { Content: UIElement content } scroll) return;
            list.Loaded -= Once;
            Attach(scroll, content);
        }
        list.Loaded += Once;
    }

    private static void Hold(ScrollViewer scroll, UIElement content)
    {
        var pulled = ElementCompositionPreview.GetScrollViewerManipulationPropertySet(scroll);
        var held = pulled.Compositor.CreatePropertySet();
        var still = pulled.Compositor.CreateExpressionAnimation("Vector3(0, -p.Translation.Y * h.On, 0)");
        still.SetReferenceParameter("p", pulled);
        still.SetReferenceParameter("h", held);
        still.Target = "Translation";
        void Update() => held.InsertScalar("On", scroll.ScrollableHeight < 0.5 ? 1 : 0);
        Update();
        content.StartAnimation(still);
        scroll.SizeChanged += (_, _) => Update();
        if (content is FrameworkElement sized) sized.SizeChanged += (_, _) => Update();
    }

    private static ScrollViewer? Find(DependencyObject at)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(at); i++)
        {
            var child = VisualTreeHelper.GetChild(at, i);
            if (child is ScrollViewer found) return found;
            if (Find(child) is { } deeper) return deeper;
        }
        return null;
    }
}
