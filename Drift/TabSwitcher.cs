using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Drift.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace Drift;

/// <summary>One tab in the Ctrl+Tab switcher: its picture, icon and title, and whether it's the one picked.</summary>
public sealed class SwitcherItem(BrowserTab tab) : INotifyPropertyChanged
{
    public BrowserTab Tab { get; } = tab;

    private BitmapImage? _picture;
    public BitmapImage? Picture
    {
        get => _picture;
        set { _picture = value; Changed(); Changed(nameof(PictureVisibility)); Changed(nameof(IconTileVisibility)); }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; Changed(); Changed(nameof(SelectedVisibility)); Changed(nameof(PlainVisibility)); }
    }

    public Visibility PictureVisibility => _picture is null ? Visibility.Collapsed : Visibility.Visible;
    // No picture yet (a tab not loaded, or not seen since Drift started): its icon, on a tile.
    public Visibility IconTileVisibility => _picture is null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SelectedVisibility => _isSelected ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PlainVisibility => _isSelected ? Visibility.Collapsed : Visibility.Visible;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Ctrl+Tab, as in Arc: like Alt+Tab, for tabs. A quick press goes back to the tab you were on last;
/// holding Ctrl brings up a bar of your tabs (most recently used first, TabCycle), each further Tab
/// moves along it, and letting go of Ctrl goes to the one picked. Esc puts it away. It appears and
/// goes without animating, since it's used all day. Each tab's picture is taken while it's in front
/// (a moment after it's shown or loads), so the bar has them at once. Tabs with no page loaded
/// aren't in it. Settings › Tabs can have
/// Ctrl+Tab go down the sidebar instead.
/// </summary>
public sealed partial class MainWindow
{
    // How long Ctrl is held before the bar shows: a quick press just switches.
    private const int SwitcherDelayMs = 150;
    private const int PictureWidth = 320;

    public ObservableCollection<SwitcherItem> SwitcherItems { get; } = new();

    private bool _cycling;
    private int _cycleAt;
    private long _cycleStarted;
    private bool _switcherShown;
    private DispatcherQueueTimer? _cyclePoll;
    private DispatcherQueueTimer? _pictureTimer;
    private readonly ConditionalWeakTable<BrowserTab, BitmapImage> _tabPictures = new();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    private const int VkMenu = 0x12;

    private static bool IsKeyDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    /// <summary>Whether the keys held with Tab for the shortcut (Ctrl, as set) are still down.</summary>
    private static bool HoldingSwitcherKeys()
    {
        var keys = Settings.ShortcutFor(Settings.Actions.First(a => a.Id == "nexttab"));
        if (!keys.Ctrl && !keys.Alt) return false;
        return (!keys.Ctrl || IsKeyDown(VkControl)) && (!keys.Alt || IsKeyDown(VkMenu));
    }

    /// <summary>Ctrl+Tab (step 1) and Ctrl+Shift+Tab (step −1).</summary>
    private void CycleTabs(int step)
    {
        if (!Settings.Current.CtrlTabRecentOrder)
        {
            SwitchTab(step);
            return;
        }
        if (_cycling)
        {
            _cycleAt = TabCycle.Step(_cycleAt, step, SwitcherItems.Count);
            // A second press means you're looking for one: no waiting for the bar.
            ShowSwitcher();
            return;
        }

        var order = TabCycle.Order(_recentTabs, AllTabs.ToList(), _active, t => !t.IsUnloaded);
        // Just the one: the bar still comes up, so you can see Ctrl+Tab worked.
        if (order.Count == 0) return;
        _cycling = true;
        SwitcherItems.Clear();
        foreach (var tab in order)
            SwitcherItems.Add(new SwitcherItem(tab) { Picture = _tabPictures.TryGetValue(tab, out var picture) ? picture : null });
        _cycleAt = TabCycle.Start(step, order.Count);
        _cycleStarted = Environment.TickCount64;
        // The tab you're on, as it is now.
        if (_active is { } current) _ = TakePictureAsync(current);

        // Already let go (the press reached Drift after the keys came up), or no key to hold: just switch.
        if (!HoldingSwitcherKeys())
        {
            FinishCycle(go: true);
            return;
        }
        if (_cyclePoll is null)
        {
            _cyclePoll = DispatcherQueue.CreateTimer();
            _cyclePoll.Interval = TimeSpan.FromMilliseconds(15);
            _cyclePoll.Tick += (_, _) => Safely(PollCycle);
        }
        _cyclePoll.Start();
    }

    /// <summary>The page has the keyboard, so Drift doesn't hear Ctrl come up: it looks.</summary>
    private void PollCycle()
    {
        if (!_cycling) return;
        if (!HoldingSwitcherKeys()) FinishCycle(go: true);
        else if (!_switcherShown && Environment.TickCount64 - _cycleStarted >= SwitcherDelayMs) ShowSwitcher();
    }

    private void ShowSwitcher()
    {
        for (var i = 0; i < SwitcherItems.Count; i++) SwitcherItems[i].IsSelected = i == _cycleAt;
        if (!_switcherShown)
        {
            _switcherShown = true;
            SwitcherCard.MaxWidth = Math.Max(200, Root.ActualWidth - 48);
            SwitcherLayer.Visibility = Visibility.Visible;
            SwitcherLayer.UpdateLayout();
        }
        if (SwitcherList.ContainerFromIndex(_cycleAt) is UIElement picked)
            picked.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, HorizontalAlignmentRatio = 0.5 });
    }

    /// <summary>Let go: to the tab picked (go), or nowhere (Esc).</summary>
    private void FinishCycle(bool go)
    {
        if (!_cycling) return;
        var tab = _cycleAt < SwitcherItems.Count ? SwitcherItems[_cycleAt].Tab : null;
        _cycling = false;
        _cyclePoll?.Stop();
        _switcherShown = false;
        SwitcherLayer.Visibility = Visibility.Collapsed;
        SwitcherItems.Clear();
        if (go && tab is not null && tab != _active && AllTabs.Contains(tab)) ShowTab(tab);
    }

    /// <summary>Esc while the bar's up: it goes, and you stay where you are.</summary>
    private bool CancelCycle()
    {
        if (!_cycling) return false;
        FinishCycle(go: false);
        return true;
    }

    private void SwitcherItem_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not SwitcherItem item) return;
        _cycleAt = SwitcherItems.IndexOf(item);
        FinishCycle(go: true);
        e.Handled = true;
    }

    // ── Pictures ──

    /// <summary>The tab in front, a moment after it's shown or has loaded: its picture for the bar.</summary>
    private void PictureActiveSoon()
    {
        if (_pictureTimer is null)
        {
            _pictureTimer = DispatcherQueue.CreateTimer();
            _pictureTimer.IsRepeating = false;
            _pictureTimer.Interval = TimeSpan.FromMilliseconds(1000);
            _pictureTimer.Tick += (_, _) => { if (_active is { } tab) _ = TakePictureAsync(tab); };
        }
        _pictureTimer.Stop();
        _pictureTimer.Start();
    }

    private void WirePicture(BrowserTab tab, CoreWebView2 core) =>
        core.NavigationCompleted += (_, _) => { if (tab == _active && tab.View.CoreWebView2 == core) PictureActiveSoon(); };

    /// <summary>The page as it's drawn now, small. Through DevTools, which doesn't blank the page.</summary>
    private async Task TakePictureAsync(BrowserTab tab)
    {
        if (tab.IsUnloaded || tab.View.CoreWebView2 is not { } core || tab.View.Visibility != Visibility.Visible) return;
        try
        {
            var reply = await core.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", """{"format":"jpeg","quality":70}""");
            var bytes = Convert.FromBase64String(JsonDocument.Parse(reply).RootElement.GetProperty("data").GetString()!);
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            using (var writer = new Windows.Storage.Streams.DataWriter(stream))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            var picture = new BitmapImage { DecodePixelWidth = PictureWidth };
            await picture.SetSourceAsync(stream);
            _tabPictures.AddOrUpdate(tab, picture);
            foreach (var item in SwitcherItems.Where(i => i.Tab == tab)) item.Picture = picture;
        }
        catch
        {
            // The page went away, or isn't drawn: it keeps the picture it had.
        }
    }
}
