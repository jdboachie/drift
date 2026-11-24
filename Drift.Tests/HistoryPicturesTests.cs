using Drift;
using Xunit;

public class HistoryPicturesTests
{
    private const string ThreeEntries = """
        {"currentIndex":1,"entries":[
          {"id":11,"url":"https://a.example/","title":"A"},
          {"id":12,"url":"https://b.example/","title":"B"},
          {"id":13,"url":"https://c.example/","title":"C"}]}
        """;

    [Fact]
    public void Its_places_come_from_the_pages_history()
    {
        var pictures = new HistoryPictures();
        pictures.Read(ThreeEntries);
        Assert.Equal(12, pictures.Current);
        Assert.Equal(11, pictures.Back);
        Assert.Equal(13, pictures.Forward);
    }

    [Fact]
    public void At_the_first_page_there_is_no_way_back()
    {
        var pictures = new HistoryPictures();
        pictures.Read("""{"currentIndex":0,"entries":[{"id":5,"url":"https://a.example/"}]}""");
        Assert.Equal(5, pictures.Current);
        Assert.Null(pictures.Back);
        Assert.Null(pictures.Forward);
    }

    [Fact]
    public void A_reply_it_cant_read_leaves_it_knowing_nothing()
    {
        var pictures = new HistoryPictures();
        pictures.Read(ThreeEntries);
        pictures.Read("not json");
        Assert.Null(pictures.Current);
        Assert.Null(pictures.Back);
    }

    [Fact]
    public void A_picture_is_kept_for_its_page()
    {
        var pictures = new HistoryPictures();
        pictures.Keep(12, [1, 2, 3]);
        Assert.Equal(new byte[] { 1, 2, 3 }, pictures.Of(12));
        Assert.Null(pictures.Of(11));
        Assert.Null(pictures.Of(null));
    }

    [Fact]
    public void It_keeps_only_the_newest_few()
    {
        var pictures = new HistoryPictures();
        for (var id = 1; id <= HistoryPictures.MostKept + 1; id++) pictures.Keep(id, [(byte)id]);
        Assert.Null(pictures.Of(1));
        Assert.NotNull(pictures.Of(2));
        Assert.NotNull(pictures.Of(HistoryPictures.MostKept + 1));
    }

    [Fact]
    public void A_page_pictured_again_counts_as_new()
    {
        var pictures = new HistoryPictures();
        for (var id = 1; id <= HistoryPictures.MostKept; id++) pictures.Keep(id, [(byte)id]);
        pictures.Keep(1, [99]);
        pictures.Keep(100, [100]);
        Assert.Equal(new byte[] { 99 }, pictures.Of(1));
        Assert.Null(pictures.Of(2));
    }

    [Fact]
    public void A_new_engine_forgets_its_pages_but_not_its_last_look()
    {
        var pictures = new HistoryPictures();
        pictures.Read(ThreeEntries);
        pictures.Keep(12, [1]);
        pictures.LastLook = ([7], "https://b.example/");
        pictures.ForgetPages();
        Assert.Null(pictures.Of(12));
        Assert.Null(pictures.Current);
        Assert.Equal(new byte[] { 7 }, pictures.LastLookAt("https://b.example/"));
    }

    [Fact]
    public void Its_last_look_is_only_for_the_page_it_was_taken_on()
    {
        var pictures = new HistoryPictures { LastLook = ([7], "https://b.example/deep") };
        Assert.Null(pictures.LastLookAt("https://b.example/"));
        Assert.Null(pictures.LastLookAt(null));
    }
}
