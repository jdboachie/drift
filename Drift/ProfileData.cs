using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Drift.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;

namespace Drift;

/// <summary>
/// Profiles, as Dia's and Safari's are: each its own sidebar (Profiles.cs) and its own
/// data, its pages running in an engine profile of its own (Profile.Profile, ProfileIds), with its own logins, cookies and
/// history. Your default Profile is the engine's own profile, the data you had before there were Profiles, and can't be
/// deleted. A session from when Profiles could share a profile gives each its own (ProfileIds.Unshare).
/// <list type="bullet">
/// <item>New Profile (+) takes the sidebar's place with Create a Profile (Profiles.cs); it starts fresh.</item>
/// <item>Settings › Profiles: each Profile, Rename…, and Delete for any but the default; New Profile…
/// (SettingsWindow.ShowProfiles).</item>
/// <item>Deleting one closes its tabs, and the engine deletes its data (when it next stops, as it can't while running).</item>
/// </list>
/// </summary>
public sealed partial class MainWindow
{
    // Engine profiles deleted while the engine runs: their data goes when it stops, so their ids aren't given out till then.
    private static readonly HashSet<string> DeletedProfileIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Before the window's made: the first Profile, before a session says otherwise, is your default.</summary>
    private void SetUpEngine() => _profile.Engine = new EngineProfile(ProfileIds.Default);

    /// <summary>An engine profile for a Profile that's new: one no other has, nor one being deleted.</summary>
    private EngineProfile NewEngineProfile() => new(ProfileIds.Next(Profiles.Select(s => s.Engine.Id), DeletedProfileIds));

    /// <summary>The engine profile a tab's page runs in: its Profile's.</summary>
    private EngineProfile EngineOf(BrowserTab tab) => tab.Engine ?? (ProfileOf(tab) ?? _profile).Engine;

    /// <summary>
    /// A tab now in another Profile (Move to Profile): its page starts again in that one's engine profile, on the page
    /// it was on; now if it's showing, otherwise when it's next opened.
    /// </summary>
    private void MoveToEngine(BrowserTab tab, EngineProfile engine)
    {
        if (EngineOf(tab) == engine) return;
        tab.Engine = engine;
        // One that hasn't started (unloaded, or put back asleep) starts in its new profile anyway.
        if (tab.View.CoreWebView2 is null && !_startingViews.Contains(tab.View)) return;
        var shown = _shownPages.Contains(tab);
        RestartTab(tab, start: false);
        if (shown && _active is { } active)
        {
            ShowPages(active);
            foreach (var page in _shownPages) LoadShown(page);
        }
    }

    /// <summary>
    /// A Profile's name, asked in a dialog: Arc's box, its "already exists" under it while the name's another's, then
    /// <paramref name="ask"/>'s words. Null if it's cancelled (or another dialog's open).
    /// </summary>
    /// <param name="others">The other Profiles' names, which it can't be.</param>
    /// <param name="current">Renaming: the name now, filled in and picked.</param>
    internal static async Task<string?> AskProfileNameAsync(XamlRoot root, Warning ask, IEnumerable<string> others, string? current = null)
    {
        var taken = others.ToList();
        var nameBox = new TextBox { PlaceholderText = "Profile name…", Text = current ?? "" };
        AutomationProperties.SetName(nameBox, "Profile name");
        nameBox.Loaded += (_, _) => nameBox.SelectAll();
        var takenText = new TextBlock
        {
            Text = Warnings.ProfileNameTaken,
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            Visibility = Visibility.Collapsed,
        };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(nameBox);
        content.Children.Add(takenText);
        if (ask.Body.Length > 0) content.Children.Add(new TextBlock { Text = ask.Body, TextWrapping = TextWrapping.Wrap });
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = ask.Title,
            Content = content,
            PrimaryButtonText = ask.Action,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
        };
        string? name = null;
        nameBox.TextChanged += (_, _) =>
        {
            name = ProfileIds.CleanName(nameBox.Text);
            var isTaken = name is not null && ProfileIds.NameTaken(name, taken);
            takenText.Visibility = Ui.Shown(isTaken);
            // Renaming to the name it has already is no change.
            dialog.IsPrimaryButtonEnabled = name is not null && !isTaken && name != current;
        };
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary ? name : null;
        }
        catch (Exception)
        {
            // Another dialog's already open: nothing is done.
            return null;
        }
    }

    // ── Settings › Profiles ──

    /// <summary>A new Profile from Settings, last in the switcher: an empty sidebar, its data fresh. You stay where you are.</summary>
    internal Profile NewProfileNamed(string name)
    {
        var profile = new Profile { Name = name, Engine = NewEngineProfile() };
        AddProfile(profile, Profiles.Count);
        SaveSessionSoon();
        return profile;
    }

    internal void RenameProfile(Profile profile, string name)
    {
        profile.Name = name;
        SaveSessionSoon();
    }

    /// <summary>Delete Profile… from its menu: asked first, then deleted.</summary>
    private async void AskDeleteProfile(Profile profile)
    {
        if (!ProfileIds.CanDelete(profile.Engine.Id) || !Profiles.Contains(profile)) return;
        if (!await AskAsync(Warnings.DeleteProfile(profile.Name))) return;
        await DeleteProfileAsync(profile);
    }

    /// <summary>
    /// A Profile deleted (already asked), as Safari's: its tabs close, for good, and its history, logins and cookies are
    /// deleted by the engine (when it next stops, as it can't while running). Never your default one.
    /// </summary>
    internal async Task DeleteProfileAsync(Profile profile)
    {
        if (!ProfileIds.CanDelete(profile.Engine.Id) || !Profiles.Contains(profile) || Profiles.Count < 2) return;
        if (!await MayLeaveAsync(TabsIn(profile).ToList()) || !Profiles.Contains(profile)) return;
        var at = Profiles.IndexOf(profile);
        if (profile == _profileShown)
        {
            var others = Profiles.Where(s => s != profile).ToList();
            ShowProfile(others[ProfileOrder.AfterDelete(at, at)]);
        }
        // Its tabs aren't kept in Closed tabs: their data's going.
        InProfile(profile, () =>
        {
            foreach (var tab in TabsIn(profile).ToList()) RemoveTab(tab, remember: false);
        });
        if (_sparePeekProfile == profile.Engine) DropSparePeekView();
        Profiles.Remove(profile);
        DeletedProfileIds.Add(profile.Engine.Id);
        SaveSessionSoon();
        UpdateMediaBar();

        // The engine deletes a profile from one of its pages: a hidden one, made for it, if none is open.
        var view = new WebView2 { Opacity = 0, IsHitTestVisible = false };
        PeekWebHost.Children.Insert(0, view);
        try
        {
            await EnsureEngineAsync(view, await EnvironmentAsync(), isPrivate: false, profile.Engine.Id);
            Extensions.DetachOther(view.CoreWebView2.Profile);
            view.CoreWebView2.Profile.Delete();
        }
        catch (Exception)
        {
            // Its folder stays on disk; it's no longer one of yours either way.
        }
        finally
        {
            PeekWebHost.Children.Remove(view);
            try { view.Close(); } catch { }
        }
    }

    /// <summary>A view's engine, in a profile: the private window's, one of yours, or the engine's own (Default).</summary>
    internal static async Task EnsureEngineAsync(WebView2 view, CoreWebView2Environment environment, bool isPrivate, string? profile)
    {
        if (isPrivate) await view.EnsureCoreWebView2Async(environment, PrivateOptions(environment));
        else if (profile is null || ProfileIds.IsEngineDefault(profile)) await view.EnsureCoreWebView2Async(environment);
        else
        {
            var options = environment.CreateCoreWebView2ControllerOptions();
            options.ProfileName = profile;
            await view.EnsureCoreWebView2Async(environment, options);
        }
    }
}
