using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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
    private readonly Dictionary<string, (Button Button, SettingsRow Row, Button Reset)> _rows = new();

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
        foreach (var tab in Toolbar.Children.OfType<RadioButton>())
            if ((string)tab.Tag == page) tab.IsChecked = true;
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
            if (e.WindowActivationState == WindowActivationState.Deactivated) return;
            ShowDefaultBrowser();
            if (_aiRecheck && AiPage.Visibility == Visibility.Visible)
            {
                _aiRecheck = false;
                _ = ShowAiAsync();
            }
        };
        Extensions.Changed += Extensions_Changed;
        ShowPage("general");

        _loading = true;
        var settings = Settings.Current;
        CloseCombo.SelectedIndex = settings.OnClose == CloseBehavior.KeepTabs ? 0 : 1;
        StartupCombo.SelectedIndex = (int)settings.OnStartup;
        StartupPageBox.Text = settings.StartupPage;
        StartupPagePanel.Visibility = settings.OnStartup == StartupBehavior.Page ? Visibility.Visible : Visibility.Collapsed;
        AdBlockToggle.IsOn = settings.BlockAds;
        PipToggle.IsOn = settings.AutoPictureInPicture;
        CloseTwiceToggle.IsOn = settings.CloseTwice;
        LoadEssentialsToggle.IsOn = settings.LoadEssentialsAtStart;
        RecentTabsToggle.IsOn = settings.CtrlTabRecentOrder;
        SwitchToNewTabsToggle.IsOn = settings.SwitchToNewTabs;
        RemindersToggle.IsOn = settings.MeetingReminders;
        PasswordsToggle.IsOn = settings.SavePasswords;
        AutofillToggle.IsOn = settings.Autofill;
        SignInToggle.IsOn = settings.MicrosoftSignIn;
        AskTabToggle.IsOn = settings.AskAiWithTab;
        AskFirstCombo.SelectedIndex = settings.AskFirst == AiProvider.Claude ? 0 : 1;
        ClearRangeCombo.SelectedIndex = 1;
        var keep = DownloadArchive.Choices.ToList().IndexOf(settings.KeepDownloadsDays);
        KeepDownloadsCombo.SelectedIndex = keep >= 0 ? keep : DownloadArchive.Choices.ToList().IndexOf(DownloadArchive.DefaultDays);
        ShowDownloadFolder();
        _loading = false;
        ShowDefaultBrowser();
        ShowAdBlockStatus();
        ShowSignInRestart();
        // Loaded already, it's filled in here, before the window is shown.
        _ = ShowAccountAsync();

        BuildShortcuts();
        ShowPreviews();
    }

    private void Place(Window owner)
    {
        var scale = owner.Content?.XamlRoot?.RasterizationScale ?? 1;
        AppWindow.Resize(new SizeInt32((int)(720 * scale), (int)(640 * scale)));
        var ownerPos = owner.AppWindow.Position;
        var ownerSize = owner.AppWindow.Size;
        var size = AppWindow.Size;
        AppWindow.Move(new PointInt32(
            ownerPos.X + (ownerSize.Width - size.Width) / 2,
            ownerPos.Y + (ownerSize.Height - size.Height) / 2));
    }

    private void Page_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton tab || tab.Tag is not string page) return;
        StopRecording();
        PageTitle.Text = AutomationProperties.GetName(tab);
        if (page == "general" && PreviewList is not null) ShowPreviews();
        GeneralPage.Visibility = page == "general" ? Visibility.Visible : Visibility.Collapsed;
        AccountsPage.Visibility = page == "accounts" ? Visibility.Visible : Visibility.Collapsed;
        if (page == "accounts") _ = ShowAccountAsync();
        TabsPage.Visibility = page == "tabs" ? Visibility.Visible : Visibility.Collapsed;
        DownloadsPage.Visibility = page == "downloads" ? Visibility.Visible : Visibility.Collapsed;
        AiPage.Visibility = page == "ai" ? Visibility.Visible : Visibility.Collapsed;
        ShortcutsPage.Visibility = page == "shortcuts" ? Visibility.Visible : Visibility.Collapsed;
        ExtensionsPage.Visibility = page == "extensions" ? Visibility.Visible : Visibility.Collapsed;
        PrivacyPage.Visibility = page == "privacy" ? Visibility.Visible : Visibility.Collapsed;
        if (page == "extensions") _ = ShowExtensionsAsync();
        if (page == "privacy") _ = ShowPermissionsAsync();
        if (page == "ai") _ = ShowAiAsync();
    }

    // ───────────────────────── Downloads ─────────────────────────

    private void ShowDownloadFolder()
    {
        DownloadFolderText.Text = Downloads.Folder;
    }

    private async void DownloadFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        Windows.Storage.StorageFolder? folder;
        try { folder = await picker.PickSingleFolderAsync(); }
        catch { return; }
        if (folder is null) return;
        Settings.Update(s => s.DownloadFolder = folder.Path);
        ShowDownloadFolder();
    }

    private void KeepDownloadsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || KeepDownloadsCombo.SelectedIndex < 0) return;
        var days = DownloadArchive.Choices[KeepDownloadsCombo.SelectedIndex];
        if (days != Settings.Current.KeepDownloadsDays) Settings.Update(s => s.KeepDownloadsDays = days);
    }

    // ───────────────────────── General ─────────────────────────

    private void ShowDefaultBrowser()
    {
        var isDefault = DefaultBrowser.IsDefault;
        DefaultBrowserRow.Header = isDefault ? "Drift is your default browser" : "Open links from other apps in Drift";
        DefaultBrowserButton.Visibility = isDefault ? Visibility.Collapsed : Visibility.Visible;
        if (isDefault) DefaultBrowserRow.Description = null;
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
            ShowDefaultBrowserNote("Almost there: in Windows Settings, click Set default");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            ShowDefaultBrowserNote("We couldn't make Drift your default browser. Please try again.");
        }
    }

    private void ShowDefaultBrowserNote(string note) => DefaultBrowserRow.Description = note;

    private void CloseTwiceToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || CloseTwiceToggle.IsOn == Settings.Current.CloseTwice) return;
        var on = CloseTwiceToggle.IsOn;
        Settings.Update(s => s.CloseTwice = on);
    }

    private void SwitchToNewTabsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || SwitchToNewTabsToggle.IsOn == Settings.Current.SwitchToNewTabs) return;
        var on = SwitchToNewTabsToggle.IsOn;
        Settings.Update(s => s.SwitchToNewTabs = on);
    }

    private void LoadEssentialsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || LoadEssentialsToggle.IsOn == Settings.Current.LoadEssentialsAtStart) return;
        var on = LoadEssentialsToggle.IsOn;
        Settings.Update(s => s.LoadEssentialsAtStart = on);
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

    // ---- Accounts ----

    private bool _accountShown;

    /// <summary>
    /// The Windows account's name, email and picture, filled in as the window is made: Drift loads
    /// them when it starts (WindowsAccount.PreloadAsync), so they're there before the page shows,
    /// all at once. Only if Settings opens before that has finished do they arrive a moment later.
    /// </summary>
    private async System.Threading.Tasks.Task ShowAccountAsync()
    {
        if (_accountShown) return;
        _accountShown = true;
        if (WindowsAccount.Loaded is null) await WindowsAccount.PreloadAsync();
        if (WindowsAccount.Loaded is not { } account) return;
        AccountName.Text = account.Name ?? "";
        AccountPicture.DisplayName = account.Name ?? "";
        if (account.Email is { } email) AccountEmail.Text = email;
        if (WindowsAccount.Picture is { } picture) AccountPicture.ProfilePicture = picture;
    }

    private void SignInToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || SignInToggle.IsOn == Settings.Current.MicrosoftSignIn) return;
        var on = SignInToggle.IsOn;
        Settings.Update(s => s.MicrosoftSignIn = on);
        ShowSignInRestart();
    }

    /// <summary>A change that waits for the engine to restart says so, with a button to do it.</summary>
    private void ShowSignInRestart()
    {
        var pending = MainWindow.SignInRestartPending;
        SignInRow.Description = pending ? $"Turns {(Settings.Current.MicrosoftSignIn ? "on" : "off")} when Drift restarts" : null;
        SignInRestartButton.Visibility = pending && App.Main is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SignInRestart_Click(object sender, RoutedEventArgs e) => _ = App.Main?.RestartAsync(Content.XamlRoot);

    private void DifferentAccount_Click(object sender, RoutedEventArgs e) => App.Main?.UseDifferentMicrosoftAccount();

    private async void SignOut_Click(object sender, RoutedEventArgs e)
    {
        if (App.Main is not { } main) return;
        SignOutButton.IsEnabled = false;
        try
        {
            if (await main.SignOutOfMicrosoftAsync(Content.XamlRoot) is not { } result) return;
            SignOutText.Text = result;
            SignOutText.Visibility = Visibility.Visible;
            _loading = true;
            SignInToggle.IsOn = Settings.Current.MicrosoftSignIn;
            _loading = false;
            ShowSignInRestart();
        }
        finally
        {
            SignOutButton.IsEnabled = true;
        }
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
        AdBlockRow.Description = Extensions.AdBlockStatus
            ?? (Extensions.Profile is null && Settings.Current.BlockAds
                ? "Drift uses uBlock Origin Lite to block them. It starts once you open a page."
                : "Drift uses uBlock Origin Lite to block them");
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
            ExtensionList.Children.Add(new SettingsRow
            {
                Description = Extensions.Profile is null ? "Open a page, and your extensions show up here" : "No extensions yet",
            });
            return;
        }

        foreach (var extension in extensions.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
            ExtensionList.Children.Add(ExtensionRow(extension));
    }

    /// <summary>A row whose name is one line, cut short with an ellipsis (an extension, a site).</summary>
    private static SettingsRow OneLineRow(string header, string description)
    {
        var row = new SettingsRow { Header = header, Description = description };
        row.HeaderText.TextWrapping = TextWrapping.NoWrap;
        row.HeaderText.TextTrimming = TextTrimming.CharacterEllipsis;
        return row;
    }

    /// <summary>The control on the right of a row, and a 32-wide place after it for a small button (or nothing),
    /// so every row's control lines up, button or not.</summary>
    private static Grid WithSlot(FrameworkElement control, FrameworkElement? button)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.Children.Add(control);
        if (button is not null)
        {
            Grid.SetColumn(button, 1);
            grid.Children.Add(button);
        }
        return grid;
    }

    private SettingsRow ExtensionRow(CoreWebView2BrowserExtension extension)
    {
        var adBlock = Extensions.IsAdBlock(extension);
        var row = OneLineRow(extension.Name, adBlock ? "Ad blocking (Settings › General)" : extension.Id);
        row.DescriptionText.TextWrapping = TextWrapping.NoWrap;
        row.DescriptionText.TextTrimming = TextTrimming.CharacterEllipsis;

        var toggle = new ToggleSwitch { IsOn = extension.IsEnabled, VerticalAlignment = VerticalAlignment.Center };
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
        Button? remove = null;
        if (!adBlock)
        {
            remove = new Button
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
                if (!await ConfirmDialog.AskAsync(Content.XamlRoot, Warnings.RemoveExtension(extension.Name))) return;
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
        }
        row.Trailing = WithSlot(toggle, remove);
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
        System.Diagnostics.Debug.WriteLine(ex);
        ExtensionsError.Text = ex switch
        {
            System.IO.FileNotFoundException => "We couldn't find an extension in that folder. Choose the unzipped extension's own folder.",
            // Drift's own, already in words ("Open a page first, then add the extension.").
            InvalidOperationException => ex.Message,
            _ => "Something went wrong with that extension. Please try again.",
        };
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
            ClearDataText.Text = "Choose something to clear";
            return;
        }
        var ticked = new[] { ClearHistoryCheck, ClearCookiesCheck, ClearCacheCheck, ClearDownloadsCheck, ClearPasswordsCheck }
            .Where(c => c.IsChecked == true)
            .Select(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName)
            .ToList();
        var range = (ClearRangeCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "All time";
        if (Warnings.ClearData(ticked, range) is { } warning && !await ConfirmDialog.AskAsync(Content.XamlRoot, warning)) return;

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
            else ClearDataText.Text = profileKinds == 0 ? "Cleared" : "History cleared. Open a page, then clear again for the rest.";
            // The library's history list, if it's showing.
            App.Main?.HistoryChanged();
            if (_owner is { } owner && owner != App.Main) owner.HistoryChanged();
            _ = ShowPermissionsAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            ClearDataText.Text = "We couldn't clear that. Please try again.";
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
            PermissionList.Children.Add(new SettingsRow
            {
                Description = profile is null ? "Open a page, and these show up here" : "You haven't allowed or blocked anything for a site yet",
            });
            return;
        }

        foreach (var site in settings.GroupBy(s => s.PermissionOrigin.TrimEnd('/')).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var host = Uri.TryCreate(site.Key, UriKind.Absolute, out var origin) ? origin.Host : site.Key;
            var row = OneLineRow(host, string.Join(" · ", site.Select(s =>
                $"{MainWindow.PermissionName(s.PermissionKind)}: {(s.PermissionState == CoreWebView2PermissionState.Allow ? "allowed" : "blocked")}")));

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
            row.Trailing = reset;
            PermissionList.Children.Add(row);
        }
    }


    // ───────────────────────── Previews ─────────────────────────

    private static readonly (PreviewKind Kind, string App, string What)[] PreviewChoices =
    [
        (PreviewKind.Folders, "Folders", "The tabs inside"),
        (PreviewKind.Gmail, "Gmail", "Unread emails"),
        (PreviewKind.GoogleCalendar, "Google Calendar", "Today's events"),
        (PreviewKind.OutlookMail, "Outlook", "Unread emails"),
        (PreviewKind.OutlookCalendar, "Outlook Calendar", "Today's events"),
    ];

    /// <summary>
    /// The previews, each a row: its name, what it shows — or, when something needs you, what (signed out,
    /// couldn't reach it) — and its switch. Built afresh whenever General shows.
    /// </summary>
    private void ShowPreviews()
    {
        PreviewList.Children.Clear();
        void Row(string label, string? note, bool on, Action<bool> set)
        {
            var toggle = new ToggleSwitch { IsOn = on };
            toggle.Toggled += (_, _) => set(toggle.IsOn);
            PreviewList.Children.Add(new SettingsRow { Header = label, Description = note, Trailing = toggle });
        }
        foreach (var (kind, app, what) in PreviewChoices)
        {
            var on = Settings.Current.Shows(kind);
            Row(app, (on ? App.Main?.PreviewTrouble(kind) : null) ?? what, on, v => Settings.Update(s => s.Show(kind, v)));
        }
    }

    private void RemindersToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || RemindersToggle.IsOn == Settings.Current.MeetingReminders) return;
        var on = RemindersToggle.IsOn;
        Settings.Update(s => s.MeetingReminders = on);
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
            // One card for all of them, a hairline between rows; a message (a clash) under its name.
            var row = new SettingsRow { Header = action.Name };

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
            row.Trailing = WithSlot(button, reset);

            ShortcutList.Children.Add(row);
            _rows[action.Id] = (button, row, reset);
            ShowShortcut(action);
        }
    }

    /// <summary>The row at rest: its keys as keycaps, and the reset button if it's been changed.</summary>
    private void ShowShortcut(ShortcutAction action, string? message = null)
    {
        var (button, row, reset) = _rows[action.Id];
        var shortcut = Settings.ShortcutFor(action);
        button.Content = Keycaps(shortcut.Parts());
        reset.Visibility = shortcut == action.Default ? Visibility.Collapsed : Visibility.Visible;
        row.Description = message;
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
        var (button, row, _) = _rows[action.Id];
        button.Content = new TextBlock { Text = "Press keys…", FontSize = 12 };
        row.Description = null;
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
        var (button, row, _) = _rows[action.Id];

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

        void Explain(string why) => row.Description = why;

        if (Shortcut.KeyName(e.Key) is null)
        {
            Explain("Use a letter, number, function key, Tab or punctuation key");
            return;
        }
        var shortcut = new Shortcut(e.Key, ctrl, shift, alt);
        if (!ctrl && !alt && !shortcut.IsFunctionKey)
        {
            Explain("Shortcuts need Ctrl or Alt");
            return;
        }
        if (Reserved.Contains(shortcut))
        {
            Explain($"{shortcut} is already used everywhere in Windows");
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

    private async void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        StopRecording();
        if (Warnings.ResetShortcuts(Settings.Current.Shortcuts.Count) is { } warning
            && !await ConfirmDialog.AskAsync(Content.XamlRoot, warning)) return;
        Settings.Update(s => s.Shortcuts.Clear());
        foreach (var action in Settings.Actions) ShowShortcut(action);
    }

    // ───────────────────────── AI ─────────────────────────

    private static readonly string[] ClaudeModels = ["sonnet", "opus", "haiku"];
    private static readonly string[] CodexFallbackModels = ["gpt-6-luna", "gpt-6-sol", "gpt-6-astra"];

    /// <summary>Asks each CLI whether it's there and signed in, and Codex which models you have.</summary>
    private async System.Threading.Tasks.Task ShowAiAsync()
    {
        _loading = true;
        FillModels(ClaudeModelCombo, ClaudeModels, Settings.Current.ClaudeModel);
        FillModels(CodexModelCombo, CodexFallbackModels, Settings.Current.CodexModel);
        _loading = false;
        ClaudeRow.Description = CodexRow.Description = "Checking…";
        ClaudeAction.Visibility = CodexAction.Visibility = Visibility.Collapsed;

        var claude = AiReadinessCheck.CheckAsync(AiProvider.Claude);
        var codex = AiReadinessCheck.CheckAsync(AiProvider.Codex);
        ShowReadiness(AiProvider.Claude, await claude);
        var codexReady = await codex;
        ShowReadiness(AiProvider.Codex, codexReady);
        if (codexReady == AiReadiness.Ready && await AiReadinessCheck.CodexModelsAsync() is { Count: > 0 } models)
        {
            _loading = true;
            FillModels(CodexModelCombo, models, Settings.Current.CodexModel);
            _loading = false;
        }
    }

    private void ShowReadiness(AiProvider provider, AiReadiness readiness)
    {
        AiReadinessCheck.Known[provider] = readiness;
        var (row, action) = provider == AiProvider.Claude ? (ClaudeRow, ClaudeAction) : (CodexRow, CodexAction);
        row.Description = readiness switch
        {
            AiReadiness.Ready => "Ready",
            AiReadiness.SignedOut => "Not signed in",
            _ => "Not installed",
        };
        action.Content = readiness == AiReadiness.Missing ? "Get it" : "Sign in";
        action.Visibility = readiness == AiReadiness.Ready ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void FillModels(ComboBox combo, IEnumerable<string> models, string current)
    {
        var list = models.ToList();
        if (!list.Contains(current)) list.Insert(0, current);
        combo.Items.Clear();
        foreach (var model in list) combo.Items.Add(new ComboBoxItem { Content = AiNames.Model(model), Tag = model });
        combo.SelectedIndex = list.IndexOf(current);
    }

    private void AiModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || sender is not ComboBox { SelectedItem: ComboBoxItem { Tag: string model } } combo) return;
        if (combo == ClaudeModelCombo) Settings.Update(s => s.ClaudeModel = model);
        else Settings.Update(s => s.CodexModel = model);
    }

    private void AskTabToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || AskTabToggle.IsOn == Settings.Current.AskAiWithTab) return;
        Settings.Update(s => s.AskAiWithTab = AskTabToggle.IsOn);
    }

    private void AskFirstCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || AskFirstCombo.SelectedIndex < 0) return;
        Settings.Update(s => s.AskFirst = AskFirstCombo.SelectedIndex == 0 ? AiProvider.Claude : AiProvider.Codex);
    }

    /// <summary>
    /// "Sign in": a terminal running the CLI's own sign-in, which opens your browser; Drift never
    /// sees the sign-in. "Get it": the install instructions. Checked again when you come back.
    /// </summary>
    private async void AiAction_Click(object sender, RoutedEventArgs e)
    {
        var provider = (sender as FrameworkElement)?.Tag as string == "Codex" ? AiProvider.Codex : AiProvider.Claude;
        if (AiLocator.Find(provider) is not { } exe)
        {
            var page = provider == AiProvider.Claude
                ? "https://code.claude.com/docs/en/setup"
                : "https://developers.openai.com/codex/cli";
            await Launcher.LaunchUriAsync(new Uri(page));
            return;
        }
        var args = provider == AiProvider.Claude ? "auth login" : "login";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                Arguments = $"/k \"\"{exe}\" {args}\"",
                UseShellExecute = true,
            });
        }
        catch { }
        _aiRecheck = true;
    }

    // Back from signing in: check again.
    private bool _aiRecheck;
}
