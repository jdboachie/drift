using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Drift.Tests;

/// <summary>
/// DESIGN.md › Components: the window is built from shared pieces (Views/, Controls/, Styles/), and these keep it so.
/// </summary>
public class ComponentRulesTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// A shadow comes from a shared piece: a floating panel's or the page controls' style (Styles/Surfaces.xaml) or a
    /// page's card (Controls/PageCardView.xaml). Never one made on the spot, in XAML or in code.
    /// </summary>
    [Fact]
    public void Shadows_come_only_from_the_shared_pieces()
    {
        string[] allowed = [Path.Combine("Styles", "Surfaces.xaml"), Path.Combine("Controls", "PageCardView.xaml")];
        var found = (
            from file in SourceFiles("*.xaml").Concat(SourceFiles("*.cs"))
            let name = Path.GetRelativePath(Source, file)
            where !allowed.Contains(name)
            let lines = File.ReadAllLines(file)
            from i in Enumerable.Range(0, lines.Length)
            where lines[i].Contains("<ThemeShadow") || lines[i].Contains("new ThemeShadow")
            select $"{name}:{i + 1}").ToList();
        Assert.True(found.Count == 0, "Shadows made on the spot (use FloatingPanelStyle or PageCardView): " + string.Join(", ", found));
    }

    /// <summary>Every colour has a light and a dark value: a key in one theme and not the other shows the wrong one.</summary>
    [Fact]
    public void Every_colour_has_a_light_and_a_dark_value()
    {
        var light = ThemeKeys("Light");
        var dark = ThemeKeys("Dark");
        Assert.True(light.SetEquals(dark),
            "Only light: " + string.Join(", ", light.Except(dark)) + ". Only dark: " + string.Join(", ", dark.Except(light)));
    }

    // Windows' own close button (red on hover), which isn't one of Drift's colours.
    private static readonly HashSet<string> WindowsColours = ["#FFC42B1C", "#FFC83C31", "#E6FFFFFF"];

    /// <summary>
    /// Colours live in Styles/Colors.xaml. Where a button's template can only take a colour written out (its
    /// ButtonBackground and the like), the colour written must be one of Drift's, in the same theme, so it changes
    /// when Colors.xaml does.
    /// </summary>
    [Fact]
    public void Colours_written_out_are_Drifts_own()
    {
        var colours = new Dictionary<string, HashSet<string>> { ["Light"] = ThemeColours("Light"), ["Dark"] = ThemeColours("Dark") };
        var colorsFile = Path.Combine(Source, "Styles", "Colors.xaml");
        var wrong = new List<string>();
        foreach (var file in SourceFiles("*.xaml").Where(f => f != colorsFile))
            foreach (var at in XDocument.Load(file, LoadOptions.SetLineInfo).Descendants())
                foreach (var attr in at.Attributes().Where(a => Regex.IsMatch(a.Value, "^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$")))
                {
                    var colour = Normalize(attr.Value);
                    if (WindowsColours.Contains(colour)) continue;
                    var theme = at.Ancestors().Select(a => (string?)a.Attribute(X + "Key")).FirstOrDefault(k => k is "Light" or "Dark");
                    var ok = theme is null ? colours.Values.All(c => c.Contains(colour)) : colours[theme].Contains(colour);
                    if (!ok) wrong.Add($"{Path.GetRelativePath(Source, file)}:{((System.Xml.IXmlLineInfo)at).LineNumber} {attr.Value} ({theme ?? "both themes"})");
                }
        Assert.True(wrong.Count == 0, "Colours that aren't Drift's (put them in Styles/Colors.xaml): " + string.Join(", ", wrong));
    }

    /// <summary>A list's row in MainWindow.xaml is one element: anything more is a control of its own (Controls/).</summary>
    [Fact]
    public void The_windows_list_templates_are_one_element()
    {
        var window = XDocument.Load(Path.Combine(Source, "MainWindow.xaml"), LoadOptions.SetLineInfo);
        var big = (
            from template in window.Descendants().Where(e => e.Name.LocalName == "DataTemplate")
            let root = template.Elements().SingleOrDefault()
            where root is null || root.Descendants().Any()
            select ((System.Xml.IXmlLineInfo)template).LineNumber).ToList();
        Assert.True(big.Count == 0, "Templates in MainWindow.xaml with more than one element (make a control): lines " + string.Join(", ", big));
    }

    private static HashSet<string> ThemeKeys(string theme) =>
        Theme(theme).Elements().Select(e => (string?)e.Attribute(X + "Key")).OfType<string>().ToHashSet();

    private static HashSet<string> ThemeColours(string theme) =>
        Theme(theme).Descendants().SelectMany(e => e.Attributes())
            .Where(a => Regex.IsMatch(a.Value, "^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$"))
            .Select(a => Normalize(a.Value)).ToHashSet();

    private static XElement Theme(string theme) =>
        XDocument.Load(Path.Combine(Source, "Styles", "Colors.xaml")).Descendants()
            .Single(e => e.Name.LocalName == "ResourceDictionary" && (string?)e.Attribute(X + "Key") == theme);

    // #RRGGBB is opaque: #FFRRGGBB.
    private static string Normalize(string hex) => (hex.Length == 7 ? "#FF" + hex[1..] : hex).ToUpperInvariant();

    private static string Source => Path.Combine(RepoRoot(), "Drift");

    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(Source, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DESIGN.md"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("DESIGN.md not found above the test's folder");
    }
}
