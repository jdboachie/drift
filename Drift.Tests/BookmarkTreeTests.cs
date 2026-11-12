using Drift;

public class BookmarkTreeTests
{
    [Fact]
    public void A_saved_page_is_found_by_its_address()
    {
        var tree = new BookmarkTree();
        var saved = tree.Add("https://example.com/docs/", "Docs");
        Assert.Same(saved, tree.Find("https://example.com/docs"));
        Assert.Same(saved, tree.Find("HTTPS://EXAMPLE.COM/docs/"));
        Assert.Null(tree.Find("https://example.com/other"));
    }

    [Fact]
    public void Each_bookmark_and_folder_has_its_own_id()
    {
        var tree = new BookmarkTree();
        var a = tree.Add("https://a.com", "A");
        var f = tree.NewFolder("Work");
        var b = tree.Add("https://b.com", "B", f);
        Assert.Equal(3, new[] { a.Id, f.Id, b.Id }.Distinct().Count());
        Assert.Same(b, tree.ById(b.Id));
    }

    [Fact]
    public void Into_a_folder_and_found_there()
    {
        var tree = new BookmarkTree();
        var work = tree.NewFolder("Work");
        var saved = tree.Add("https://jira.example.com", "Jira", work);
        Assert.Same(work, tree.ParentOf(saved));
        Assert.Single(work.Children!);
        Assert.Empty(tree.Root.Where(n => !n.IsFolder));
    }

    [Fact]
    public void Folders_are_listed_with_their_paths()
    {
        var tree = new BookmarkTree();
        var work = tree.NewFolder("Work");
        tree.NewFolder("Reading", work);
        tree.NewFolder("Recipes");
        Assert.Equal(new[] { "Work", "Work / Reading", "Recipes" }, tree.Folders().Select(f => f.Path));
    }

    [Fact]
    public void Removing_takes_it_out_wherever_it_is()
    {
        var tree = new BookmarkTree();
        var work = tree.NewFolder("Work");
        var saved = tree.Add("https://a.com", "A", work);
        Assert.True(tree.Remove(saved));
        Assert.Empty(work.Children!);
        Assert.Null(tree.Find("https://a.com"));
        Assert.False(tree.Remove(saved));
    }

    [Fact]
    public void Removing_a_folder_removes_what_is_in_it()
    {
        var tree = new BookmarkTree();
        var work = tree.NewFolder("Work");
        tree.Add("https://a.com", "A", work);
        tree.Remove(work);
        Assert.Empty(tree.Root);
        Assert.Null(tree.Find("https://a.com"));
    }

    [Fact]
    public void Moving_to_another_folder_and_place()
    {
        var tree = new BookmarkTree();
        var a = tree.Add("https://a.com", "A");
        var b = tree.Add("https://b.com", "B");
        var work = tree.NewFolder("Work");
        // Pages after folders: first among the pages is as far up as it goes.
        tree.Move(b, null, 0);
        Assert.Equal(new[] { work, b, a }, tree.Root);
        tree.Move(a, work, 0);
        Assert.Same(work, tree.ParentOf(a));
        Assert.Equal(new[] { work, b }, tree.Root);
    }

    [Fact]
    public void A_folder_cannot_go_inside_itself()
    {
        var tree = new BookmarkTree();
        var outer = tree.NewFolder("Outer");
        var inner = tree.NewFolder("Inner", outer);
        Assert.False(tree.Move(outer, inner, 0));
        Assert.False(tree.Move(outer, outer, 0));
        Assert.Same(outer, tree.ParentOf(inner));
        Assert.Contains(outer, tree.Root);
    }

    [Fact]
    public void Search_finds_every_word_in_the_title_or_address_inside_folders_too()
    {
        var tree = new BookmarkTree();
        var work = tree.NewFolder("Work");
        var jira = tree.Add("https://jira.example.com/board", "Sprint board", work);
        tree.Add("https://news.example.com", "News");
        Assert.Equal(new[] { jira }, tree.Search("sprint jira"));
        Assert.Equal(new[] { jira }, tree.Search("BOARD"));
        Assert.Empty(tree.Search("sprint news"));
        Assert.Empty(tree.Search("  "));
    }

    [Fact]
    public void Saved_and_read_back_as_it_was()
    {
        var tree = new BookmarkTree();
        var work = tree.NewFolder("Work");
        tree.Add("https://a.com", "A \"quoted\"", work);
        tree.Add("https://b.com", "B");
        var back = BookmarkTree.FromJson(tree.ToJson());
        Assert.Equal("Work", back.Root[0].Title);
        Assert.Equal("A \"quoted\"", back.Root[0].Children![0].Title);
        Assert.Equal("https://b.com", back.Root[1].Url);
        Assert.NotNull(back.Find("https://a.com"));
        // New ones after reading don't reuse an id.
        var c = back.Add("https://c.com", "C");
        Assert.DoesNotContain(c.Id, back.Root.Take(2).Select(n => n.Id).Append(back.Root[0].Children![0].Id));
    }

    [Fact]
    public void A_broken_file_is_an_empty_tree()
    {
        Assert.Empty(BookmarkTree.FromJson("{ not json").Root);
        Assert.Empty(BookmarkTree.FromJson("").Root);
    }

    // ───────────── Folders first, and dragging ─────────────

    [Fact]
    public void Folders_come_before_pages_however_they_were_added()
    {
        var tree = new BookmarkTree();
        var a = tree.Add("https://a.com", "A");
        var work = tree.NewFolder("Work");
        var b = tree.Add("https://b.com", "B");
        var home = tree.NewFolder("Home");
        Assert.Equal(new[] { work, home, a, b }, tree.Root);
    }

    [Fact]
    public void A_page_moved_among_the_folders_stays_below_them()
    {
        var tree = new BookmarkTree();
        var work = tree.NewFolder("Work");
        var a = tree.Add("https://a.com", "A");
        tree.Move(a, null, 0);
        Assert.Equal(new[] { work, a }, tree.Root);
    }

    [Fact]
    public void A_folder_moved_among_the_pages_stays_above_them()
    {
        var tree = new BookmarkTree();
        var work = tree.NewFolder("Work");
        var a = tree.Add("https://a.com", "A");
        var b = tree.Add("https://b.com", "B");
        tree.Move(work, null, 3);
        Assert.Equal(new[] { work, a, b }, tree.Root);
    }

    [Fact]
    public void Dropped_before_or_after_a_bookmark_goes_beside_it()
    {
        var tree = new BookmarkTree();
        var a = tree.Add("https://a.com", "A");
        var b = tree.Add("https://b.com", "B");
        var c = tree.Add("https://c.com", "C");
        Assert.True(tree.Drop(c, a, BookmarkDrop.Before));
        Assert.Equal(new[] { c, a, b }, tree.Root);
        Assert.True(tree.Drop(c, b, BookmarkDrop.After));
        Assert.Equal(new[] { a, b, c }, tree.Root);
    }

    [Fact]
    public void Dropped_beside_one_in_a_folder_goes_into_that_folder()
    {
        var tree = new BookmarkTree();
        var work = tree.NewFolder("Work");
        var inside = tree.Add("https://in.com", "In", work);
        var a = tree.Add("https://a.com", "A");
        Assert.True(tree.Drop(a, inside, BookmarkDrop.After));
        Assert.Equal(new[] { inside, a }, work.Children);
        Assert.Equal(new[] { work }, tree.Root);
    }

    [Fact]
    public void Dropped_on_a_folder_goes_in_at_the_end_of_its_kind()
    {
        var tree = new BookmarkTree();
        var work = tree.NewFolder("Work");
        var sub = tree.NewFolder("Sub", work);
        var inside = tree.Add("https://in.com", "In", work);
        var a = tree.Add("https://a.com", "A");
        var other = tree.NewFolder("Other");
        Assert.True(tree.Drop(a, work, BookmarkDrop.Into));
        Assert.Equal(new[] { sub, inside, a }, work.Children);
        Assert.True(tree.Drop(other, work, BookmarkDrop.Into));
        Assert.Equal(new[] { sub, other, inside, a }, work.Children);
    }

    [Fact]
    public void Not_onto_itself_or_into_its_own_folder()
    {
        var tree = new BookmarkTree();
        var outer = tree.NewFolder("Outer");
        var inner = tree.NewFolder("Inner", outer);
        var a = tree.Add("https://a.com", "A");
        Assert.False(tree.Drop(a, a, BookmarkDrop.Before));
        Assert.False(tree.Drop(outer, inner, BookmarkDrop.Into));
        Assert.False(tree.Drop(outer, inner, BookmarkDrop.After));
        Assert.False(tree.Drop(a, a, BookmarkDrop.Into));
    }

    [Fact]
    public void A_file_written_out_of_order_is_put_right()
    {
        var tree = BookmarkTree.FromJson("""[{"Id":"1","Title":"A","Url":"https://a.com"},{"Id":"2","Title":"Work","Children":[]}]""");
        Assert.True(tree.Root[0].IsFolder);
    }
    [Fact]
    public void A_deleted_bookmark_goes_back_where_it_was()
    {
        var tree = new BookmarkTree();
        var work = tree.NewFolder("Work");
        var a = tree.Add("https://a.com", "A", work);
        var b = tree.Add("https://b.com", "B", work);
        var c = tree.Add("https://c.com", "C", work);
        var place = tree.Take(b)!;
        Assert.Equal(new[] { a, c }, work.Children);
        tree.PutBack(b, place);
        Assert.Equal(new[] { a, b, c }, work.Children);
    }

    [Fact]
    public void A_deleted_folder_goes_back_with_what_was_in_it()
    {
        var tree = new BookmarkTree();
        var first = tree.NewFolder("First");
        var work = tree.NewFolder("Work");
        var inside = tree.Add("https://in.com", "In", work);
        var place = tree.Take(work)!;
        Assert.Null(tree.Find("https://in.com"));
        tree.PutBack(work, place);
        Assert.Equal(new[] { first, work }, tree.Root);
        Assert.Same(inside, tree.Find("https://in.com"));
    }

    [Fact]
    public void Put_back_at_the_top_if_its_folder_has_gone()
    {
        var tree = new BookmarkTree();
        var work = tree.NewFolder("Work");
        var a = tree.Add("https://a.com", "A", work);
        var place = tree.Take(a)!;
        tree.Remove(work);
        tree.PutBack(a, place);
        Assert.Equal(new[] { a }, tree.Root);
    }

    [Fact]
    public void Nothing_taken_that_isnt_there()
    {
        var tree = new BookmarkTree();
        var a = tree.Add("https://a.com", "A");
        tree.Remove(a);
        Assert.Null(tree.Take(a));
    }
}
