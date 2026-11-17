using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Drift;

/// <summary>
/// Right-click menus built in code, as Arc's are: their commands by name, no icons, in groups with a
/// line between. Picking one closes the whole menu, submenus included.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>An empty one, for a menu built in code. A window of its own, so it can reach past Drift's.</summary>
    private static MenuFlyout NewMenu() => new() { ShouldConstrainToRootBounds = false };

    private static MenuFlyoutItem Command(string text, Action click, bool enabled = true)
    {
        var item = new MenuFlyoutItem { Text = text, IsEnabled = enabled };
        item.Click += (_, _) => click();
        return item;
    }

    /// <summary>A line that opens more beside it; one with nothing in it can't be opened.</summary>
    private static MenuFlyoutSubItem Submenu(string text, IEnumerable<MenuFlyoutItemBase> items)
    {
        var sub = new MenuFlyoutSubItem { Text = text };
        foreach (var item in items) sub.Items.Add(item);
        sub.IsEnabled = sub.Items.Count > 0;
        return sub;
    }

    /// <summary>The groups, in order, with a line between each; empty groups (and nulls) left out.</summary>
    private static void FillMenu(MenuFlyout menu, params IEnumerable<MenuFlyoutItemBase?>[] groups)
    {
        menu.Items.Clear();
        foreach (var group in groups.Select(g => g.OfType<MenuFlyoutItemBase>().ToList()).Where(g => g.Count > 0))
        {
            if (menu.Items.Count > 0) menu.Items.Add(new MenuFlyoutSeparator());
            foreach (var item in group) menu.Items.Add(item);
        }
    }
}
