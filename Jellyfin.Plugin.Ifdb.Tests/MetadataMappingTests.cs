using System;
using System.Linq;
using Jellyfin.Plugin.Ifdb.Api;
using Jellyfin.Plugin.Ifdb.Configuration;
using Jellyfin.Plugin.Ifdb.Providers;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.Ifdb.Tests;

public class MetadataMappingTests
{
    private static IfdbEntry Entry()
    {
        var entry = IfdbParser.ParseEntry(Fixture.Read("entry.html"), new Uri("https://fanedit.org/m4s-the-hobbit-book-edit/"));
        Assert.NotNull(entry);
        return entry;
    }

    [Fact]
    public void BuildsMovieFromEntry()
    {
        var movie = IfdbMetadataMapper.ToMovie(Entry(), new PluginConfiguration());

        Assert.Equal("M4’s The Hobbit Book Edit", movie.Name);
        Assert.StartsWith("The Hobbit: An Unexpected Journey, ", movie.OriginalTitle, StringComparison.Ordinal);
        Assert.Equal(2015, movie.ProductionYear);
        Assert.Equal(TimeSpan.FromMinutes(245).Ticks, movie.RunTimeTicks);
        Assert.Equal(new[] { "Adventure", "Fantasy" }, movie.Genres);
        Assert.Equal(new[] { "Shortcut", "Middle-earth" }, movie.Tags);
        Assert.Equal("https://fanedit.org/m4s-the-hobbit-book-edit/", movie.HomePageUrl);
        Assert.Equal("m4s-the-hobbit-book-edit", movie.GetProviderId(Constants.ProviderIdKey));
        Assert.Equal(2, movie.RemoteTrailers.Count);
    }

    [Fact]
    public void TrustedReviewerRatingIsScaledToPercent()
    {
        var movie = IfdbMetadataMapper.ToMovie(Entry(), new PluginConfiguration());

        Assert.Equal(94.0, movie.CriticRating!.Value, 3);
        Assert.Null(movie.CommunityRating);
    }

    [Fact]
    public void ImdbIdIsOnlyStoredWhenEnabled()
    {
        var off = IfdbMetadataMapper.ToMovie(Entry(), new PluginConfiguration());
        var on = IfdbMetadataMapper.ToMovie(Entry(), new PluginConfiguration { SetImdbId = true });

        Assert.Null(off.GetProviderId(MetadataProvider.Imdb));
        Assert.Equal("tt0903624", on.GetProviderId(MetadataProvider.Imdb));
    }

    [Fact]
    public void OverviewIncludesChangesWhenEnabled()
    {
        var with = IfdbMetadataMapper.BuildOverview(Entry(), new PluginConfiguration());
        var without = IfdbMetadataMapper.BuildOverview(Entry(), new PluginConfiguration { IncludeChangesInOverview = false });

        Assert.Equal("A tighter, book-faithful Hobbit in one sitting.", without);
        Assert.NotNull(with);
        Assert.StartsWith(without!, with, StringComparison.Ordinal);
        Assert.Contains("\n\nEditing Details:\nRemoved Legolas", with, StringComparison.Ordinal);
        Assert.Contains("\n\nCuts and Additions:\nTrimmed the barrel chase.", with, StringComparison.Ordinal);
    }

    [Fact]
    public void TagsCanBeTurnedOff()
    {
        var movie = IfdbMetadataMapper.ToMovie(Entry(), new PluginConfiguration { AddFaneditTypeTags = false, AddFranchiseTags = false });

        Assert.Empty(movie.Tags);
    }

    [Fact]
    public void LandscapeImagesBecomeBackdropsAndPortraitsPosters()
    {
        var images = IfdbMetadataMapper.ToRemoteImages(Entry().Images);

        Assert.Equal(
            new[] { ImageType.Backdrop, ImageType.Thumb, ImageType.Primary },
            images.Select(i => i.Type).ToArray());
        Assert.Equal("https://fanedit.org/wp-content/uploads/2016/01/hobbit-cover.jpg", images[2].Url);
    }

    [Fact]
    public void LandscapeOnlyEntryStillGetsAPoster()
    {
        var images = IfdbMetadataMapper.ToRemoteImages(
            [new IfdbImage { Url = "https://fanedit.org/banner-837x390.jpg", Width = 837, Height = 390, IsMain = true }]);

        Assert.Contains(images, i => i.Type == ImageType.Primary);
    }
}
