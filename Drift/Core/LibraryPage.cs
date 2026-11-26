namespace Drift;

/// <summary>Which of the Library's pages its button opens (Library.cs).</summary>
public static class LibraryPage
{
    /// <summary>
    /// Downloads while there's one to see there: still going, or finished since the list was last looked at. Otherwise
    /// the page you were on last.
    /// </summary>
    public static string ToOpen(string last, bool unseen, bool running) => unseen || running ? "downloads" : last;
}
