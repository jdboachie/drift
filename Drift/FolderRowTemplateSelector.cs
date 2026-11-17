using Drift.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Drift;

/// <summary>The folder list's rows: a folder's header, or a tab in a folder.</summary>
public sealed partial class FolderRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? FolderTemplate { get; set; }
    public DataTemplate? TabTemplate { get; set; }
    public DataTemplate? NoteTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item switch
    {
        TabFolder => FolderTemplate,
        BrowserTab => TabTemplate,
        LiveNote => NoteTemplate,
        _ => null,
    };

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}

/// <summary>
/// The folder list's rows' own looks: a folder's header has no hover fill of a tab's (hovered while
/// closed, it shows its card, as Dia's groups do); everything else is a tab's row.
/// </summary>
public sealed partial class FolderRowStyleSelector : StyleSelector
{
    public Style? FolderStyle { get; set; }
    public Style? RowStyle { get; set; }

    protected override Style? SelectStyleCore(object item, DependencyObject container) => item is TabFolder ? FolderStyle : RowStyle;
}
