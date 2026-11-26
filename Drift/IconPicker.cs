using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Drift.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Drift;

/// <summary>
/// Change icon…, for a tab or a folder (TabMenu.cs, Folders.cs), as in Arc: an emoji, or one of the
/// symbols Drift's own icons are drawn in, which take its colours (light or dark). A tab's chosen icon
/// stands in for its site's wherever that shows, so it's drawn into a picture (DrawIconAsync).
/// </summary>
public sealed partial class MainWindow
{
    // Arc's picker, measured on Windows: seven across, icons about 24 points on a 32-point pitch, with
    // a search field and a bin above.
    private const int PickerColumns = 7;
    private const double PickerCell = 32;
    private static readonly FontFamily EmojiFont = new("Segoe UI Emoji");

    private static List<(string Group, string Icon, string Words)>? _pickerEmoji;
    private static Dictionary<string, string>? _pickerNames;

    /// <summary>The emoji and the words they're found by (Assets/emoji.txt), read when the picker first opens.</summary>
    private static List<(string Group, string Icon, string Words)> PickerEmoji()
    {
        if (_pickerEmoji is not null) return _pickerEmoji;
        try
        {
            _pickerEmoji = PickerIcons.ParseEmoji(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "emoji.txt")));
        }
        catch (Exception error)
        {
            App.LogCrash(error);
            _pickerEmoji = [];
        }
        return _pickerEmoji;
    }

    /// <summary>What each icon is called, for screen readers.</summary>
    private static string PickerName(string icon)
    {
        _pickerNames ??= PickerIcons.Symbols.Select(s => (s.Icon, s.Words))
            .Concat(PickerEmoji().Select(e => (e.Icon, e.Words)))
            .GroupBy(p => p.Icon)
            .ToDictionary(g => g.Key, g => g.First().Words);
        return _pickerNames.TryGetValue(icon, out var name) ? name : icon;
    }

    /// <summary>
    /// What the picker needs, got ready a little after Drift starts, so it opens at once the first time
    /// too: the emoji list and their names read, and the icon and emoji fonts loaded (drawn once, out of
    /// sight).
    /// </summary>
    private void WarmIconPickerSoon()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(2);
        timer.IsRepeating = false;
        timer.Tick += async (_, _) =>
        {
            try
            {
                await Task.Run(() =>
                {
                    PickerEmoji();
                    PickerName("");
                    PickerRows("");
                });
                var first = PickerIcons.Symbols.First().Icon;
                var probe = new StackPanel
                {
                    Opacity = 0.01,
                    IsHitTestVisible = false,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Children =
                    {
                        new FontIcon { Glyph = first, FontFamily = Ui.IconFont(first), FontSize = 20 },
                        new TextBlock { Text = PickerEmoji().FirstOrDefault().Icon ?? "\U0001F600", FontFamily = EmojiFont, FontSize = 20 },
                    },
                };
                Root.Children.Add(probe);
                await Task.Delay(1000);
                Root.Children.Remove(probe);
            }
            catch (Exception error)
            {
                // Not ready ahead, it's made as it opens, as before.
                App.LogCrash(error);
            }
        };
        timer.Start();
    }

    /// <summary>A line of the picker: a group's name, or up to seven icons.</summary>
    private sealed record PickerRow(string? Heading, IReadOnlyList<string> Icons);

    /// <summary>Drift's icons, then the emoji by group; or, searching, everything that matches.</summary>
    private static List<PickerRow> PickerRows(string query)
    {
        var rows = new List<PickerRow>();
        void Group(string? heading, IEnumerable<string> icons)
        {
            var all = icons.ToList();
            if (all.Count == 0) return;
            if (heading is not null) rows.Add(new PickerRow(heading, []));
            for (var i = 0; i < all.Count; i += PickerColumns) rows.Add(new PickerRow(null, all.Skip(i).Take(PickerColumns).ToList()));
        }
        if (query.Trim().Length > 0)
        {
            Group(null, PickerIcons.Symbols.Where(s => PickerIcons.Matches(s.Words, query)).Select(s => s.Icon)
                .Concat(PickerEmoji().Where(e => PickerIcons.Matches(e.Words, query)).Select(e => e.Icon)));
            return rows;
        }
        Group("Icons", PickerIcons.Symbols.Select(s => s.Icon));
        foreach (var group in PickerEmoji().GroupBy(e => e.Group)) Group(group.Key, group.Select(e => e.Icon));
        return rows;
    }

    /// <summary>Makes the picker's lines as they scroll into view (an ItemsRepeater's template, in code).</summary>
    private sealed class PickerRowFactory(Func<PickerRow, UIElement> make) : IElementFactory
    {
        public UIElement GetElement(ElementFactoryGetArgs args) => make((PickerRow)args.Data);

        public void RecycleElement(ElementFactoryRecycleArgs args)
        {
        }
    }

    /// <summary>
    /// The picker, beside <paramref name="anchor"/>, after Arc's: a search field and a bin, then one
    /// scrolling grid, Drift's icons (Ionicons, as Arc's are) and then every emoji, the one in use on
    /// a soft fill. Only the lines in view are made, so all the emoji are there without a wait.
    /// What's picked is passed on, or null for "no icon of its own".
    /// </summary>
    /// <param name="removeText">What the bin does: "Use site icon", "Use tab group icon".</param>
    private void ShowIconPicker(FrameworkElement anchor, string? current, string removeText, Action<string?> picked)
    {
        // Snug around the grid; the usual flyout has room for paragraphs.
        var presenter = new Style(typeof(FlyoutPresenter));
        presenter.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10)));
        // Its own style drops the flyout's rounded corners: back, as the menus have them.
        presenter.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(8)));
        presenter.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 0d));
        presenter.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 0d));
        // A window of its own, as menus are: drawn in Drift's, it'd be under the page beside the sidebar.
        var flyout = new Flyout
        {
            Placement = FlyoutPlacementMode.RightEdgeAlignedTop,
            ShouldConstrainToRootBounds = false,
            FlyoutPresenterStyle = presenter,
        };
        var iconButton = (Style)Application.Current.Resources["IconButtonStyle"];

        void Pick(string? icon)
        {
            flyout.Hide();
            picked(icon);
        }

        UIElement Cell(string icon)
        {
            var cell = new Button
            {
                Style = iconButton,
                Width = PickerCell,
                Height = PickerCell,
                CornerRadius = new CornerRadius(6),
                Content = CustomIcon.IsSymbol(icon)
                    ? new FontIcon { Glyph = icon, FontFamily = Ui.IconFont(icon), FontSize = 20 }
                    : new TextBlock { Text = icon, FontSize = 20, FontFamily = EmojiFont },
            };
            if (icon == current) cell.Background = ThemeBrush("SubtleFillColorSecondaryBrush");
            AutomationProperties.SetName(cell, PickerName(icon));
            cell.Click += (_, _) => Pick(icon);
            return cell;
        }

        UIElement Row(PickerRow row)
        {
            if (row.Heading is { } heading)
                return new TextBlock
                {
                    Text = heading,
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = ThemeBrush("DriftSecondaryTextBrush"),
                    Margin = new Thickness(6, 10, 0, 4),
                };
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var icon in row.Icons) line.Children.Add(Cell(icon));
            return line;
        }

        var rows = PickerRows("");
        // Arrow keys move around the grid; Tab goes past it in one step.
        var repeater = new ItemsRepeater
        {
            Layout = new StackLayout(),
            ItemTemplate = new PickerRowFactory(Row),
            ItemsSource = rows,
            XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Enabled,
            TabFocusNavigation = KeyboardNavigationMode.Once,
            HorizontalAlignment = HorizontalAlignment.Left,
            // Only the lines in view. An emoji takes a while to draw the first time (30ms or so), and
            // lines made ahead, out of sight, held the picker up for seconds as it opened. It opens on
            // Drift's own icons, so no emoji is drawn until it's scrolled to or searched for.
            VerticalCacheLength = 0,
        };
        var scroll = new ScrollViewer
        {
            Content = repeater,
            Width = PickerColumns * PickerCell + 8,
            Height = 8 * PickerCell,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var nothing = new TextBlock
        {
            Text = "No matches",
            Foreground = ThemeBrush("DriftSecondaryTextBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 24, 0, 0),
            Visibility = Visibility.Collapsed,
        };

        var search = new TextBox { PlaceholderText = "Search", VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(search, "Search icons and emoji");
        search.TextChanged += (_, _) =>
        {
            rows = PickerRows(search.Text);
            repeater.ItemsSource = rows;
            nothing.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            scroll.ChangeView(null, 0, null, disableAnimation: true);
        };
        // Enter takes the first match.
        search.KeyDown += (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            if (rows.FirstOrDefault(r => r.Icons.Count > 0)?.Icons[0] is { } first)
            {
                e.Handled = true;
                Pick(first);
            }
        };

        var remove = new Button
        {
            Style = iconButton,
            Width = PickerCell,
            Height = PickerCell,
            Content = new FontIcon { Glyph = "", FontSize = 16 },
            IsEnabled = current is not null,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(remove, removeText);
        AutomationProperties.SetName(remove, removeText);
        remove.Click += (_, _) => Pick(null);

        var header = new Grid { ColumnSpacing = 6 };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(search);
        Grid.SetColumn(remove, 1);
        header.Children.Add(remove);

        var grid = new Grid();
        grid.Children.Add(scroll);
        grid.Children.Add(nothing);
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(header);
        panel.Children.Add(grid);
        flyout.Content = panel;
        flyout.Opened += SidebarMenu_Opened;
        flyout.Closed += SidebarMenu_Closed;
        // Type to search straight away. It opens at the top, as Arc's does, not scrolled to the one in use: scrolled to
        // an emoji, it drew a screenful of them at once, and an emoji takes 20-100ms to draw the first time (measured
        // 2026-11-25: 3.5-6s opening on the Home Profile's house; at the top, about 120ms).
        flyout.Opened += (_, _) => search.Focus(FocusState.Programmatic);
        flyout.ShowAt(anchor);
    }

    /// <summary>A tab's own icon (null: its site's again), drawn and kept with the session.</summary>
    private async Task SetTabIconAsync(BrowserTab tab, string? icon)
    {
        if (icon is null)
        {
            tab.SetIcon(null, null);
            SaveSessionSoon();
            return;
        }
        if (await DrawIconAsync(icon) is { } image)
        {
            tab.SetIcon(icon, image);
            SaveSessionSoon();
        }
    }

    // Where icons are put to be drawn: in the window (a picture can only be taken of what's in it), unseen.
    private Grid? _iconEasel;
    private bool _redrawsOnTheme;
    private double _drawnAtScale;

    /// <summary>The size a tab's icon is shown at in its row, in points.</summary>
    private const double IconSize = 16;

    private void RedrawTabIcons(bool symbolsOnly)
    {
        foreach (var tab in AllTabs.Where(t => t.Icon is { } i && (!symbolsOnly || CustomIcon.IsSymbol(i))).ToList())
            _ = SetTabIconAsync(tab, tab.Icon);
    }

    /// <summary>An emoji or symbol as a picture, for where a site's icon goes; null if it couldn't be drawn.</summary>
    private async Task<BitmapImage?> DrawIconAsync(string icon)
    {
        if (Root.XamlRoot is null) return null;
        if (_iconEasel is null)
        {
            _iconEasel = new Grid
            {
                Width = 64,
                Height = 64,
                Opacity = 0,
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            };
            Root.Children.Insert(0, _iconEasel);
        }
        if (!_redrawsOnTheme)
        {
            // A symbol is drawn in the theme's colour: drawn again when that changes. And each is drawn
            // for the screen's scaling: again when the window moves to a screen scaled differently.
            _redrawsOnTheme = true;
            Root.ActualThemeChanged += (_, _) => RedrawTabIcons(symbolsOnly: true);
            _drawnAtScale = Root.XamlRoot.RasterizationScale;
            Root.XamlRoot.Changed += (root, _) =>
            {
                if (root.RasterizationScale == _drawnAtScale) return;
                _drawnAtScale = root.RasterizationScale;
                RedrawTabIcons(symbolsOnly: false);
            };
        }
        // Drawn at the size it's shown in the tab rows, pixel for pixel, as text is: a bigger picture
        // made smaller comes out soft.
        // Filling the box as a site's icon does.
        FrameworkElement face = CustomIcon.IsSymbol(icon)
            ? new FontIcon { Glyph = icon, FontFamily = Ui.IconFont(icon), FontSize = 16, Foreground = ThemeBrush("DriftIconBrush") }
            : new TextBlock
            {
                Text = icon,
                FontSize = 15,
                FontFamily = new FontFamily("Segoe UI Emoji"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                // An emoji's room is 1.37 times its size, the picture centred in it: given only the box,
                // it'd sit at the left and lose its right edge. Room either side lets it centre.
                Margin = new Thickness(-4),
            };
        var cell = new Grid { Width = IconSize, Height = IconSize, Children = { face } };
        _iconEasel.Children.Add(cell);
        try
        {
            cell.UpdateLayout();
            var shot = new RenderTargetBitmap();
            // At the screen's own scale (no size given): 16 points is 24 pixels at 150%.
            await shot.RenderAsync(cell);
            var pixels = await shot.GetPixelsAsync();
            if (shot.PixelWidth == 0 || shot.PixelHeight == 0) return null;
            using var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                (uint)shot.PixelWidth, (uint)shot.PixelHeight, 96, 96, pixels.ToArray());
            await encoder.FlushAsync();
            stream.Seek(0);
            var image = new BitmapImage();
            await image.SetSourceAsync(stream);
            return image;
        }
        catch (Exception error)
        {
            App.LogCrash(error);
            return null;
        }
        finally
        {
            _iconEasel.Children.Remove(cell);
        }
    }
}
