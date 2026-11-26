using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Drift.Models;

/// <summary>
/// A folder in the sidebar, like Arc's: a named group of pinned tabs and other folders, between
/// the Essentials and the pinned tabs. Its tabs behave like pinned tabs (they keep their page, and
/// closing one unloads it). Folders nest up to FolderTree.MaxDepth levels. No UI types here, so it can be tested.
/// </summary>
public sealed class TabFolder : INotifyPropertyChanged
{
    // Segoe Fluent Icons: a closed folder, and an open one.
    private static readonly string ClosedGlyph = ((char)0xE8B7).ToString();
    private static readonly string OpenGlyph = ((char)0xED25).ToString();

    private string _name;
    private bool _isExpanded = true;
    private bool _isRenaming;
    private int _depth;

    public TabFolder(string name, string? id = null)
    {
        _name = name;
        Id = id ?? Guid.NewGuid().ToString("N");
    }

    /// <summary>Ties tabs and nested folders to it in session.json.</summary>
    public string Id { get; }

    public TabFolder? Parent { get; set; }

    private bool _isPinned = true;

    /// <summary>
    /// With the pinned tabs, kept like them (as Arc's folders are), or with your other tabs, which come and go
    /// (as Chrome's groups do). A group inside another is on its side; only a top-level one's is its own
    /// (FolderTree.SetPinned).
    /// </summary>
    public bool IsPinned
    {
        get => Parent?.IsPinned ?? _isPinned;
        set => _isPinned = value;
    }

    /// <summary>Its nested folders and tabs, in sidebar order, mixed (TabFolder or IFolderTab).</summary>
    public List<object> Children { get; } = new();

    /// <summary>
    /// While collapsed: tabs opened from it, which stay under its header until they're closed (or
    /// the header is clicked), as in Arc. The tab in front always shows.
    /// </summary>
    public HashSet<IFolderTab> Lingering { get; } = new();

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            OnPropertyChanged();
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Glyph));
            OnPropertyChanged(nameof(ChevronAngle));
            OnPropertyChanged(nameof(StateName));
        }
    }

    /// <summary>
    /// The chevron after its name, as Dia's groups have it: pointing right while it's closed, turned to
    /// point down while it's open.
    /// </summary>
    public float ChevronAngle => _isExpanded ? 90 : 0;

    /// <summary>Its icon: a symbol chosen for it, else a folder, open or closed.</summary>
    public string Glyph => _icon is { } icon && CustomIcon.IsSymbol(icon) ? icon : _isExpanded ? OpenGlyph : ClosedGlyph;

    private string? _icon;

    /// <summary>The icon you chose for it (Change icon…): an emoji, or a symbol (CustomIcon); null for a folder.</summary>
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
            OnPropertyChanged(nameof(ShowsGlyph));
        }
    }

    public bool HasEmoji => _icon is { } icon && !CustomIcon.IsSymbol(icon);

    public string Emoji => HasEmoji ? _icon! : "";

    /// <summary>Its row shows Glyph: not for an emoji, nor a live folder (which has its badge).</summary>
    public bool ShowsGlyph => !HasEmoji && !IsLive;

    public string StateName => _isExpanded ? "expanded" : "collapsed";

    /// <summary>The name is being edited in place (a new folder, double-click, or Rename).</summary>
    public bool IsRenaming
    {
        get => _isRenaming;
        set
        {
            if (_isRenaming == value) return;
            _isRenaming = value;
            OnPropertyChanged();
        }
    }

    /// <summary>0 for a top-level folder.</summary>
    public int Depth
    {
        get => _depth;
        set
        {
            if (_depth == value) return;
            _depth = value;
            OnPropertyChanged();
        }
    }

    /// <summary>For a live folder, what keeps its tabs (LiveFolder); null for an ordinary one.</summary>
    public LiveSource? Live
    {
        get => _live;
        set
        {
            _live = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLive));
            OnPropertyChanged(nameof(ShowsGlyph));
        }
    }

    private LiveSource? _live;

    public bool IsLive => _live is not null;

    private bool _needsSignIn;

    /// <summary>A live folder whose source said you're signed out, last time it was asked.</summary>
    public bool NeedsSignIn
    {
        get => _needsSignIn;
        set
        {
            if (_needsSignIn == value) return;
            _needsSignIn = value;
            OnPropertyChanged();
            _note?.Changed();
        }
    }

    private bool _isFetching;

    /// <summary>A live folder being asked again just now: a small spinner on its header (LiveFolders.cs).</summary>
    public bool IsFetching
    {
        get => _isFetching;
        set
        {
            if (_isFetching == value) return;
            _isFetching = value;
            OnPropertyChanged();
        }
    }

    private LiveNote? _note;

    /// <summary>A live folder's note row (LiveNote), the same one each time.</summary>
    public LiveNote Note => _note ??= new LiveNote(this);

    public IEnumerable<TabFolder> Folders => Children.OfType<TabFolder>();

    /// <summary>Every tab in it and its nested folders, in sidebar order.</summary>
    public IEnumerable<IFolderTab> AllTabs =>
        Children.SelectMany(c => c is TabFolder f ? f.AllTabs : new[] { (IFolderTab)c });

    /// <summary>It and every folder nested in it.</summary>
    public IEnumerable<TabFolder> SelfAndDescendants => new[] { this }.Concat(Folders.SelectMany(f => f.SelfAndDescendants));

    /// <summary>Levels of folders from it down: 1 for a folder with no subfolders.</summary>
    public int Height => 1 + (Folders.Any() ? Folders.Max(f => f.Height) : 0);

    /// <summary>"Work / Docs", for menus.</summary>
    public string Path => Parent is null ? Name : $"{Parent.Path} / {Name}";

    public override string ToString() => $"{Name} tab group, {StateName}";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
