using System;
using Jellyfin.Plugin.Ifdb.Api;
using Jellyfin.Plugin.Ifdb.Configuration;
using Jellyfin.Plugin.Ifdb.Matching;
using Jellyfin.Plugin.Ifdb.Providers;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.Ifdb.Tests;

/// <summary>
/// The other side of conditions the main tests only exercise one way.
/// </summary>
public class BranchCoverageTests
{
    private static readonly Uri PageUrl = new("https://fanedit.org/edge-case-edit/");

    private static IfdbEntry Parse(string body)
        => IfdbParser.ParseEntry(
            "<html><body><div id=\"primary\"><h1><span itemprop=\"headline\">X</span></h1>"
            + "<div class=\"jrGenre\"><div class=\"jrFieldValue\">Drama</div></div>"
            + body + "</div></body></html>",
            PageUrl)!;

    [Fact]
    public void ImdbIdIsntStoredWhenTheEntryHasNone()
    {
        var entry = new IfdbEntry { Id = "x", Title = "X", Url = "https://fanedit.org/x/" };

        var movie = IfdbMetadataMapper.ToMovie(entry, new PluginConfiguration { SetImdbId = true });

        Assert.Null(movie.GetProviderId(MetadataProvider.Imdb));
    }

    [Fact]
    public void AdditionalLinksWithoutAnImdbLinkLeaveTheIdEmpty()
        => Assert.Null(Parse("<div class=\"jrAdditionallinks\"><div class=\"jrFieldValue\"><a href=\"https://example.com/\">Site</a></div></div>").ImdbId);

    [Fact]
    public void RatingsOutsideTheMainAreaAreUsedWhenItHasNone()
    {
        var entry = IfdbParser.ParseEntry(
            """
            <html><body>
            <div id="primary"><h1><span itemprop="headline">X</span></h1>
            <div class="jrGenre"><div class="jrFieldValue">Drama</div></div></div>
            <div class="jrOverallRatings"><div class="jrOverallUser">
              <span class="jrRatingValue"><span>8.5</span></span><span class="count">3</span></div></div>
            </body></html>
            """,
            PageUrl)!;

        Assert.Equal(8.5f, entry.UserRating);
    }

    [Fact]
    public void NoRatingsAnywhereLeavesThemEmpty()
    {
        var entry = Parse(string.Empty);

        Assert.Null(entry.UserRating);
        Assert.Null(entry.TrustedReviewerRating);
    }

    [Fact]
    public void RatingWithZeroReviewsIsIgnored()
        => Assert.Null(Parse("<div class=\"jrOverallRatings\"><div class=\"jrOverallUser\"><span class=\"jrRatingValue\"><span>7.0</span></span><span class=\"count\">0</span></div></div>").UserRating);

    [Theory]
    [InlineData("/", null)]
    [InlineData("2001/12", 2001)]
    [InlineData("1999/2001", 2001)]
    public void SlashedDates(string text, int? year)
        => Assert.Equal(year, IfdbParser.ParseDate(text)?.Year);

    [Fact]
    public void InstallmentNumbersOnOnlyOneSideArentPenalised()
    {
        Assert.True(TitleScorer.Score("Alien 3 Assembly Cut", "Alien Assembly Cut") >= TitleScorer.Score("Alien 3 Assembly Cut", "Alien 4 Assembly Cut"));
        Assert.True(TitleScorer.Score("Alien Assembly Cut", "Alien 3 Assembly Cut") >= 80);
    }

    [Theory]
    [InlineData("http://fanedit.org/", "http://fanedit.org")]
    [InlineData("https://[bad", "https://fanedit.org")]
    public void BaseUrlSchemesAndBadAddresses(string input, string expected)
        => Assert.Equal(expected, IfdbUrls.NormalizeBaseUrl(input));

    [Theory]
    [InlineData("www.fanedit.org/m4s-the-hobbit-book-edit/", "m4s-the-hobbit-book-edit")]
    [InlineData("ifdb.fanedit.org/m4s-the-hobbit-book-edit/", "m4s-the-hobbit-book-edit")]
    public void SchemelessSiteAddressesAreIds(string input, string? expected)
        => Assert.Equal(expected, IfdbUrls.NormalizeId(input));
}
