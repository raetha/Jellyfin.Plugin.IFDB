using System;
using System.Linq;
using Jellyfin.Plugin.Ifdb.Api;
using Xunit;

namespace Jellyfin.Plugin.Ifdb.Tests;

public class ParserEdgeTests
{
    private static readonly Uri PageUrl = new("https://fanedit.org/edge-case-edit/");
    private static readonly Uri SearchUrl = new("https://fanedit.org/fanedit-search/search-results/?keywords=x");

    private static IfdbEntry Edge()
    {
        var entry = IfdbParser.ParseEntry(Fixture.Read("entry-edge.html"), PageUrl);
        Assert.NotNull(entry);
        return entry;
    }

    [Fact]
    public void TitleFallsBackToTheCompareCheckbox()
        => Assert.Equal("Edge & Case Edit", Edge().Title);

    [Fact]
    public void TitleFallsBackToThePlainHeading()
    {
        var entry = IfdbParser.ParseEntry(Fixture.Read("entry-h1.html"), PageUrl);

        Assert.NotNull(entry);
        Assert.Equal("Plain Heading Edit", entry.Title);
        Assert.Equal(new[] { "Someone" }, entry.Faneditors);
        Assert.Empty(entry.Images);
        Assert.Empty(entry.TrailerUrls);
        Assert.Null(entry.TrustedReviewerRating);
        Assert.Null(entry.Synopsis);
    }

    [Fact]
    public void PlainTextAndLinkFieldsAreRead()
    {
        var entry = Edge();

        Assert.Equal(new[] { "Solo Editor" }, entry.Faneditors);
        Assert.Equal(new[] { "Drama", "Crime" }, entry.Genres);
        Assert.Empty(entry.OriginalTitles);
    }

    [Fact]
    public void UnreadableValuesAreLeftEmpty()
    {
        var entry = Edge();

        Assert.Null(entry.TrustedReviewerRating); // count but no number
        Assert.Null(entry.UserRating);            // count but 0.0
        Assert.Null(entry.RuntimeMinutes);
        Assert.Equal(2012, entry.ReleaseDate?.Year);
        Assert.Null(entry.ImdbId);
    }

    [Fact]
    public void SynopsisFallsBackToTheMainAreaAndTrailingBreaksAreDropped()
        => Assert.Equal("Line one\nLine two", Edge().Synopsis);

    [Fact]
    public void ChangeRowsWithoutAValueAreSkippedAndUnlabelledOnesKept()
    {
        var change = Assert.Single(Edge().Changes);

        Assert.Equal(string.Empty, change.Label);
        Assert.Equal("Unlabelled change", change.Text);
    }

    [Fact]
    public void ImagesComeFromEveryKnownPlace()
    {
        var images = Edge().Images;

        Assert.Equal(
            new[]
            {
                "https://fanedit.org/uploads/edge-poster.jpg",
                "https://fanedit.org/uploads/gallery-1.jpg",
                "https://fanedit.org/uploads/cover-front.jpg",
                "https://fanedit.org/uploads/cover-back.png",
                "https://fanedit.org/uploads/cover-spine.webp",
            },
            images.Select(i => i.Url));

        // The main image's own width/height attributes describe it, so they're trusted.
        Assert.True(images[0].IsMain);
        Assert.Equal(400, images[0].Width);
        Assert.Equal(600, images[0].Height);
        Assert.False(images[0].IsLandscape);
        Assert.Null(images[1].Width);
    }

    [Fact]
    public void TrailersAreFoundOutsideTheVideoTabAndDeduplicated()
        => Assert.Equal(
            new[] { "https://www.youtube.com/watch?v=lazy1", "https://www.youtube.com/watch?v=v2" },
            Edge().TrailerUrls);

    [Fact]
    public void PosterSkipsWideImagesAndFallsBackToTheFirst()
    {
        var entry = new IfdbEntry();
        Assert.Null(entry.PosterUrl);

        entry.Images.Add(new IfdbImage { Url = "wide", Width = 800, Height = 300 });
        Assert.Equal("wide", entry.PosterUrl);

        entry.Images.Add(new IfdbImage { Url = "tall", Width = 300, Height = 450 });
        Assert.Equal("tall", entry.PosterUrl);
    }

    [Fact]
    public void ListingsOutsideAPageWrapperStillParse()
    {
        var results = IfdbParser.ParseSearchResults("<div class=\"jrListingTitle\"><a href=\"/bare-edit/\">Bare Edit</a></div>", SearchUrl);

        Assert.Equal("bare-edit", Assert.Single(results).Id);
    }

    [Fact]
    public void ListingsWithoutUsableLinksAreSkipped()
    {
        const string Html = """
            <div><div><div class="jrListingTitle"><a href="javascript:void(0)">Script link</a></div></div></div>
            <div><div><div class="jrListingTitle"><a href="/fanedit-search/search-results/">Search link</a></div></div></div>
            <div><div><div class="jrListingTitle"><a href="/untitled/"> </a></div></div></div>
            """;

        Assert.Empty(IfdbParser.ParseSearchResults(Html, SearchUrl));
    }

    [Fact]
    public void LegacyCheckboxesFallBackToThePostNumber()
    {
        const string Html = """
            <input class="jrCheckListing" value="123" data-listingtitle="Numbered Edit">
            <input class="jrCheckListing" value="123" data-listingtitle="Duplicate">
            <input class="jrCheckListing" value="456" data-listingtitle="">
            <input class="jrCheckListing" data-listingtitle="No id">
            """;

        var result = Assert.Single(IfdbParser.ParseSearchResults(Html, SearchUrl));
        Assert.Equal("123", result.Id);
        Assert.Equal(string.Empty, result.Url);
        Assert.Null(result.ImageUrl);
    }

    [Fact]
    public void PageWithoutATitleHasAnEmptyPageTitle()
        => Assert.Equal(string.Empty, IfdbParser.PageTitle("<html><body></body></html>"));

    [Fact]
    public void BadImageAddressesAreIgnored()
    {
        const string Html = """
            <div id="primary"><h1><span itemprop="headline">X</span></h1>
            <div class="jrGenre"><div class="jrFieldValue">Drama</div></div>
            <div class="jrListingMainImage"><a href="http://[bad/x.jpg"><img src="http://[bad/x.jpg"></a></div>
            </div>
            """;

        var entry = IfdbParser.ParseEntry(Html, PageUrl);

        Assert.NotNull(entry);
        Assert.Empty(entry.Images);
    }

    [Theory]
    [InlineData("2012 / soon", 2012)]
    [InlineData("12 / 13", null)]
    [InlineData("released 1850, v2", null)]
    public void UnusualDates(string text, int? year)
        => Assert.Equal(year, IfdbParser.ParseDate(text)?.Year);

    [Fact]
    public void LeadingNumbersAreRead()
    {
        Assert.Equal(102, IfdbParser.ParseLeadingInt("102 minutes"));
        Assert.Null(IfdbParser.ParseLeadingInt(null));
        Assert.Null(IfdbParser.ParseLeadingInt("99999999999"));
    }

    [Fact]
    public void PagesWithFieldsButNoTitleAreNotEntries()
        => Assert.Null(IfdbParser.ParseEntry("<div id=\"primary\"><div class=\"jrFieldValue\">x</div></div>", PageUrl));

    [Fact]
    public void MainImageLinkWithoutAnImageElementIsUsed()
    {
        const string Html = """
            <div id="primary"><h1><span itemprop="headline">X</span></h1>
            <div class="jrGenre"><div class="jrFieldValue">Drama</div></div>
            <div class="jrListingMainImage"><a href="/uploads/poster-300x450.jpg">View poster</a></div>
            </div>
            """;

        var image = Assert.Single(IfdbParser.ParseEntry(Html, PageUrl)!.Images);

        Assert.Equal("https://fanedit.org/uploads/poster-300x450.jpg", image.Url);
        Assert.Equal(300, image.Width);
        Assert.Equal(450, image.Height);
    }

    [Fact]
    public void RatingWithACountButNoValueIsIgnored()
    {
        const string Html = """
            <div id="primary"><h1><span itemprop="headline">X</span></h1>
            <div class="jrGenre"><div class="jrFieldValue">Drama</div></div>
            <div class="jrOverallRatings"><div class="jrOverallUser"><span class="count">2</span></div></div>
            </div>
            """;

        Assert.Null(IfdbParser.ParseEntry(Html, PageUrl)!.UserRating);
    }
}
