using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;

namespace Drift.Models;

/// <summary>
/// A Profile: a sidebar of its own for a part of your life, with its own Essentials, folders, pinned tabs and tabs, and
/// its own data (Engine), as Dia's are. The sidebar shows the one you're on (Profiles.cs); the others
/// keep their tabs, loaded or not, till you're back.
/// </summary>
public sealed class Profile : INotifyPropertyChanged
{
    /// <summary>The engine profile its pages run in, with its logins, cookies and history (ProfileData.cs).</summary>
    public EngineProfile Engine { get; set; } = null!;

    // A globe (Segoe Fluent Icons), as Arc shows a Space with no icon of its own.
    private static readonly string DefaultGlyph = ((char)0xE774).ToString();

    /// <summary>Its Essentials, at the top of its sidebar, sliding in and out with it.</summary>
    public ObservableCollection<BrowserTab> Essentials { get; } = new();
    public ObservableCollection<BrowserTab> Pinned { get; } = new();
    public ObservableCollection<BrowserTab> Tabs { get; } = new();
    public FolderTree Tree { get; } = new();

    /// <summary>The rows of its splits shown in its unpinned groups (Split.cs HostInGroup): in no list of their own.</summary>
    public System.Collections.Generic.List<BrowserTab> GroupSplitRows { get; } = new();

    /// <summary>Its tab you were last on, to come back to.</summary>
    public BrowserTab? LastActive { get; set; }

    private string _name = "";
    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropertyChanged(); } }
    }

    private string? _icon;

    /// <summary>The icon you chose for it (Change Profile icon…): an emoji, or a symbol (CustomIcon); null for none.</summary>
    public string? Icon
    {
        get => _icon;
        set
        {
            if (_icon == value) return;
            _icon = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Glyph));
            OnPropertyChanged(nameof(Emoji));
            OnPropertyChanged(nameof(HasEmoji));
        }
    }

    public bool HasEmoji => _icon is { } icon && !CustomIcon.IsSymbol(icon);
    public string Emoji => HasEmoji ? _icon! : "";
    /// <summary>Its icon as a symbol: the one chosen for it, else a globe.</summary>
    public string Glyph => _icon is { } icon && CustomIcon.IsSymbol(icon) ? icon : DefaultGlyph;

    private bool _isCurrent;
    /// <summary>It's the Profile you're on: its icon in the switcher stands out.</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        set { if (_isCurrent != value) { _isCurrent = value; OnPropertyChanged(); } }
    }

    private bool _isDot;
    /// <summary>More Profiles than the switcher has room for: it shows as a dot (ProfileOrder.Dots).</summary>
    public bool IsDot
    {
        get => _isDot;
        set { if (_isDot != value) { _isDot = value; OnPropertyChanged(); } }
    }

    private bool _isRenaming;
    /// <summary>Its name is being edited in place, in its header.</summary>
    public bool IsRenaming
    {
        get => _isRenaming;
        set { if (_isRenaming != value) { _isRenaming = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
