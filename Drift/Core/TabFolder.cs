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
            OnPropertyChanged(nameof(StateName));
        }
    }

    public string Glyph => _isExpanded ? OpenGlyph : ClosedGlyph;

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

    public override string ToString() => $"{Name} folder, {StateName}";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
