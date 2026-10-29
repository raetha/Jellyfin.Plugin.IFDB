using System;
using System.Linq;
using Jellyfin.Plugin.Ifdb.Api;
using Xunit;

namespace Jellyfin.Plugin.Ifdb.Tests;

public class IfdbParserTests
{
    private static readonly Uri SearchUrl = new("https://fanedit.org/fanedit-search/search-results/?query=all&scope=title&keywords=hobbit");
    private static readonly Uri EntryUrl = new("https://fanedit.org/m4s-the-hobbit-book-edit/");

    [Fact]
    public void ParsesSearchListings()
    {
        var results = IfdbParser.ParseSearchResults(Fixture.Read("search.html"), SearchUrl);

        Assert.Equal(2, results.Count);

        var first = results[0];
        Assert.Equal("m4s-the-hobbit-book-edit", first.Id);
        Assert.Equal("https://fanedit.org/m4s-the-hobbit-book-edit/", first.Url);
        Assert.Equal("M4’s The Hobbit Book Edit", first.Title);
        Assert.Equal(2015, first.Year);
        Assert.Equal("https://fanedit.org/wp-content/uploads/2024/05/hobbit-thumb-150x220.jpg", first.ImageUrl);
        Assert.Equal("The whole trilogy cut down to one film.", first.Overview);

        var second = results[1];
        Assert.Equal("the-hobbit-extended-tolkien-edit", second.Id);
        Assert.Equal(2019, second.Year);
        Assert.Null(second.ImageUrl);
    }

    [Fact]
    public void ParsesLegacyCheckboxListings()
    {
        var results = IfdbParser.ParseSearchResults(Fixture.Read("search-legacy.html"), SearchUrl);

        var result = Assert.Single(results);
        Assert.Equal("m4s-the-hobbit-book-edit", result.Id);
        Assert.Equal("M4's The Hobbit Book Edit", result.Title);
        Assert.Equal("https://fanedit.org/wp-content/uploads/thumb.jpg", result.ImageUrl);
    }

    [Fact]
    public void EmptyPageHasNoResults()
        => Assert.Empty(IfdbParser.ParseSearchResults("<html><body></body></html>", SearchUrl));

    [Fact]
    public void DetectsChallengePages()
    {
        Assert.True(IfdbParser.IsChallengePage(Fixture.Read("challenge.html")));

        // Real pages load Cloudflare's challenge-platform script too; that alone isn't a block.
        Assert.False(IfdbParser.IsChallengePage(Fixture.Read("search.html")));
        Assert.False(IfdbParser.IsChallengePage(Fixture.Read("entry.html")));
    }

    [Fact]
    public void ParsesEntryFields()
    {
        var entry = IfdbParser.ParseEntry(Fixture.Read("entry.html"), EntryUrl);

        Assert.NotNull(entry);
        Assert.Equal("m4s-the-hobbit-book-edit", entry.Id);
        Assert.Equal("M4’s The Hobbit Book Edit", entry.Title);

        // The sidebar's "Someone Else" is outside #primary and must not be picked up.
        Assert.Equal(new[] { "M4" }, entry.Faneditors);
        Assert.Equal(3, entry.OriginalTitles.Count);
        Assert.Equal("The Hobbit: An Unexpected Journey", entry.OriginalTitles[0]);
        Assert.Equal(new[] { "Adventure", "Fantasy" }, entry.Genres);
        Assert.Equal(new[] { "Shortcut" }, entry.FaneditTypes);
        Assert.Equal(new[] { "Middle-earth" }, entry.Franchises);
        Assert.Equal(2015, entry.ReleaseDate?.Year);
        Assert.Equal(245, entry.RuntimeMinutes);
        Assert.Equal("tt0903624", entry.ImdbId);
        Assert.Equal("A tighter, book-faithful Hobbit in one sitting.", entry.Synopsis);
    }

    [Fact]
    public void RatingsWithoutReviewsAreIgnored()
    {
        var entry = IfdbParser.ParseEntry(Fixture.Read("entry.html"), EntryUrl);

        Assert.NotNull(entry);
        Assert.Equal(9.4f, entry.TrustedReviewerRating);
        Assert.Null(entry.UserRating);
    }

    [Fact]
    public void ParsesChangesKeepingLineBreaks()
    {
        var entry = IfdbParser.ParseEntry(Fixture.Read("entry.html"), EntryUrl);

        Assert.NotNull(entry);
        Assert.Equal(2, entry.Changes.Count);
        Assert.Equal("Editing Details", entry.Changes[0].Label);
        Assert.Equal("Removed Legolas\nRemoved Tauriel & Alfrid", entry.Changes[0].Text);
        Assert.Equal("Cuts and Additions", entry.Changes[1].Label);
        Assert.Equal("Trimmed the barrel chase.\n\nRemoved the Dol Guldur subplot.", entry.Changes[1].Text);
    }

    [Fact]
    public void ParsesImagesWithoutDuplicates()
    {
        var entry = IfdbParser.ParseEntry(Fixture.Read("entry.html"), EntryUrl);

        Assert.NotNull(entry);
        Assert.Equal(2, entry.Images.Count);

        var main = entry.Images[0];
        Assert.True(main.IsMain);
        Assert.Equal("https://ifdb.fanedit.org/wp-content/uploads/2024/05/Nov-2021-FEOTM-837x390-1.jpg", main.Url);
        Assert.Equal(837, main.Width);
        Assert.Equal(390, main.Height);
        Assert.True(main.IsLandscape);

        var cover = entry.Images[1];
        Assert.Equal("https://fanedit.org/wp-content/uploads/2016/01/hobbit-cover.jpg", cover.Url);
        Assert.False(cover.IsLandscape);
    }

    [Fact]
    public void ParsesYouTubeTrailers()
    {
        var entry = IfdbParser.ParseEntry(Fixture.Read("entry.html"), EntryUrl);

        Assert.NotNull(entry);
        Assert.Equal(
            new[] { "https://www.youtube.com/watch?v=abc123XYZ", "https://www.youtube.com/watch?v=def456" },
            entry.TrailerUrls);
    }

    [Fact]
    public void NonEntryPagesReturnNull()
    {
        Assert.Null(IfdbParser.ParseEntry(Fixture.Read("not-entry.html"), EntryUrl));
        Assert.Null(IfdbParser.ParseEntry(Fixture.Read("challenge.html"), EntryUrl));
    }

    [Theory]
    [InlineData("July 2025", 2025, 7)]
    [InlineData("Jul 2025", 2025, 7)]
    [InlineData("Aug. 2022", 2022, 8)]
    [InlineData("Sept 2019", 2019, 9)]
    [InlineData("2008", 2008, 1)]
    [InlineData("2012 / 2013 / 2014", 2014, 1)]
    [InlineData("Released in 2010 (v2)", 2010, 1)]
    public void ParsesLooseDates(string text, int year, int month)
    {
        var date = IfdbParser.ParseDate(text);

        Assert.NotNull(date);
        Assert.Equal(year, date.Value.Year);
        Assert.Equal(month, date.Value.Month);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    public void UnparseableDatesAreNull(string text)
        => Assert.Null(IfdbParser.ParseDate(text));

    [Fact]
    public void PageTitleIsReadForDiagnostics()
        => Assert.Equal("Just a moment...", IfdbParser.PageTitle(Fixture.Read("challenge.html")));
}
