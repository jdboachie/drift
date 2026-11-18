using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Drift.Tests;

/// <summary>DESIGN.md › Surfaces: every popup has rounded corners, so a custom presenter style must set them.</summary>
public class PopupStyleTests
{
    [Fact]
    public void Custom_flyout_presenter_styles_set_rounded_corners()
    {
        var source = Path.Combine(RepoRoot(), "Drift");
        var missing = (
            from file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            where !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                  && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
            let lines = File.ReadAllLines(file)
            from i in Enumerable.Range(0, lines.Length)
            where lines[i].Contains("new Style(typeof(FlyoutPresenter))")
            where !lines.Skip(i).Take(10).Any(l => l.Contains("CornerRadiusProperty"))
            select $"{Path.GetFileName(file)}:{i + 1}").ToList();
        Assert.True(missing.Count == 0, "Presenter styles without CornerRadius: " + string.Join(", ", missing));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DESIGN.md"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("DESIGN.md not found above the test's folder");
    }
}
