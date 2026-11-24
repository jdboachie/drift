using System.Collections.Generic;
using System.Linq;
using Drift.Models;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Drift;

/// <summary>
/// Several tabs picked out in the sidebar, to put in a folder together (TabPicks, in Core, says which): Ctrl+click
/// adds a tab or takes it out, Shift+click picks every tab from the last one clicked to this one. Picked rows show
/// as the tab in front does. A plain click, or Esc, lets them go. Right-click one of them for "Add 3 tabs to new
/// folder", or to a folder there already is (TabMenu.cs). Rows only: not Essentials' tiles, a split's row, or a
/// live folder's tabs, which come from GitHub.
/// </summary>
public sealed partial class MainWindow
{
    private readonly List<BrowserTab> _picked = new();

    /// <summary>Where the next Shift+click picks from: the last tab Ctrl+clicked, else the tab in front.</summary>
    private BrowserTab? _pickAnchor;

    /// <summary>The tabs that can be picked, in the sidebar's order: the folders' rows, then pinned tabs, then the rest.</summary>
    private List<BrowserTab> PickableTabs() =>
        FolderRows.OfType<BrowserTab>().Where(t => t.Folder is not { IsLive: true })
            .Concat(PinnedTabs).Concat(Tabs)
            .Where(t => !t.IsSplitRow && t.Split is null)
            .ToList();

    private static bool IsKeyDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    /// <summary>
    /// A row clicked (its list's selection changed): with Ctrl or Shift held, it's picked (or taken out) rather
    /// than opened, and the tab in front stays marked; true then. Any other click lets the picks go.
    /// </summary>
    /// <param name="list">The list it's in.</param>
    /// <param name="e">Ctrl+click on the row already marked takes the list's selection off it: that's the row clicked.</param>
    private bool PickInstead(ListViewBase list, SelectionChangedEventArgs e)
    {
        var clicked = list.SelectedItem as BrowserTab
            ?? (list.SelectedItem is null ? e.RemovedItems.OfType<BrowserTab>().FirstOrDefault() : null);
        var ctrl = IsKeyDown(VirtualKey.Control);
        var shift = IsKeyDown(VirtualKey.Shift);
        var order = PickableTabs();
        if ((!ctrl && !shift) || clicked is null || !order.Contains(clicked))
        {
            ClearPicks();
            return false;
        }
        var front = _active is { } active && order.Contains(active) ? active : null;
        List<BrowserTab> now;
        if (shift)
        {
            _pickAnchor ??= front ?? clicked;
            now = TabPicks.Range(order, _pickAnchor, clicked);
        }
        else
        {
            now = TabPicks.Toggle(_picked, clicked, front);
            _pickAnchor = clicked;
        }
        SetPicks(now);
        MarkActiveRow();
        return true;
    }

    private void SetPicks(List<BrowserTab> now)
    {
        foreach (var tab in _picked) tab.IsPicked = false;
        _picked.Clear();
        _picked.AddRange(now);
        foreach (var tab in _picked) tab.IsPicked = true;
    }

    private void ClearPicks()
    {
        _pickAnchor = null;
        if (_picked.Count > 0) SetPicks([]);
    }

    /// <summary>A tab closed, or gone some other way, is no longer picked.</summary>
    private void Unpick(BrowserTab tab)
    {
        if (!_picked.Remove(tab)) return;
        tab.IsPicked = false;
        if (_pickAnchor == tab) _pickAnchor = null;
    }

    /// <summary>The tab in front's row marked again in every list, after a click that only picked.</summary>
    private void MarkActiveRow()
    {
        if (_active is not { } active) return;
        MarkInSidebar(active);
        _syncingSelection = true;
        FolderList.SelectedItem = active is { Place: TabPlace.Folder, Split: null } && FolderRows.Contains(active) ? active : null;
        _syncingSelection = false;
    }

    /// <summary>A plain click on a row lets the picks go, the row in front too (whose selection doesn't change).</summary>
    internal void TabRow_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_picked.Count > 0 && !IsKeyDown(VirtualKey.Control) && !IsKeyDown(VirtualKey.Shift)) ClearPicks();
    }
}
