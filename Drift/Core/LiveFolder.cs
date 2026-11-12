using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Drift.Models;

/// <summary>
/// What makes a folder live, as in Arc: its tabs are kept to a source's list (for now, your GitHub
/// pull requests: GitHubPulls), added and taken away as it changes. No UI types here, so it can be tested.
/// </summary>
public sealed class LiveSource
{
    /// <summary>The kind saved in session.json for a folder of your GitHub pull requests.</summary>
    public const string GitHubPulls = "github-pulls";

    public string Kind { get; init; } = GitHubPulls;

    /// <summary>Pull requests whose tabs you closed, kept out until they're finished on GitHub.</summary>
    public HashSet<string> Dismissed { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// It has been filled once. Until then what arrives isn't new to you, so it isn't marked new.
    /// </summary>
    public bool Filled { get; set; }

    /// <summary>The last answer had nothing in it (LiveFolder.Believe).</summary>
    public bool EmptyOnce { get; set; }
}

/// <summary>
/// A dim row in an open live folder that has something to say: that you're signed out (clicking it
/// signs you in), or that there's nothing in it. One per folder, so the list keeps the same row.
/// </summary>
public sealed class LiveNote : INotifyPropertyChanged
{
    internal LiveNote(TabFolder folder) => Folder = folder;

    public TabFolder Folder { get; }

    /// <summary>Folder levels it sits under, set by FolderTree.Rows.</summary>
    public int Depth { get; set; }

    public bool OpensSignIn => Folder.NeedsSignIn;

    public string Text => Folder.NeedsSignIn ? "Sign in to GitHub" : "No open pull requests";

    /// <summary>Whether its folder, open, shows it: signed out, or filled and empty.</summary>
    public static bool Shows(TabFolder folder) =>
        folder.Live is { } live && (folder.NeedsSignIn || (live.Filled && folder.Children.Count == 0));

    internal void Changed()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OpensSignIn)));
    }

    public override string ToString() => Text;

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>What to do to a live folder's tabs to bring them in line with the list.</summary>
/// <param name="Add">Pull requests to add, in order, at the top.</param>
/// <param name="Remove">Keys of the tabs to take away.</param>
/// <param name="Titles">Each key still there, with its title on GitHub.</param>
/// <param name="Dismissed">The closed ones to keep out from now on: those still open on GitHub.</param>
public sealed record LivePlan(
    IReadOnlyList<PullRequest> Add,
    IReadOnlyList<string> Remove,
    IReadOnlyDictionary<string, string> Titles,
    HashSet<string> Dismissed);

public static class LiveFolder
{
    /// <summary>
    /// Whether to go by an answer. GitHub's search has been seen to answer with nothing for a moment
    /// (signed in, no error), so an empty answer counts only the second time running; until then the
    /// folder stays as it is.
    /// </summary>
    public static bool Believe(LiveSource live, IReadOnlyCollection<PullRequest> found)
    {
        if (found.Count > 0)
        {
            live.EmptyOnce = false;
            return true;
        }
        if (live.EmptyOnce) return true;
        live.EmptyOnce = true;
        return false;
    }

    /// <param name="showing">The key of each tab in the folder (null for a tab that isn't a pull request's).</param>
    /// <param name="found">What the source has now.</param>
    /// <param name="dismissed">Keys whose tabs were closed.</param>
    /// <param name="keep">Whether a tab that's finished stays for now (it's in front, say).</param>
    public static LivePlan Reconcile(IReadOnlyList<string?> showing, IReadOnlyList<PullRequest> found,
        ISet<string> dismissed, Func<string, bool> keep)
    {
        var there = new HashSet<string>(showing.OfType<string>(), StringComparer.OrdinalIgnoreCase);
        var current = found.ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);
        var add = found.Where(p => !there.Contains(p.Key) && !dismissed.Contains(p.Key)).ToList();
        var remove = there.Where(k => !current.ContainsKey(k) && !keep(k)).ToList();
        var titles = there.Where(current.ContainsKey).ToDictionary(k => k, k => current[k].Title, StringComparer.OrdinalIgnoreCase);
        var stillDismissed = new HashSet<string>(dismissed.Where(current.ContainsKey), StringComparer.OrdinalIgnoreCase);
        return new LivePlan(add, remove, titles, stillDismissed);
    }
}
