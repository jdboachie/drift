using System;
using System.Linq;
using System.Collections.Generic;

namespace Drift.Models;

/// <summary>
/// The sidebar's folders and the tabs in them, as Arc has them: what each holds, which are open,
/// and what the sidebar shows. No UI here (Folders.cs puts it on screen), so it can be tested.
/// </summary>
public sealed class FolderTree
{
    private readonly List<TabFolder> _folders = new();

    /// <summary>The top-level folders, in order.</summary>
    public IReadOnlyList<TabFolder> Folders => _folders;

    /// <summary>How many levels folders nest, the top level included.</summary>
    public const int MaxDepth = 4;

    /// <summary>
    /// A new, empty, open folder, last in its parent (or at the top level, with the pinned tabs or, not
    /// <paramref name="pinned"/>, with the others). Asked for a fifth level, it goes a level up instead.
    /// </summary>
    public TabFolder NewFolder(string name, TabFolder? parent = null, bool pinned = true)
    {
        // A live folder holds only what its source gives it.
        while (parent is { IsLive: true }) parent = parent.Parent;
        if (parent is not null && DepthOf(parent) + 1 >= MaxDepth) parent = parent.Parent;
        var folder = new TabFolder(name) { Parent = parent, IsPinned = pinned };
        if (parent is null) _folders.Add(folder);
        else parent.Children.Add(folder);
        return folder;
    }

    /// <summary>
    /// Puts a tab in a folder, at a gap among its contents (by default, last), taking it out of
    /// any folder it was in. The index counts the tab itself if it's already in that folder.
    /// </summary>
    public void Add(IFolderTab tab, TabFolder folder, int? index = null)
    {
        if (tab.Folder == folder && index is { } at && folder.Children.IndexOf(tab) is var from and >= 0 && at > from)
            index = at - 1;
        Remove(tab);
        tab.Folder = folder;
        folder.Children.Insert(Math.Clamp(index ?? folder.Children.Count, 0, folder.Children.Count), tab);
    }

    /// <summary>Takes a tab out of its folder.</summary>
    public void Remove(IFolderTab tab)
    {
        tab.Folder?.Children.Remove(tab);
        Unloaded(tab);
        tab.Folder = null;
        tab.Depth = 0;
    }

    /// <summary>
    /// A tab came to the front. In a closed folder, it stays under the folder's header from now
    /// on (until it's unloaded, or the header is clicked), as in Arc.
    /// </summary>
    public void Shown(IFolderTab tab)
    {
        if (ClosedFolderOf(tab) is { } closed) closed.Lingering.Add(tab);
    }

    /// <summary>0 for a top-level folder.</summary>
    public static int DepthOf(TabFolder folder)
    {
        var depth = 0;
        for (var f = folder.Parent; f is not null; f = f.Parent) depth++;
        return depth;
    }

    /// <summary>Whether a new folder can go inside this one: not a live one, and not at the deepest level (NewFolder would put it beside).</summary>
    public static bool CanHoldSubfolder(TabFolder folder) => !folder.IsLive && DepthOf(folder) + 1 < MaxDepth;

    /// <summary>Whether a folder can go into another (or to the top, for null): not inside itself, and not past MaxDepth.</summary>
    public bool CanNest(TabFolder folder, TabFolder? into)
    {
        if (into is { IsLive: true }) return false;
        for (var f = into; f is not null; f = f.Parent)
            if (f == folder) return false;
        var depth = into is null ? 0 : DepthOf(into) + 1;
        return depth + folder.Height <= MaxDepth;
    }

    /// <summary>
    /// Moves a folder into another (or to the top level, for null), to a gap among its contents.
    /// The index counts the folder itself if it's already there. Opens the folder it goes into. At the top level,
    /// it's on the side <paramref name="pinned"/> says (the list it was dropped in), else the side it was on.
    /// </summary>
    public void MoveFolder(TabFolder folder, TabFolder? into, int index, bool? pinned = null)
    {
        if (!CanNest(folder, into)) return;
        var side = pinned ?? folder.IsPinned;
        var from = into is null ? _folders.IndexOf(folder) : into.Children.IndexOf(folder);
        if (from >= 0 && index > from) index--;
        if (folder.Parent is { } old) old.Children.Remove(folder);
        else _folders.Remove(folder);
        folder.Parent = into;
        folder.IsPinned = side;
        if (into is null) _folders.Insert(Math.Clamp(index, 0, _folders.Count), folder);
        else
        {
            into.Children.Insert(Math.Clamp(index, 0, into.Children.Count), folder);
            SetExpanded(into, true);
        }
    }

    /// <summary>A top-level group's Pin or Unpin: it moves to the other list, with everything in it.</summary>
    public void SetPinned(TabFolder folder, bool pinned)
    {
        if (folder.Parent is null && _folders.Contains(folder)) folder.IsPinned = pinned;
    }

    /// <summary>
    /// The unpinned groups with no tab left in them go, as Chrome's do when their last tab closes or leaves (a pinned
    /// one stays, empty, as Arc's folders do). Returns those gone.
    /// </summary>
    public IReadOnlyList<TabFolder> PruneEmptyGroups()
    {
        var gone = _folders.Where(f => !f.IsPinned && !f.IsLive && !f.AllTabs.Any()).ToList();
        foreach (var folder in gone) _folders.Remove(folder);
        return gone;
    }

    /// <summary>
    /// Takes a folder out of the tree whole, with everything in it, to be put back (Reattach) or let
    /// go: deleting with Undo. Returns where it was.
    /// </summary>
    public (TabFolder? Parent, int Index) Detach(TabFolder folder)
    {
        var parent = folder.Parent;
        int at;
        if (parent is null)
        {
            at = _folders.IndexOf(folder);
            _folders.Remove(folder);
        }
        else
        {
            at = parent.Children.IndexOf(folder);
            parent.Children.Remove(folder);
        }
        return (parent, at);
    }

    /// <summary>
    /// A folder taken out (Detach) back where it was; at the top level, last, if the folder it was in
    /// has gone meanwhile.
    /// </summary>
    public void Reattach(TabFolder folder, TabFolder? parent, int index)
    {
        if (parent is not null && !AllFolders.Contains(parent)) (parent, index) = (null, _folders.Count);
        folder.Parent = parent;
        if (parent is null) _folders.Insert(Math.Clamp(index, 0, _folders.Count), folder);
        else parent.Children.Insert(Math.Clamp(index, 0, parent.Children.Count), folder);
    }

    /// <summary>
    /// Removes a folder. With keepTabs, what's in it moves up into its place, and the tabs of a
    /// top-level folder (which have no folder to go to) are handed back, out of the folders, for
    /// the caller to pin. Without, everything in it goes, and its tabs are handed back to close.
    /// </summary>
    public IReadOnlyList<IFolderTab> Delete(TabFolder folder, bool keepTabs)
    {
        var parent = folder.Parent;
        var side = folder.IsPinned;
        var siblings = parent?.Children ?? _folders.Cast<object>().ToList();
        var at = siblings.IndexOf(folder);
        var handedBack = new List<IFolderTab>();
        if (!keepTabs)
        {
            handedBack.AddRange(folder.AllTabs);
            foreach (var tab in handedBack) Remove(tab);
        }
        else
        {
            foreach (var child in folder.Children.ToList())
            {
                if (child is TabFolder sub)
                {
                    sub.Parent = parent;
                    sub.IsPinned = side;
                    if (parent is null) _folders.Insert(++at, sub);
                    else parent.Children.Insert(++at, sub);
                }
                else if (child is IFolderTab tab)
                {
                    if (parent is null)
                    {
                        Remove(tab);
                        handedBack.Add(tab);
                    }
                    else
                    {
                        tab.Folder = parent;
                        parent.Children.Insert(++at, tab);
                    }
                }
            }
            folder.Children.Clear();
        }
        if (parent is null) _folders.Remove(folder);
        else parent.Children.Remove(folder);
        return handedBack;
    }

    /// <summary>
    /// Where a drag over the folder rows lands, as in Arc: on a folder's header (onto), last inside
    /// it; just under an open folder's header, first inside it; otherwise in the folder of the row
    /// below, before it. A tab can't be at the top level, so between top-level folders it joins the
    /// one above. Null where it can't go (a folder into itself, or too deep).
    /// </summary>
    /// <param name="rows">What Rows returned (what's on screen).</param>
    /// <param name="gap">The gap above row <paramref name="gap"/> (rows.Count for the end).</param>
    /// <param name="dragging">The folder being dragged; null for a tab.</param>
    public FolderDrop? DropAt(IReadOnlyList<object> rows, int gap, TabFolder? onto, TabFolder? dragging)
    {
        // Nothing goes into a live folder: it holds only what its source gives it.
        bool Fits(TabFolder? into) => dragging is null ? into is { IsLive: false } : CanNest(dragging, into);

        if (onto is not null) return Fits(onto) ? new FolderDrop(onto, onto.Children.Count) : null;

        var previous = gap > 0 ? rows[gap - 1] : null;
        var next = gap < rows.Count ? rows[gap] : null;
        if (previous is TabFolder { IsExpanded: true } open && Fits(open)) return new FolderDrop(open, 0);
        if (next is IFolderTab { Folder: { } folder } nextTab)
            return Fits(folder) ? new FolderDrop(folder, FirstIndexOf(folder, nextTab)) : null;
        if (next is TabFolder nextFolder)
        {
            if (nextFolder.Parent is { } parent)
                return Fits(parent) ? new FolderDrop(parent, parent.Children.IndexOf(nextFolder)) : null;
            if (dragging is not null) return new FolderDrop(null, _folders.IndexOf(nextFolder));
        }
        // The end, or a tab between top-level folders: after the row above, in its folder.
        return previous switch
        {
            _ when dragging is not null => new FolderDrop(null, _folders.Count),
            IFolderTab { Folder: { } aboveFolder } above when Fits(aboveFolder) => new FolderDrop(aboveFolder, LastIndexOf(aboveFolder, above) + 1),
            TabFolder above when Fits(above) => new FolderDrop(above, above.Children.Count),
            _ => null,
        };
    }

    // A row's place among its folder's contents: its own, or for a row standing for tabs there, the first's or the last's.
    private static int FirstIndexOf(TabFolder folder, IFolderTab row) =>
        folder.Children.FindIndex(c => c == row || c is IFolderTab tab && row.StandsFor(tab));

    private static int LastIndexOf(TabFolder folder, IFolderTab row) =>
        folder.Children.FindLastIndex(c => c == row || c is IFolderTab tab && row.StandsFor(tab));

    // ── The session (session.json) ──

    /// <summary>Every folder, parents before the folders in them, with where each sits.</summary>
    public List<SessionFolder> Save()
    {
        var saved = new List<SessionFolder>();
        void Add(TabFolder folder, int position)
        {
            saved.Add(new SessionFolder
            {
                Id = folder.Id,
                Name = folder.Name,
                Icon = folder.Icon,
                Parent = folder.Parent?.Id,
                Unpinned = folder.Parent is null && !folder.IsPinned ? true : null,
                Expanded = folder.IsExpanded,
                Position = position,
                Live = folder.Live?.Kind,
                Dismissed = folder.Live is { } live ? live.Dismissed.Order(StringComparer.OrdinalIgnoreCase).ToList() : null,
                Filled = folder.Live?.Filled,
                Off = folder.Live is { Off.Count: > 0 } off ? off.Off.Order(StringComparer.OrdinalIgnoreCase).ToList() : null,
                Drafts = folder.Live is { Drafts: false } ? false : null,
                HiddenRepos = folder.Live is { HiddenRepos.Count: > 0 } hidden ? hidden.HiddenRepos.Order(StringComparer.OrdinalIgnoreCase).ToList() : null,
            });
            for (var i = 0; i < folder.Children.Count; i++)
                if (folder.Children[i] is TabFolder sub) Add(sub, i);
        }
        for (var i = 0; i < _folders.Count; i++) Add(_folders[i], i);
        return saved;
    }

    /// <summary>Where a tab sits among its folder's contents, for the session.</summary>
    public int? PositionOf(IFolderTab tab) => tab.Folder?.Children.IndexOf(tab);

    // Where restored folders and tabs go, until Arrange puts them there.
    private readonly Dictionary<object, int> _positions = new();

    /// <summary>
    /// The saved folders, empty, by Id. Then AddSaved each tab, and Arrange. A folder whose parent
    /// is missing (or would be too deep) comes back at the top.
    /// </summary>
    public Dictionary<string, TabFolder> Load(IEnumerable<SessionFolder>? saved)
    {
        var byId = new Dictionary<string, TabFolder>();
        var list = saved?.ToList() ?? new List<SessionFolder>();
        foreach (var s in list)
        {
            if (string.IsNullOrEmpty(s.Id) || byId.ContainsKey(s.Id)) continue;
            var folder = new TabFolder(string.IsNullOrWhiteSpace(s.Name) ? "Tab group" : s.Name, s.Id)
            {
                IsExpanded = s.Expanded,
                Icon = s.Icon,
                IsPinned = s.Unpinned != true,
            };
            if (s.Live == LiveSource.GitHubPulls)
                folder.Live = new LiveSource
                {
                    Kind = s.Live,
                    Dismissed = new HashSet<string>(s.Dismissed ?? [], StringComparer.OrdinalIgnoreCase),
                    Filled = s.Filled ?? true,
                    Off = new HashSet<string>(s.Off ?? [], StringComparer.OrdinalIgnoreCase),
                    Drafts = s.Drafts ?? true,
                    HiddenRepos = new HashSet<string>(s.HiddenRepos ?? [], StringComparer.OrdinalIgnoreCase),
                };
            byId[s.Id] = folder;
            _positions[folder] = s.Position;
        }
        foreach (var s in list)
        {
            if (!byId.TryGetValue(s.Id, out var folder) || folder.Parent is not null || _folders.Contains(folder)) continue;
            if (s.Parent is { } parentId && byId.TryGetValue(parentId, out var parent) && parent != folder
                && DepthOf(parent) + 1 < MaxDepth)
            {
                folder.Parent = parent;
                parent.Children.Add(folder);
            }
            else _folders.Add(folder);
        }
        return byId;
    }

    /// <summary>A tab from the session, into its folder; Arrange puts it at its saved position.</summary>
    public void AddSaved(IFolderTab tab, TabFolder folder, int? position)
    {
        Add(tab, folder);
        _positions[tab] = position ?? int.MaxValue;
    }

    /// <summary>Puts restored folders and tabs in their saved order.</summary>
    public void Arrange()
    {
        int At(object item) => _positions.TryGetValue(item, out var p) ? p : int.MaxValue;
        var top = _folders.OrderBy(At).ToList();
        _folders.Clear();
        _folders.AddRange(top);
        foreach (var folder in AllFolders.ToList())
        {
            var sorted = folder.Children.OrderBy(At).ToList();
            folder.Children.Clear();
            folder.Children.AddRange(sorted);
        }
        _positions.Clear();
    }

    /// <summary>A tab was unloaded (or is leaving): it no longer stays under a closed folder.</summary>
    public void Unloaded(IFolderTab tab)
    {
        foreach (var folder in AllFolders) folder.Lingering.Remove(tab);
    }

    /// <summary>Every folder, nested ones included, top to bottom.</summary>
    public IEnumerable<TabFolder> AllFolders => _folders.SelectMany(f => f.SelfAndDescendants);

    /// <summary>Every tab in a folder, top to bottom.</summary>
    public IEnumerable<IFolderTab> AllTabs => _folders.SelectMany(f => f.AllTabs);

    /// <summary>
    /// A click on a folder's header. A closed folder showing tabs opened from it tucks them away
    /// first (the tab in front still shows); otherwise it opens or closes.
    /// </summary>
    public void Toggle(TabFolder folder, IFolderTab? active)
    {
        if (!folder.IsExpanded && folder.Lingering.Any(t => t != active)) folder.Lingering.Clear();
        else SetExpanded(folder, !folder.IsExpanded);
    }

    /// <summary>Opens or closes a folder. Either starts over: only tabs opened from now on stay under a closed folder.</summary>
    public void SetExpanded(TabFolder folder, bool expanded)
    {
        if (folder.IsExpanded == expanded) return;
        foreach (var f in folder.SelfAndDescendants) f.Lingering.Clear();
        folder.IsExpanded = expanded;
    }

    /// <summary>The outermost closed folder a tab is hidden in, if any.</summary>
    private static TabFolder? ClosedFolderOf(IFolderTab tab)
    {
        TabFolder? closed = null;
        for (var f = tab.Folder; f is not null; f = f.Parent)
            if (!f.IsExpanded) closed = f;
        return closed;
    }

    /// <summary>
    /// What the sidebar shows, top to bottom: folder headers and tabs, each with its Depth set. With
    /// <paramref name="pinned"/>, only that side's groups: the pinned list's, or the unpinned list's.
    /// </summary>
    /// <param name="rowOf">The row a tab shows as: itself, another standing for it (a split's, shown once, where the first
    /// of its tabs is), or none.</param>
    public IReadOnlyList<object> Rows(IFolderTab? active, bool? pinned = null, Func<IFolderTab, IFolderTab?>? rowOf = null)
    {
        var rows = new List<object>();
        var shown = new HashSet<IFolderTab>();
        IFolderTab? RowOf(IFolderTab tab) => rowOf is null ? tab : rowOf(tab);
        var activeRow = active is null ? null : RowOf(active);
        void AddFolder(TabFolder folder, int depth)
        {
            folder.Depth = depth;
            rows.Add(folder);
            if (folder.IsExpanded)
            {
                if (LiveNote.Shows(folder))
                {
                    folder.Note.Depth = depth + 1;
                    rows.Add(folder.Note);
                }
                foreach (var child in folder.Children)
                {
                    if (child is TabFolder sub) AddFolder(sub, depth + 1);
                    else if (child is IFolderTab tab) AddTab(tab, depth + 1);
                }
            }
            else
            {
                // Closed: only the tab in front and the ones opened from it since it closed.
                foreach (var tab in folder.AllTabs)
                    if (tab == active || folder.Lingering.Contains(tab) || activeRow is not null && RowOf(tab) == activeRow) AddTab(tab, depth + 1);
            }
        }
        void AddTab(IFolderTab tab, int depth)
        {
            if (RowOf(tab) is not { } row || !shown.Add(row)) return;
            row.Depth = depth;
            rows.Add(row);
        }
        foreach (var folder in _folders)
            if (pinned is not { } side || folder.IsPinned == side) AddFolder(folder, 0);
        return rows;
    }
}

/// <summary>Where a drop lands: inside a folder (null: the top level, for folders) at a gap among its contents.</summary>
public readonly record struct FolderDrop(TabFolder? Into, int Index);
