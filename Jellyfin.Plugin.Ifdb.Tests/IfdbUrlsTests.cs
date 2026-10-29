using System;
using Jellyfin.Plugin.Ifdb.Api;
using Jellyfin.Plugin.Ifdb.Configuration;
using Xunit;

namespace Jellyfin.Plugin.Ifdb.Tests;

public class IfdbUrlsTests
{
    [Theory]
    [InlineData("m4s-the-hobbit-book-edit", "m4s-the-hobbit-book-edit")]
    [InlineData("/m4s-the-hobbit-book-edit/", "m4s-the-hobbit-book-edit")]
    [InlineData("17071", "17071")]
    [InlineData("?p=17071", "17071")]
    [InlineData("https://fanedit.org/m4s-the-hobbit-book-edit/", "m4s-the-hobbit-book-edit")]
    [InlineData("https://ifdb.fanedit.org/m4s-the-hobbit-book-edit/reviews/", "m4s-the-hobbit-book-edit/reviews")]
    [InlineData("https://ifdb.fanedit.org/?p=17071", "17071")]
    [InlineData("fanedit.org/m4s-the-hobbit-book-edit", "m4s-the-hobbit-book-edit")]
    [InlineData("https://fanedit.org/fanedit-search/search-results/?keywords=x", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void NormalizesIds(string? input, string? expected)
        => Assert.Equal(expected, IfdbUrls.NormalizeId(input));

    [Theory]
    [InlineData("m4s-the-hobbit-book-edit", "https://fanedit.org/m4s-the-hobbit-book-edit/")]
    [InlineData("17071", "https://fanedit.org/?p=17071")]
    public void BuildsEntryUrls(string id, string expected)
        => Assert.Equal(expected, IfdbUrls.BuildEntryUrl("https://fanedit.org/", id));

    [Theory]
    [InlineData(null, "https://fanedit.org")]
    [InlineData("", "https://fanedit.org")]
    [InlineData("ifdb.fanedit.org", "https://ifdb.fanedit.org")]
    [InlineData("https://ifdb.fanedit.org/some/path", "https://ifdb.fanedit.org")]
    [InlineData("ftp://fanedit.org", "https://fanedit.org")]
    public void NormalizesBaseUrls(string? input, string expected)
        => Assert.Equal(expected, IfdbUrls.NormalizeBaseUrl(input));

    [Fact]
    public void BuildsSearchUrls()
    {
        var url = IfdbUrls.BuildSearchUrl("https://fanedit.org", "M4's Hobbit & more", SearchMatchType.Any);

        Assert.Equal(
            "https://fanedit.org/fanedit-search/search-results/?query=any&scope=title&keywords=M4%27s%20Hobbit%20%26%20more&order=rdate",
            url);
    }

    [Fact]
    public void DistinguishesSearchPagesFromRedirectedEntries()
    {
        Assert.True(IfdbUrls.IsSearchUrl(new Uri("https://fanedit.org/fanedit-search/search-results/?keywords=x")));
        Assert.False(IfdbUrls.IsSearchUrl(new Uri("https://fanedit.org/m4s-the-hobbit-book-edit/")));
    }

    [Theory]
    [InlineData("https://www.youtube.com/embed/abc123?feature=oembed", "https://www.youtube.com/watch?v=abc123")]
    [InlineData("//www.youtube-nocookie.com/embed/def456", "https://www.youtube.com/watch?v=def456")]
    [InlineData("https://youtu.be/ghi789", "https://www.youtube.com/watch?v=ghi789")]
    [InlineData("https://www.youtube.com/watch?v=jkl012&t=4", "https://www.youtube.com/watch?v=jkl012")]
    [InlineData("https://www.youtube.com/embed/videoseries?list=PL1", null)]
    public void ConvertsYouTubeUrls(string input, string? expected)
        => Assert.Equal(expected, IfdbUrls.YouTubeWatchUrl(input));
}
