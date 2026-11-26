using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Drift.Tests;

/// <summary>DESIGN.md › Color: text reads at 4.5:1 or better on the sidebar, icons and dots at 3:1.</summary>
public class ContrastTests
{
    // The sidebar as it's drawn (measured in screenshots), and with a row's hover tint over it.
    private const string LightSidebar = "#F9F9F9", DarkSidebar = "#202020";

    [Theory]
    [InlineData("Light", LightSidebar)]
    [InlineData("Dark", DarkSidebar)]
    public void Secondary_text_reads_on_the_sidebar_and_on_a_hovered_row(string theme, string sidebar)
    {
        var text = Brush(theme, "DriftSecondaryTextBrush");
        var hover = Over(Brush(theme, "DriftHoverBrush"), Parse(sidebar));
        Assert.True(Ratio(text, Parse(sidebar)) >= 4.5, $"{theme}: {Ratio(text, Parse(sidebar)):N2} on the sidebar");
        Assert.True(Ratio(text, hover) >= 4.5, $"{theme}: {Ratio(text, hover):N2} on a hovered row");
    }

    [Theory]
    [InlineData("Light", LightSidebar)]
    [InlineData("Dark", DarkSidebar)]
    public void A_Profile_you_are_not_on_still_shows_in_the_switcher(string theme, string sidebar)
    {
        var icon = Brush(theme, "DriftIconBrush");
        var faded = Over((icon.r, icon.g, icon.b, ProfileOrder.IconStrength(current: false)), Parse(sidebar));
        Assert.True(Ratio(faded, Parse(sidebar)) >= 3, $"{theme}: {Ratio(faded, Parse(sidebar)):N2}");
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void A_meeting_s_faces_read_in_white(string theme)
    {
        var keys = Enumerable.Range(0, MeetingBar.FaceColors).Select(i => $"DriftFace{i}Brush").Append("DriftFaceMoreBrush");
        foreach (var key in keys)
        {
            var face = Brush(theme, key);
            var ratio = Ratio((255, 255, 255), (face.r, face.g, face.b));
            Assert.True(ratio >= 4.5, $"{theme} {key}: {ratio:N2}");
        }
    }

    private static (double r, double g, double b, double a) Brush(string theme, string key)
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "Drift", "Styles", "Colors.xaml"));
        var start = xaml.IndexOf($"x:Key=\"{theme}\"", StringComparison.Ordinal);
        var match = new Regex($"x:Key=\"{key}\" Color=\"(#[0-9A-Fa-f]{{6,8}})\"").Match(xaml, start);
        Assert.True(match.Success, $"{key} not found in {theme}");
        return ParseArgb(match.Groups[1].Value);
    }

    private static (double r, double g, double b, double a) ParseArgb(string hex)
    {
        hex = hex.TrimStart('#');
        var a = hex.Length == 8 ? Convert.ToInt32(hex[..2], 16) / 255.0 : 1;
        var rgb = hex[^6..];
        return (Convert.ToInt32(rgb[..2], 16), Convert.ToInt32(rgb[2..4], 16), Convert.ToInt32(rgb[4..], 16), a);
    }

    private static (double r, double g, double b) Parse(string hex)
    {
        var c = ParseArgb(hex);
        return (c.r, c.g, c.b);
    }

    private static (double r, double g, double b) Over((double r, double g, double b, double a) top, (double r, double g, double b) under) =>
        (top.r * top.a + under.r * (1 - top.a), top.g * top.a + under.g * (1 - top.a), top.b * top.a + under.b * (1 - top.a));

    private static double Ratio((double r, double g, double b, double a) text, (double r, double g, double b) back) =>
        Ratio(Over(text, back), back);

    private static double Ratio((double r, double g, double b) a, (double r, double g, double b) b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance((double r, double g, double b) c)
    {
        static double Channel(double v) { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        return 0.2126 * Channel(c.r) + 0.7152 * Channel(c.g) + 0.0722 * Channel(c.b);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DESIGN.md"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("DESIGN.md not found above the test's folder");
    }
}
