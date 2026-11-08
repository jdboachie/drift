using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;
using Microsoft.Web.WebView2.Core;

namespace Drift;

/// <summary>
/// Drift's settings, in a window of their own (one at a time, centred over Drift): the default
/// browser and ad blocking, what happens to tabs when Drift closes and what's shown when it opens,
/// the keyboard shortcuts, and extensions. Every change applies and saves straight away.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private static SettingsWindow? _open;

    // Keys that already mean something everywhere text is edited.
    private static readonly HashSet<Shortcut> Reserved = new()
    {
        new(VirtualKey.C, true, false, false), new(VirtualKey.V, true, false, false),
        new(VirtualKey.X, true, false, false), new(VirtualKey.A, true, false, false),
        new(VirtualKey.Z, true, false, false), new(VirtualKey.Y, true, false, false),
        new(VirtualKey.F4, false, false, true),
    };

    private static readonly string ResetGlyph = ((char)0xE7A7).ToString();
    private static readonly string RemoveGlyph = ((char)0xE74D).ToString();

    private bool _loading;
    // The shortcut being recorded, and the row it's on.
    private ShortcutAction? _recording;
    private readonly Dictionary<string, (Button Button, TextBlock Message, Button Reset)> _rows = new();

    // The window that opened Settings: pages picked from History open there.
    private MainWindow? _owner;

    /// <param name="page">The page to show ("privacy", …); by default the one already showing.</param>
    public static void Open(Window owner, string? page = null)
    {
        if (_open is null)
        {
            _open = new SettingsWindow();
            _open.Place(owner);
        }
        _open._owner = owner as MainWindow;
        if (page is not null) _open.ShowPage(page);
        _open.Activate();
    }

    private void ShowPage(string page)
    {
        foreach (var item in Nav.Items.OfType<ListViewItem>())
            if ((string)item.Tag == page) Nav.SelectedItem = item;
    }

    public static void CloseIfOpen() => _open?.Close();

    private SettingsWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        // Acrylic, like Drift's own window.
        SystemBackdrop = new DesktopAcrylicBackdrop();
        AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;
        // Just a close button, like Dia's: it's a small window you visit, not one you arrange.
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
        }
        Closed += (_, _) =>
        {
            _open = null;
            Extensions.Changed -= Extensions_Changed;
        };
        // Back from Windows' Default apps page: show whether Drift is the default now.
        Activated += (_, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated) ShowDefaultBrowser();
        };
        Extensions.Changed += Extensions_Changed;
        Nav.SelectedIndex = 0;

        _loading = true;
        var settings = Settings.Current;
        CloseCombo.SelectedIndex = settings.OnClose == CloseBehavior.KeepTabs ? 0 : 1;
        StartupCombo.SelectedIndex = (int)settings.OnStartup;
        StartupPageBox.Text = settings.StartupPage;
        StartupPagePanel.Visibility = settings.OnStartup == StartupBehavior.Page ? Visibility.Visible : Visibility.Collapsed;
        AdBlockToggle.IsOn = settings.BlockAds;
        PipToggle.IsOn = settings.AutoPictureInPicture;
        RecentTabsToggle.IsOn = settings.CtrlTabRecentOrder;
        PasswordsToggle.IsOn = settings.SavePasswords;
        AutofillToggle.IsOn = settings.Autofill;
        ClearRangeCombo.SelectedIndex = 1;
        _loading = false;
        ShowDefaultBrowser();
        ShowAdBlockStatus();

        BuildShortcuts();
    }

    private void Place(Window owner)
    {
        var scale = owner.Content?.XamlRoot?.RasterizationScale ?? 1;
        AppWindow.Resize(new SizeInt32((int)(720 * scale), (int)(560 * scale)));
        var ownerPos = owner.AppWindow.Position;
        var ownerSize = owner.AppWindow.Size;
        var size = AppWindow.Size;
        AppWindow.Move(new PointInt32(
            ownerPos.X + (ownerSize.Width - size.Width) / 2,
            ownerPos.Y + (ownerSize.Height - size.Height) / 2));
    }

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        StopRecording();
        var page = (Nav.SelectedItem as ListViewItem)?.Tag as string ?? "general";
        GeneralPage.Visibility = page == "general" ? Visibility.Visible : Visibility.Collapsed;
        TabsPage.Visibility = page == "tabs" ? Visibility.Visible : Visibility.Collapsed;
        ShortcutsPage.Visibility = page == "shortcuts" ? Visibility.Visible : Visibility.Collapsed;
        ExtensionsPage.Visibility = page == "extensions" ? Visibility.Visible : Visibility.Collapsed;
        PrivacyPage.Visibility = page == "privacy" ? Visibility.Visible : Visibility.Collapsed;
        if (page == "extensions") _ = ShowExtensionsAsync();
        if (page == "privacy") _ = ShowPermissionsAsync();
    }

    // ───────────────────────── General ─────────────────────────

    private void ShowDefaultBrowser()
    {
        var isDefault = DefaultBrowser.IsDefault;
        DefaultBrowserText.Text = isDefault ? "Drift is your default browser" : "Open links from other apps in Drift";
        DefaultBrowserButton.Visibility = isDefault ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Registers Drift with Windows, then opens Default apps on Drift's page: Windows only lets you
    /// choose the default browser there.
    /// </summary>
    private void DefaultBrowser_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            DefaultBrowser.Register();
            DefaultBrowser.OpenSystemSettings();
            DefaultBrowserText.Text = "In Settings, choose Set default for Drift";
        }
        catch (Exception ex)
        {
            DefaultBrowserText.Text = "Couldn't register Drift: " + ex.Message;
        }
    }

    private void PipToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || PipToggle.IsOn == Settings.Current.AutoPictureInPicture) return;
        var on = PipToggle.IsOn;
        Settings.Update(s => s.AutoPictureInPicture = on);
    }

    private void RecentTabsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || RecentTabsToggle.IsOn == Settings.Current.CtrlTabRecentOrder) return;
        var on = RecentTabsToggle.IsOn;
        Settings.Update(s => s.CtrlTabRecentOrder = on);
    }

    private void AdBlockToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || AdBlockToggle.IsOn == Settings.Current.BlockAds) return;
        var on = AdBlockToggle.IsOn;
        Settings.Update(s => s.BlockAds = on);
        _ = Extensions.SyncAdBlockAsync();
    }

    private void ShowAdBlockStatus()
    {
        AdBlockText.Text = Extensions.AdBlockStatus
            ?? (Extensions.Profile is null && Settings.Current.BlockAds
                ? "With uBlock Origin Lite, once a page has opened"
                : "With uBlock Origin Lite");
    }

    private void Extensions_Changed()
    {
        ShowAdBlockStatus();
        if (ExtensionsPage.Visibility == Visibility.Visible) _ = ShowExtensionsAsync();
    }

    // ───────────────────────── Extensions ─────────────────────────

    private async System.Threading.Tasks.Task ShowExtensionsAsync()
    {
        ExtensionsError.Visibility = Visibility.Collapsed;
        IReadOnlyList<CoreWebView2BrowserExtension> extensions;
        try
        {
            extensions = await Extensions.ListAsync();
        }
        catch (Exception ex)
        {
            ShowExtensionsError(ex);
            return;
        }

        ExtensionList.Children.Clear();
        if (extensions.Count == 0)
        {
            var empty = new Grid { Style = (Style)Application.Current.Resources["SettingsRowStyle"] };
            empty.Children.Add(new TextBlock
            {
                Text = Extensions.Profile is null ? "Open a page in Drift first" : "No extensions yet",
                Style = (Style)Application.Current.Resources["SettingsDescriptionStyle"],
                VerticalAlignment = VerticalAlignment.Center,
            });
            ExtensionList.Children.Add(empty);
            return;
        }

        foreach (var extension in extensions.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            if (ExtensionList.Children.Count > 0)
                ExtensionList.Children.Add(new Border { Style = (Style)Application.Current.Resources["SettingsDividerStyle"] });
            ExtensionList.Children.Add(ExtensionRow(extension));
        }
    }

    private Grid ExtensionRow(CoreWebView2BrowserExtension extension)
    {
        var adBlock = Extensions.IsAdBlock(extension);
        var row = new Grid { Style = (Style)Application.Current.Resources["SettingsRowStyle"] };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = extension.Name, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock
        {
            Text = adBlock ? "Ad blocking (Settings › General)" : extension.Id,
            Style = (Style)Application.Current.Resources["SettingsDescriptionStyle"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        row.Children.Add(text);

        var toggle = new ToggleSwitch
        {
            IsOn = extension.IsEnabled,
            OnContent = "",
            OffContent = "",
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, $"Turn {extension.Name} on or off");
        toggle.Toggled += async (_, _) =>
        {
            try
            {
                if (adBlock)
                {
                    // The ad blocker follows its setting, so General and this switch agree.
                    Settings.Update(s => s.BlockAds = toggle.IsOn);
                    _loading = true;
                    AdBlockToggle.IsOn = toggle.IsOn;
                    _loading = false;
                    await Extensions.SyncAdBlockAsync();
                }
                else await extension.EnableAsync(toggle.IsOn);
            }
            catch (Exception ex)
            {
                ShowExtensionsError(ex);
            }
        };
        Grid.SetColumn(toggle, 1);
        row.Children.Add(toggle);

        if (!adBlock)
        {
            var remove = new Button
            {
                Content = new FontIcon { Glyph = RemoveGlyph, FontSize = 12 },
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTipService.SetToolTip(remove, "Remove");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(remove, $"Remove {extension.Name}");
            remove.Click += async (_, _) =>
            {
                try
                {
                    await extension.RemoveAsync();
                    await ShowExtensionsAsync();
                }
                catch (Exception ex)
                {
                    ShowExtensionsError(ex);
                }
            };
            Grid.SetColumn(remove, 2);
            row.Children.Add(remove);
        }
        return row;
    }

    private async void AddExtension_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            if (await picker.PickSingleFolderAsync() is not { } folder) return;
            await Extensions.AddAsync(folder.Path);
            await ShowExtensionsAsync();
        }
        catch (Exception ex)
        {
            ShowExtensionsError(ex);
        }
    }

    private void ShowExtensionsError(Exception ex)
    {
        ExtensionsError.Text = ex is System.IO.FileNotFoundException
            ? "That folder has no manifest.json. Pick the extension's own folder, unzipped."
            : "Couldn't do that: " + ex.Message;
        ExtensionsError.Visibility = Visibility.Visible;
    }

    // ───────────────────────── Privacy ─────────────────────────

    /// <summary>
    /// Clears what's ticked, from the chosen time on: Drift's own history and download list, and from
    /// the web profile cookies, site storage, cache and saved passwords.
    /// </summary>
    private async void ClearData_Click(object sender, RoutedEventArgs e)
    {
        var now = DateTime.Now;
        DateTime? since = ClearRangeCombo.SelectedIndex switch
        {
            0 => now.AddHours(-1),
            1 => now.AddDays(-1),
            2 => now.AddDays(-7),
            3 => now.AddDays(-28),
            _ => null,
        };
        var kinds = (CoreWebView2BrowsingDataKinds)0;
        if (ClearHistoryCheck.IsChecked == true) kinds |= CoreWebView2BrowsingDataKinds.BrowsingHistory;
        if (ClearCookiesCheck.IsChecked == true) kinds |= CoreWebView2BrowsingDataKinds.AllSite;
        if (ClearCacheCheck.IsChecked == true) kinds |= CoreWebView2BrowsingDataKinds.DiskCache | CoreWebView2BrowsingDataKinds.CacheStorage;
        if (ClearDownloadsCheck.IsChecked == true) kinds |= CoreWebView2BrowsingDataKinds.DownloadHistory;
        if (ClearPasswordsCheck.IsChecked == true) kinds |= CoreWebView2BrowsingDataKinds.PasswordAutosave | CoreWebView2BrowsingDataKinds.GeneralAutofill;
        if (kinds == 0)
        {
            ClearDataText.Text = "Tick something to clear";
            return;
        }

        ClearDataButton.IsEnabled = false;
        ClearDataText.Text = "Clearing…";
        try
        {
            if (ClearHistoryCheck.IsChecked == true) History.Clear(since?.ToUniversalTime());
            if (ClearDownloadsCheck.IsChecked == true) Downloads.ClearFinished(since);
            // The profile's own data needs a page to have started the engine.
            var profileKinds = kinds & ~(CoreWebView2BrowsingDataKinds.BrowsingHistory | CoreWebView2BrowsingDataKinds.DownloadHistory);
            if (Extensions.Profile is { } profile)
            {
                if (since is { } start) await profile.ClearBrowsingDataAsync(kinds, start, now);
                else await profile.ClearBrowsingDataAsync(kinds);
                ClearDataText.Text = "Cleared";
            }
            else ClearDataText.Text = profileKinds == 0 ? "Cleared" : "Cleared history. Open a page, then clear the rest.";
            // The library's history list, if it's showing.
            App.Main?.HistoryChanged();
            if (_owner is { } owner && owner != App.Main) owner.HistoryChanged();
            _ = ShowPermissionsAsync();
        }
        catch (Exception ex)
        {
            ClearDataText.Text = "Couldn't clear it: " + ex.Message;
        }
        finally
        {
            ClearDataButton.IsEnabled = true;
        }
    }

    private void PasswordsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || PasswordsToggle.IsOn == Settings.Current.SavePasswords) return;
        var on = PasswordsToggle.IsOn;
        Settings.Update(s => s.SavePasswords = on);
        Extensions.ApplyPreferences();
    }

    private void AutofillToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || AutofillToggle.IsOn == Settings.Current.Autofill) return;
        var on = AutofillToggle.IsOn;
        Settings.Update(s => s.Autofill = on);
        Extensions.ApplyPreferences();
    }

    /// <summary>Every site with a permission allowed or blocked, and a button to put it back to asking.</summary>
    private async System.Threading.Tasks.Task ShowPermissionsAsync()
    {
        IReadOnlyList<CoreWebView2PermissionSetting> settings = [];
        var profile = Extensions.Profile;
        try
        {
            if (profile is not null) settings = await profile.GetNonDefaultPermissionSettingsAsync();
        }
        catch { }

        PermissionList.Children.Clear();
        if (profile is null || settings.Count == 0)
        {
            var empty = new Grid { Style = (Style)Application.Current.Resources["SettingsRowStyle"] };
            empty.Children.Add(new TextBlock
            {
                Text = profile is null ? "Open a page in Drift first" : "No site has been allowed or blocked anything yet",
                Style = (Style)Application.Current.Resources["SettingsDescriptionStyle"],
                VerticalAlignment = VerticalAlignment.Center,
            });
            PermissionList.Children.Add(empty);
            return;
        }

        foreach (var site in settings.GroupBy(s => s.PermissionOrigin.TrimEnd('/')).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (PermissionList.Children.Count > 0)
                PermissionList.Children.Add(new Border { Style = (Style)Application.Current.Resources["SettingsDividerStyle"] });
            var row = new Grid { Style = (Style)Application.Current.Resources["SettingsRowStyle"] };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var host = Uri.TryCreate(site.Key, UriKind.Absolute, out var origin) ? origin.Host : site.Key;
            text.Children.Add(new TextBlock { Text = host, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", site.Select(s =>
                    $"{MainWindow.PermissionName(s.PermissionKind)}: {(s.PermissionState == CoreWebView2PermissionState.Allow ? "allowed" : "blocked")}")),
                Style = (Style)Application.Current.Resources["SettingsDescriptionStyle"],
                TextWrapping = TextWrapping.Wrap,
            });
            row.Children.Add(text);

            var reset = new Button
            {
                Content = new FontIcon { Glyph = ResetGlyph, FontSize = 12 },
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTipService.SetToolTip(reset, "Ask again");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(reset, $"Reset permissions for {host}");
            var entries = site.ToList();
            reset.Click += async (_, _) =>
            {
                try
                {
                    foreach (var s in entries)
                        await profile.SetPermissionStateAsync(s.PermissionKind, s.PermissionOrigin, CoreWebView2PermissionState.Default);
                }
                catch { }
                await ShowPermissionsAsync();
            };
            Grid.SetColumn(reset, 1);
            row.Children.Add(reset);
            PermissionList.Children.Add(row);
        }
    }


    // ───────────────────────── Tabs ─────────────────────────

    private void CloseCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var keep = CloseCombo.SelectedIndex == 0 ? CloseBehavior.KeepTabs : CloseBehavior.KeepPinned;
        Settings.Update(s => s.OnClose = keep);
    }

    private void StartupCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || StartupCombo.SelectedIndex < 0) return;
        var startup = (StartupBehavior)StartupCombo.SelectedIndex;
        StartupPagePanel.Visibility = startup == StartupBehavior.Page ? Visibility.Visible : Visibility.Collapsed;
        Settings.Update(s => s.OnStartup = startup);
        if (startup == StartupBehavior.Page && StartupPageBox.Text.Length == 0) StartupPageBox.Focus(FocusState.Programmatic);
    }

    private void StartupPageBox_LostFocus(object sender, RoutedEventArgs e) => CommitStartupPage();

    private void StartupPageBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        CommitStartupPage();
    }

    /// <summary>Saved as typed if it's an address; otherwise it says what's wrong and keeps the old one.</summary>
    private void CommitStartupPage()
    {
        var text = StartupPageBox.Text.Trim();
        var valid = text.Length == 0 || MainWindow.TryParseAddress(text, out _);
        StartupPageError.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
        if (valid && text != Settings.Current.StartupPage) Settings.Update(s => s.StartupPage = text);
    }

    // ───────────────────────── Shortcuts ─────────────────────────

    private void BuildShortcuts()
    {
        ShortcutList.Children.Clear();
        _rows.Clear();
        foreach (var action in Settings.Actions)
        {
            // One card for all of them, a hairline between rows.
            if (ShortcutList.Children.Count > 0)
                ShortcutList.Children.Add(new Border { Style = (Style)Application.Current.Resources["SettingsDividerStyle"] });

            var grid = new Grid { Style = (Style)Application.Current.Resources["SettingsRowStyle"] };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = action.Name });
            var message = new TextBlock
            {
                Style = (Style)Application.Current.Resources["SettingsDescriptionStyle"],
                Visibility = Visibility.Collapsed,
            };
            text.Children.Add(message);
            Grid.SetColumn(text, 0);
            grid.Children.Add(text);

            var button = new Button
            {
                MinWidth = 120,
                MinHeight = 32,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"{action.Name} shortcut");
            button.Click += (_, _) =>
            {
                if (_recording == action) StopRecording();
                else StartRecording(action);
            };
            button.PreviewKeyDown += Shortcut_PreviewKeyDown;
            // While recording, Space and Enter are keys to record, not clicks.
            button.PreviewKeyUp += (_, e) => { if (_recording is not null) e.Handled = true; };
            button.LostFocus += (_, _) => { if (_recording == action) StopRecording(); };
            Grid.SetColumn(button, 1);
            grid.Children.Add(button);

            var reset = new Button
            {
                Content = new FontIcon { Glyph = ResetGlyph, FontSize = 12 },
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTipService.SetToolTip(reset, $"Reset to {action.Default}");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(reset, $"Reset {action.Name} shortcut");
            reset.Click += (_, _) =>
            {
                StopRecording();
                Settings.SetShortcut(action, null);
                ShowShortcut(action);
            };
            Grid.SetColumn(reset, 2);
            grid.Children.Add(reset);

            ShortcutList.Children.Add(grid);
            _rows[action.Id] = (button, message, reset);
            ShowShortcut(action);
        }
    }

    /// <summary>The row at rest: its keys as keycaps, and the reset button if it's been changed.</summary>
    private void ShowShortcut(ShortcutAction action, string? message = null)
    {
        var (button, text, reset) = _rows[action.Id];
        var shortcut = Settings.ShortcutFor(action);
        button.Content = Keycaps(shortcut.Parts());
        reset.Visibility = shortcut == action.Default ? Visibility.Collapsed : Visibility.Visible;
        text.Text = message ?? "";
        text.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
        ResetAllButton.IsEnabled = Settings.Actions.Any(a => Settings.ShortcutFor(a) != a.Default);
    }

    private static StackPanel Keycaps(IEnumerable<string> keys)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var key in keys)
        {
            panel.Children.Add(new Border
            {
                MinWidth = 24,
                Padding = new Thickness(6, 1, 6, 2),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
                Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"],
                Child = new TextBlock { Text = key, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center },
            });
        }
        return panel;
    }

    private void StartRecording(ShortcutAction action)
    {
        StopRecording();
        _recording = action;
        var (button, text, _) = _rows[action.Id];
        button.Content = new TextBlock { Text = "Press keys…", FontSize = 12 };
        text.Visibility = Visibility.Collapsed;
        button.Focus(FocusState.Programmatic);
    }

    private void StopRecording(string? message = null)
    {
        if (_recording is not { } action) return;
        _recording = null;
        ShowShortcut(action, message);
    }

    private void Shortcut_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_recording is not { } action) return;
        e.Handled = true;
        var (button, text, _) = _rows[action.Id];

        if (e.Key == VirtualKey.Escape)
        {
            StopRecording();
            return;
        }

        static bool Down(VirtualKey key) =>
            InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
        var ctrl = Down(VirtualKey.Control);
        var shift = Down(VirtualKey.Shift);
        var alt = Down(VirtualKey.Menu);

        // Modifiers on their own: show them as they're held, and wait for the key.
        if (e.Key is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl
            or VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift
            or VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu
            or VirtualKey.LeftWindows or VirtualKey.RightWindows)
        {
            var held = new List<string>();
            if (ctrl) held.Add("Ctrl");
            if (alt) held.Add("Alt");
            if (shift) held.Add("Shift");
            button.Content = held.Count == 0 ? new TextBlock { Text = "Press keys…", FontSize = 12 } : Keycaps(held.Append("…"));
            return;
        }

        void Explain(string why)
        {
            text.Text = why;
            text.Visibility = Visibility.Visible;
        }

        if (Shortcut.KeyName(e.Key) is null)
        {
            Explain("Use a letter, number, function key, Tab or punctuation key");
            return;
        }
        var shortcut = new Shortcut(e.Key, ctrl, shift, alt);
        if (!ctrl && !alt && !shortcut.IsFunctionKey)
        {
            Explain("Add Ctrl or Alt");
            return;
        }
        if (Reserved.Contains(shortcut))
        {
            Explain($"{shortcut} is already used for editing and windows");
            return;
        }
        if (Settings.ActionFor(shortcut) is { } other && other != action)
        {
            StopRecording($"{shortcut} is already used for {other.Name.ToLowerInvariant()}");
            return;
        }

        Settings.SetShortcut(action, shortcut);
        StopRecording();
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        StopRecording();
        Settings.Update(s => s.Shortcuts.Clear());
        foreach (var action in Settings.Actions) ShowShortcut(action);
    }
}
