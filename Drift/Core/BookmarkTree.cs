using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Drift;

/// <summary>A saved page, or a folder of them (Url null, Children set).</summary>
public sealed class BookmarkNode
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Url { get; set; }
    public List<BookmarkNode>? Children { get; set; }
    public DateTimeOffset Added { get; set; } = DateTimeOffset.Now;

    [JsonIgnore]
    public bool IsFolder => Url is null;
}

/// <summary>
/// Your bookmarks: pages you've saved (Ctrl+D), in folders if you like, kept apart from the
/// sidebar's pinned tabs so there can be hundreds of them. The Library shows them, and the command
/// bar finds them. In every folder (and at the top), folders come first and pages after them. Pages are matched by address, ignoring case in the scheme and host and a
/// trailing slash.
/// </summary>
public enum BookmarkDrop { Before, After, Into }

/// <summary>Where a deleted bookmark was: its folder (null at the top) and its place in it.</summary>
public sealed record BookmarkPlace(string? FolderId, int Index);

public sealed class BookmarkTree
{
    private int _nextId = 1;

    public List<BookmarkNode> Root { get; private set; } = new();

    public BookmarkNode Add(string url, string title, BookmarkNode? folder = null)
    {
        var node = new BookmarkNode { Id = NewId(), Title = title, Url = url };
        Into(folder).Add(node);
        return node;
    }

    public BookmarkNode NewFolder(string name, BookmarkNode? parent = null)
    {
        var node = new BookmarkNode { Id = NewId(), Title = name, Children = new() };
        var list = Into(parent);
        list.Insert(list.Count(n => n.IsFolder), node);
        return node;
    }

    /// <summary>The bookmark for this page, if it's saved.</summary>
    public BookmarkNode? Find(string url)
    {
        var key = Key(url);
        return All().FirstOrDefault(n => n.Url is { } u && Key(u) == key);
    }

    public BookmarkNode? ById(string id) => All().FirstOrDefault(n => n.Id == id);

    /// <summary>The folder it's in; null at the top.</summary>
    public BookmarkNode? ParentOf(BookmarkNode node) =>
        All().FirstOrDefault(n => n.Children?.Contains(node) == true);

    public bool Remove(BookmarkNode node) => SiblingsOf(node)?.Remove(node) == true;

    /// <summary>Removed, with where it was, so Undo can put it back; null if it isn't there.</summary>
    public BookmarkPlace? Take(BookmarkNode node)
    {
        if (SiblingsOf(node) is not { } siblings) return null;
        var place = new BookmarkPlace(ParentOf(node)?.Id, siblings.IndexOf(node));
        siblings.Remove(node);
        return place;
    }

    /// <summary>Back where it was taken from; at the top if its folder has gone since.</summary>
    public void PutBack(BookmarkNode node, BookmarkPlace place)
    {
        var parent = place.FolderId is { } id ? ById(id) : null;
        var list = Into(parent);
        // Folders above pages, as ever.
        var folders = list.Count(n => n.IsFolder);
        var index = parent is null && place.FolderId is not null ? int.MaxValue : place.Index;
        list.Insert(node.IsFolder ? Math.Clamp(index, 0, folders) : Math.Clamp(index, folders, list.Count), node);
    }

    /// <summary>To a folder (null for the top), at a place in it. Not a folder into itself.</summary>
    public bool Move(BookmarkNode node, BookmarkNode? folder, int index)
    {
        if (folder is not null && (folder == node || Descendants(node).Contains(folder))) return false;
        var from = SiblingsOf(node);
        if (from is null) return false;
        var at = from.IndexOf(node);
        from.RemoveAt(at);
        var to = Into(folder);
        // Further along in the same list: it's one place nearer now.
        if (to == from && index > at) index--;
        // Folders above pages: each kind only moves among its own.
        var folders = to.Count(n => n.IsFolder);
        to.Insert(node.IsFolder ? Math.Clamp(index, 0, folders) : Math.Clamp(index, folders, to.Count), node);
        return true;
    }

    /// <summary>
    /// Dragged onto another: before or after it, in its folder, or into it if it's a folder (at the
    /// end of the folders or of the pages there). Not onto itself, nor a folder into itself.
    /// </summary>
    public bool Drop(BookmarkNode node, BookmarkNode target, BookmarkDrop where)
    {
        if (node == target) return false;
        if (where == BookmarkDrop.Into)
            return target.IsFolder && Move(node, target, int.MaxValue);
        var parent = ParentOf(target);
        var siblings = parent?.Children ?? Root;
        var index = siblings.IndexOf(target) + (where == BookmarkDrop.After ? 1 : 0);
        return Move(node, parent, index);
    }

    /// <summary>Every folder, each with its path ("Work / Reading"), in order.</summary>
    public IEnumerable<(BookmarkNode Folder, string Path)> Folders()
    {
        IEnumerable<(BookmarkNode, string)> Walk(IEnumerable<BookmarkNode> nodes, string prefix)
        {
            foreach (var node in nodes.Where(n => n.IsFolder))
            {
                var path = prefix.Length == 0 ? node.Title : $"{prefix} / {node.Title}";
                yield return (node, path);
                foreach (var inner in Walk(node.Children!, path)) yield return inner;
            }
        }
        return Walk(Root, "");
    }

    /// <summary>Saved pages with every word of the query in their title or address.</summary>
    public List<BookmarkNode> Search(string query)
    {
        var words = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return new();
        return All().Where(n => !n.IsFolder && words.All(w =>
            n.Title.Contains(w, StringComparison.OrdinalIgnoreCase) || n.Url!.Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    /// <summary>Every bookmark and folder, depth first.</summary>
    public IEnumerable<BookmarkNode> All()
    {
        var stack = new Stack<BookmarkNode>(Enumerable.Reverse(Root));
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            if (node.Children is { } children)
                for (var i = children.Count - 1; i >= 0; i--) stack.Push(children[i]);
        }
    }

    public string ToJson() => JsonSerializer.Serialize(Root, Options);

    public static BookmarkTree FromJson(string json)
    {
        var tree = new BookmarkTree();
        try
        {
            tree.Root = JsonSerializer.Deserialize<List<BookmarkNode>>(json, Options) ?? new();
        }
        catch (JsonException)
        {
            return tree;
        }
        foreach (var node in tree.All())
        {
            if (node.Url is null) node.Children ??= new();
            // Folders first, in a file written before that was the rule.
            if (node.Children is { } children) Arrange(children);
            if (int.TryParse(node.Id, out var n)) tree._nextId = Math.Max(tree._nextId, n + 1);
        }
        Arrange(tree.Root);
        foreach (var node in tree.All().Where(n => n.Id.Length == 0)) node.Id = tree.NewId();
        return tree;
    }

    private static void Arrange(List<BookmarkNode> list)
    {
        var ordered = list.Where(n => n.IsFolder).Concat(list.Where(n => !n.IsFolder)).ToList();
        list.Clear();
        list.AddRange(ordered);
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private string NewId() => (_nextId++).ToString();

    private List<BookmarkNode> Into(BookmarkNode? folder) =>
        folder is null ? Root : folder.Children ?? throw new ArgumentException("Not a folder", nameof(folder));

    private List<BookmarkNode>? SiblingsOf(BookmarkNode node) =>
        Root.Contains(node) ? Root : ParentOf(node)?.Children;

    private static IEnumerable<BookmarkNode> Descendants(BookmarkNode node) =>
        (node.Children ?? new()).SelectMany(c => Descendants(c).Prepend(c));

    /// <summary>An address as it's compared: scheme and host in lower case, no trailing slash.</summary>
    public static string Key(string url)
    {
        url = url.Trim();
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            url = $"{uri.Scheme}://{uri.Authority.ToLowerInvariant()}{uri.PathAndQuery}{uri.Fragment}";
        return url.TrimEnd('/');
    }
}
