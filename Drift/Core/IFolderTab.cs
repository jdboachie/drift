namespace Drift.Models;

/// <summary>What the folder logic needs of a tab (BrowserTab in the app, a stand-in in the tests).</summary>
public interface IFolderTab
{
    /// <summary>The folder it's in, if any.</summary>
    TabFolder? Folder { get; set; }

    /// <summary>Folder levels it sits under in the sidebar, set by FolderTree.Rows.</summary>
    int Depth { get; set; }

    /// <summary>A row standing for tabs in its folder (a split's, shown in their place): whether it stands for this one.</summary>
    bool StandsFor(IFolderTab tab) => false;
}
