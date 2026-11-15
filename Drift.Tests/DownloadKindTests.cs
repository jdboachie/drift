using System;
using System.Linq;
using Drift;
using Xunit;

public class DownloadKindTests
{
    [Theory]
    [InlineData("photo.JPG", DownloadKind.Images)]
    [InlineData("shot.png", DownloadKind.Images)]
    [InlineData("logo.svg", DownloadKind.Images)]
    [InlineData("pic.heic", DownloadKind.Images)]
    [InlineData("clip.mp4", DownloadKind.Videos)]
    [InlineData("talk.webm", DownloadKind.Videos)]
    [InlineData("song.mp3", DownloadKind.Audio)]
    [InlineData("memo.m4a", DownloadKind.Audio)]
    [InlineData("paper.pdf", DownloadKind.Documents)]
    [InlineData("CV.docx", DownloadKind.Documents)]
    [InlineData("sheet.xlsx", DownloadKind.Documents)]
    [InlineData("data.csv", DownloadKind.Documents)]
    [InlineData("SKILL.md", DownloadKind.Documents)]
    [InlineData("notes.txt", DownloadKind.Documents)]
    [InlineData("bundle.zip", DownloadKind.Archives)]
    [InlineData("src.tar.gz", DownloadKind.Archives)]
    [InlineData("setup.exe", DownloadKind.Apps)]
    [InlineData("tool.msi", DownloadKind.Apps)]
    [InlineData("drift-test.bin", DownloadKind.Other)]
    [InlineData("README", DownloadKind.Other)]
    [InlineData("", DownloadKind.Other)]
    public void Sorts_a_file_by_its_extension(string name, DownloadKind kind) =>
        Assert.Equal(kind, DownloadKinds.Of(name));

    [Fact]
    public void Every_kind_has_a_name_for_the_menu()
    {
        foreach (var kind in Enum.GetValues<DownloadKind>()) Assert.False(string.IsNullOrWhiteSpace(DownloadKinds.Name(kind)));
        Assert.Equal(Enum.GetValues<DownloadKind>().Length, DownloadKinds.All.Count);
    }
}

public class DownloadPeekFilterTests
{
    private record File(string Name, bool Running, bool Saved, bool OnDisk);

    private static (bool, bool, bool) StateOf(File f) => (f.Running, f.Saved, f.OnDisk);

    [Fact]
    public void Leaves_out_the_kinds_you_turned_off_and_still_shows_three()
    {
        var files = new[]
        {
            new File("a.png", false, true, true),
            new File("b.pdf", false, true, true),
            new File("c.zip", true, false, false),
            new File("d.jpg", false, true, true),
            new File("e.docx", false, true, true),
            new File("f.mp4", false, true, true),
        };
        var shown = DownloadPeek.Pick(files, StateOf, f => DownloadKinds.Of(f.Name) != DownloadKind.Images);
        Assert.Equal(["b.pdf", "c.zip", "e.docx"], shown.Select(f => f.Name));
    }
}

public class DownloadArchiveTests
{
    private static readonly DateTime Now = new(2026, 11, 13, 17, 0, 0);

    [Theory]
    [InlineData(1, 23, false)]
    [InlineData(1, 24, true)]
    [InlineData(7, 24 * 7 - 1, false)]
    [InlineData(7, 24 * 7, true)]
    [InlineData(30, 24 * 29, false)]
    [InlineData(30, 24 * 31, true)]
    public void A_finished_download_leaves_the_list_once_it_is_older_than_you_keep_them(int keepDays, int hoursAgo, bool leaves) =>
        Assert.Equal(leaves, DownloadArchive.Leaves(running: false, Now.AddHours(-hoursAgo), Now, keepDays));

    [Fact]
    public void Kept_forever_when_you_said_never() =>
        Assert.False(DownloadArchive.Leaves(running: false, Now.AddYears(-3), Now, keepDays: 0));

    [Fact]
    public void A_download_still_running_stays() =>
        Assert.False(DownloadArchive.Leaves(running: true, Now.AddDays(-40), Now, keepDays: 7));
}
