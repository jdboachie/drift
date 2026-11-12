using Drift;

public class SiteSearchTests
{
    private static readonly SiteSearch YouTube = new("YouTube", "youtube.com", "https://www.youtube.com/results?search_query={searchTerms}");
    private static readonly SiteSearch GitHub = new("GitHub", "github.com", "https://github.com/search?q={searchTerms}");
    private static readonly SiteSearch Google = new("Google", "google.com", "https://www.google.com/search?q={searchTerms}");
    private static readonly SiteSearch[] Sites = [YouTube, GitHub, Google];

    private static SiteSearch? Match(string typed, Func<string, double>? visits = null) =>
        SiteSearches.Match(typed, Sites, visits ?? (_ => 0));

    [Fact]
    public void Typing_a_site_offers_its_search()
    {
        Assert.Equal(YouTube, Match("youtube.com"));
        Assert.Equal(YouTube, Match("yout"));
        Assert.Equal(YouTube, Match("www.youtube.com"));
        Assert.Equal(YouTube, Match("https://youtube.com/"));
        Assert.Equal(YouTube, Match("YouTube"));
    }

    [Fact]
    public void Not_from_one_letter_or_from_a_search()
    {
        Assert.Null(Match("y"));
        Assert.Null(Match("youtube cats"));
        Assert.Null(Match("youtube.com/watch?v=1"));
        Assert.Null(Match("bing"));
    }

    [Fact]
    public void Of_several_the_one_you_visit_most()
    {
        Assert.Equal(Google, Match("go"));
        var many = new[] { new SiteSearch("Gitea", "gitea.com", "https://gitea.com/explore?q={searchTerms}"), GitHub };
        Assert.Equal(GitHub, SiteSearches.Match("git", many, host => host == "github.com" ? 9 : 1));
        Assert.Equal("gitea.com", SiteSearches.Match("git", many, host => host == "gitea.com" ? 9 : 1)!.Host);
    }

    [Fact]
    public void Of_several_never_visited_the_shortest()
    {
        var both = new[] { new SiteSearch("Amazon UK", "amazon.co.uk", "https://www.amazon.co.uk/s?k={searchTerms}"),
                           new SiteSearch("Amazon", "amazon.com", "https://www.amazon.com/s?k={searchTerms}") };
        Assert.Equal("amazon.com", SiteSearches.Match("amaz", both, _ => 0)!.Host);
    }

    [Fact]
    public void The_search_address_has_the_query_in_it()
    {
        Assert.Equal("https://www.youtube.com/results?search_query=lo-fi%20beats%20%26%20rain",
            YouTube.UrlFor("lo-fi beats & rain").AbsoluteUri);
    }

    [Fact]
    public void Nothing_to_search_for_goes_to_the_site()
    {
        Assert.Equal("https://youtube.com/", YouTube.UrlFor("  ").AbsoluteUri);
    }

    [Fact]
    public void An_OpenSearch_description_gives_the_name_and_search_address()
    {
        var xml = """
            <?xml version="1.0"?>
            <OpenSearchDescription xmlns="http://a9.com/-/spec/opensearch/1.1/">
              <ShortName>MDN</ShortName>
              <Url type="application/x-suggestions+json" template="https://developer.mozilla.org/api/suggest?q={searchTerms}"/>
              <Url type="text/html" method="get" template="https://developer.mozilla.org/en-US/search?q={searchTerms}&amp;page={startPage?}"/>
            </OpenSearchDescription>
            """;
        var site = SiteSearches.FromOpenSearch(xml, new Uri("https://developer.mozilla.org/en-US/docs/Web"));
        Assert.NotNull(site);
        Assert.Equal("MDN", site!.Name);
        Assert.Equal("developer.mozilla.org", site.Host);
        Assert.Equal("https://developer.mozilla.org/en-US/search?q=css&page=", site.UrlFor("css").AbsoluteUri);
    }

    [Fact]
    public void A_description_that_searches_somewhere_else_is_ignored()
    {
        var xml = """<OpenSearchDescription xmlns="http://a9.com/-/spec/opensearch/1.1/"><ShortName>X</ShortName><Url type="text/html" template="https://evil.example/s?q={searchTerms}"/></OpenSearchDescription>""";
        Assert.Null(SiteSearches.FromOpenSearch(xml, new Uri("https://www.example.org/")));
    }

    [Fact]
    public void A_description_without_a_web_page_search_or_broken_is_ignored()
    {
        Assert.Null(SiteSearches.FromOpenSearch("""<OpenSearchDescription xmlns="http://a9.com/-/spec/opensearch/1.1/"><ShortName>X</ShortName></OpenSearchDescription>""", new Uri("https://x.com/")));
        Assert.Null(SiteSearches.FromOpenSearch("<not xml", new Uri("https://x.com/")));
        Assert.Null(SiteSearches.FromOpenSearch("""<OpenSearchDescription xmlns="http://a9.com/-/spec/opensearch/1.1/"><ShortName>X</ShortName><Url type="text/html" template="https://x.com/search"/></OpenSearchDescription>""", new Uri("https://x.com/")));
    }

    [Fact]
    public void A_subdomain_of_the_page_counts_as_the_same_site()
    {
        var xml = """<OpenSearchDescription xmlns="http://a9.com/-/spec/opensearch/1.1/"><ShortName>Docs</ShortName><Url type="text/html" template="https://search.example.org/?q={searchTerms}"/></OpenSearchDescription>""";
        Assert.Equal("example.org", SiteSearches.FromOpenSearch(xml, new Uri("https://www.example.org/"))!.Host);
    }

    [Fact]
    public void Learned_sites_add_to_the_built_in_ones_without_replacing_them()
    {
        var learned = new[] { new SiteSearch("YT", "youtube.com", "https://example.com/{searchTerms}"), new SiteSearch("MDN", "developer.mozilla.org", "https://developer.mozilla.org/search?q={searchTerms}") };
        var all = SiteSearches.Combine(SiteSearches.BuiltIn, learned);
        Assert.Equal("YouTube", all.Single(s => s.Host == "youtube.com").Name);
        Assert.Contains(all, s => s.Host == "developer.mozilla.org");
    }

    [Fact]
    public void The_built_in_ones_all_work()
    {
        Assert.Contains(SiteSearches.BuiltIn, s => s.Host == "youtube.com");
        foreach (var site in SiteSearches.BuiltIn)
        {
            Assert.Contains("{searchTerms}", site.Template);
            Assert.Equal("https", site.UrlFor("x").Scheme);
        }
    }

    // ───────────── "@": picking a site to search by name ─────────────

    [Fact]
    public void An_at_sign_alone_lists_every_site_most_visited_first()
    {
        var list = SiteSearches.Filter("", Sites, host => host == "github.com" ? 5 : 0);
        Assert.Equal(3, list.Count);
        Assert.Equal(GitHub, list[0]);
    }

    [Fact]
    public void What_follows_it_narrows_the_list_by_name_or_address()
    {
        Assert.Equal(new[] { YouTube }, SiteSearches.Filter("you", Sites, _ => 0));
        Assert.Equal(new[] { GitHub }, SiteSearches.Filter("github.com", Sites, _ => 0));
        Assert.Empty(SiteSearches.Filter("bing", Sites, _ => 0));
    }

    [Fact]
    public void A_space_after_the_site_starts_searching_it()
    {
        var (site, query) = SiteSearches.ParseAt("@youtube lo-fi beats", Sites, _ => 0)!.Value;
        Assert.Equal(YouTube, site);
        Assert.Equal("lo-fi beats", query);
        // Just the space: into its search, nothing typed yet.
        var (short_, rest) = SiteSearches.ParseAt("@yt ", [YouTube with { Name = "YT" }, GitHub], _ => 0)!.Value;
        Assert.Equal("youtube.com", short_.Host);
        Assert.Equal("", rest);
    }

    [Fact]
    public void Not_until_the_space_and_not_for_a_site_it_does_not_know()
    {
        Assert.Null(SiteSearches.ParseAt("@youtube", Sites, _ => 0));
        Assert.Null(SiteSearches.ParseAt("@bing cats", Sites, _ => 0));
        Assert.Null(SiteSearches.ParseAt("youtube cats", Sites, _ => 0));
        Assert.Null(SiteSearches.ParseAt("@ cats", Sites, _ => 0));
    }
}
