using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Ifdb.Configuration;
using Jellyfin.Plugin.Ifdb.Providers;
using Jellyfin.Plugin.Ifdb.Tests.Support;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.Ifdb.Tests;

public class ProviderTests
{
    private const string EntryUrl = "https://fanedit.org/m4s-the-hobbit-book-edit/";

    private static (TestClient Test, IfdbMovieProvider Provider) MovieProvider(PluginConfiguration? config = null, string? parsedName = "")
    {
        var test = new TestClient(config);
        var library = Substitute.For<ILibraryManager>();
        library.ParseName(Arg.Any<string>()).Returns(call => new ItemLookupInfo
        {
            // Mimic Jellyfin stripping the year; null simulates a parser that finds no name.
            Name = parsedName is null ? null! : call.Arg<string>().Replace(" (2015)", string.Empty, StringComparison.Ordinal),
        });
        return (test, new IfdbMovieProvider(test.Client, library, test.Logger, () => test.Config));
    }

    private static MovieInfo WithId(string id)
    {
        var info = new MovieInfo { Name = "Anything" };
        info.SetProviderId(Constants.ProviderIdKey, id);
        return info;
    }

    [Fact]
    public async Task SearchByIdLooksTheEntryUpDirectly()
    {
        var (t, provider) = MovieProvider();
        using var _ = t;
        t.Handler.Html(EntryUrl, Fixture.Read("entry.html"));

        var results = (await provider.GetSearchResults(WithId(EntryUrl), CancellationToken.None)).ToList();

        var result = Assert.Single(results);
        Assert.Equal("M4\u2019s The Hobbit Book Edit", result.Name);
        Assert.Equal("m4s-the-hobbit-book-edit", result.GetProviderId(Constants.ProviderIdKey));
        Assert.Equal(Constants.PluginName, result.SearchProviderName);
    }

    [Fact]
    public async Task SearchByUnknownIdIsEmpty()
    {
        var (t, provider) = MovieProvider();
        using var _ = t;

        Assert.Empty(await provider.GetSearchResults(WithId("no-such-edit"), CancellationToken.None));
    }

    [Fact]
    public async Task SearchByTitleRanksResults()
    {
        var (t, provider) = MovieProvider();
        using var _ = t;
        t.Handler.Html("keywords=Hobbit%20Book%20Edit&", Fixture.Read("search.html"));

        var results = (await provider.GetSearchResults(new MovieInfo { Name = "Hobbit Book Edit (2015)" }, CancellationToken.None)).ToList();

        Assert.Equal("m4s-the-hobbit-book-edit", results[0].GetProviderId(Constants.ProviderIdKey));
        Assert.Equal(2015, results[0].ProductionYear);
    }

    [Fact]
    public async Task UnparseableNamesAreSearchedAsGiven()
    {
        var (t, provider) = MovieProvider(parsedName: null);
        using var _ = t;
        t.Handler.Html("keywords=Hobbit%20Book%20Edit&", Fixture.Read("search.html"));

        Assert.NotEmpty(await provider.GetSearchResults(new MovieInfo { Name = "  Hobbit Book Edit  " }, CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task BlankNamesAreNotSearched(string? name)
    {
        var (t, provider) = MovieProvider();
        using var _ = t;

        Assert.Empty(await provider.GetSearchResults(new MovieInfo { Name = name! }, CancellationToken.None));
        Assert.False((await provider.GetMetadata(new MovieInfo { Name = name! }, CancellationToken.None)).HasMetadata);
        Assert.Equal(0, t.Handler.RequestCount);
    }

    [Fact]
    public async Task SearchFailuresAreLoggedNotThrown()
    {
        var (t, provider) = MovieProvider();
        using var _ = t;
        t.Handler.Html("search-results", "oops", HttpStatusCode.InternalServerError);

        Assert.Empty(await provider.GetSearchResults(new MovieInfo { Name = "x" }, CancellationToken.None));
        Assert.True(t.Logger.Has(LogLevel.Error, "searching for \"x\""));
    }

    [Fact]
    public async Task CancellationIsNotSwallowed()
    {
        var (t, provider) = MovieProvider();
        using var _ = t;
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetSearchResults(new MovieInfo { Name = "x" }, cancel.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetMetadata(new MovieInfo { Name = "x" }, cancel.Token));
    }

    [Fact]
    public async Task MetadataByIdCreditsFanEditorsAsDirectors()
    {
        var (t, provider) = MovieProvider();
        using var _ = t;
        t.Handler.Html(EntryUrl, Fixture.Read("entry.html"));

        var result = await provider.GetMetadata(WithId("m4s-the-hobbit-book-edit"), CancellationToken.None);

        Assert.True(result.HasMetadata);
        Assert.Equal("en", result.ResultLanguage);
        Assert.Equal("M4\u2019s The Hobbit Book Edit", result.Item.Name);
        var person = Assert.Single(result.People);
        Assert.Equal("M4", person.Name);
        Assert.Equal(PersonKind.Director, person.Type);
        Assert.Equal(Constants.FaneditorRole, person.Role);
    }

    [Fact]
    public async Task FanEditorsCanBeCreditedAsEditors()
    {
        var (t, provider) = MovieProvider(new PluginConfiguration { RequestDelayMilliseconds = 0, FaneditorCredit = FaneditorCredit.Editor });
        using var _ = t;
        t.Handler.Html(EntryUrl, Fixture.Read("entry.html"));

        var result = await provider.GetMetadata(WithId("m4s-the-hobbit-book-edit"), CancellationToken.None);

        Assert.Equal(PersonKind.Editor, Assert.Single(result.People).Type);
    }

    [Fact]
    public async Task UnidentifiedMoviesAreMatchedAutomatically()
    {
        var (t, provider) = MovieProvider();
        using var _ = t;
        t.Handler.Html("keywords=M4%E2%80%99s%20The%20Hobbit%20Book%20Edit&", Fixture.Read("search.html"));
        t.Handler.Html(EntryUrl, Fixture.Read("entry.html"));

        var result = await provider.GetMetadata(new MovieInfo { Name = "M4\u2019s The Hobbit Book Edit (2015)" }, CancellationToken.None);

        Assert.True(result.HasMetadata);
        Assert.Equal("m4s-the-hobbit-book-edit", result.Item.GetProviderId(Constants.ProviderIdKey));
        Assert.True(t.Logger.Has(LogLevel.Information, "matched"));
    }

    [Fact]
    public async Task AmbiguousOrWeakMatchesAreLeftForIdentify()
    {
        var (t, provider) = MovieProvider();
        using var _ = t;
        t.Handler.Html("search-results", Fixture.Read("search.html"));

        var result = await provider.GetMetadata(new MovieInfo { Name = "Hobbit" }, CancellationToken.None);

        Assert.False(result.HasMetadata);
        Assert.True(t.Logger.Has(LogLevel.Information, "no single match"));
    }

    [Fact]
    public async Task MatchLoggingCanBeOff()
    {
        var test = new TestClient(logLevel: LogLevel.Warning);
        using var _ = test;
        var library = Substitute.For<ILibraryManager>();
        library.ParseName(Arg.Any<string>()).Returns(call => new ItemLookupInfo { Name = call.Arg<string>() });
        var provider = new IfdbMovieProvider(test.Client, library, test.Logger, () => test.Config);
        test.Handler.Html("search-results", Fixture.Read("search.html"));

        await provider.GetMetadata(new MovieInfo { Name = "Hobbit" }, CancellationToken.None);

        Assert.Empty(test.Logger.Entries);
    }

    [Fact]
    public async Task MissingEntryGivesNoMetadata()
    {
        var (t, provider) = MovieProvider();
        using var _ = t;

        Assert.False((await provider.GetMetadata(WithId("gone"), CancellationToken.None)).HasMetadata);
    }

    [Fact]
    public async Task MetadataFailuresAreLoggedNotThrown()
    {
        var (t, provider) = MovieProvider();
        using var _ = t;
        t.Handler.Html("gone", "oops", HttpStatusCode.InternalServerError);

        Assert.False((await provider.GetMetadata(WithId("gone"), CancellationToken.None)).HasMetadata);
        Assert.True(t.Logger.Has(LogLevel.Error, "getting metadata"));
    }

    [Fact]
    public async Task ProvidersPassImageDownloadsThroughTheClient()
    {
        var (t, provider) = MovieProvider();
        using var _ = t;
        t.Handler.Bytes("poster.jpg", TestImages.Png, "image/png");
        var images = new IfdbMovieImageProvider(t.Client);

        using var fromMovie = await provider.GetImageResponse("https://fanedit.org/poster.jpg", CancellationToken.None);
        using var fromImages = await images.GetImageResponse("https://fanedit.org/poster.jpg", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, fromMovie.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fromImages.StatusCode);
        Assert.Equal(Constants.PluginName, provider.Name);
    }

    [Fact]
    public void PublicConstructorsWireUpServerClients()
    {
        var factory = Substitute.For<System.Net.Http.IHttpClientFactory>();
        var library = Substitute.For<ILibraryManager>();

        Assert.Equal(Constants.PluginName, new IfdbMovieProvider(factory, library, Substitute.For<ILogger<IfdbMovieProvider>>()).Name);
        Assert.Equal(Constants.PluginName, new IfdbMovieImageProvider(factory, Substitute.For<ILogger<IfdbMovieImageProvider>>()).Name);
    }

    [Fact]
    public async Task ImageProviderSortsEntryImages()
    {
        using var t = new TestClient();
        t.Handler.Html(EntryUrl, Fixture.Read("entry.html"));
        var provider = new IfdbMovieImageProvider(t.Client);
        var movie = new Movie();
        movie.SetProviderId(Constants.ProviderIdKey, "m4s-the-hobbit-book-edit");

        var images = (await provider.GetImages(movie, CancellationToken.None)).ToList();

        Assert.Equal(new[] { ImageType.Backdrop, ImageType.Thumb, ImageType.Primary }, images.Select(i => i.Type));
        Assert.All(images, i => Assert.Equal(Constants.PluginName, i.ProviderName));
    }

    [Fact]
    public async Task ImageProviderNeedsAnIdAndAnEntry()
    {
        using var t = new TestClient();
        var provider = new IfdbMovieImageProvider(t.Client);
        var missing = new Movie();
        missing.SetProviderId(Constants.ProviderIdKey, "gone");

        Assert.Empty(await provider.GetImages(new Movie(), CancellationToken.None));
        Assert.Empty(await provider.GetImages(missing, CancellationToken.None));
    }

    [Fact]
    public async Task ImageProviderFailuresAreLoggedNotThrown()
    {
        using var t = new TestClient();
        t.Handler.Html("broken", "oops", HttpStatusCode.InternalServerError);
        var provider = new IfdbMovieImageProvider(t.Client);
        var movie = new Movie { Name = "Broken" };
        movie.SetProviderId(Constants.ProviderIdKey, "broken");

        Assert.Empty(await provider.GetImages(movie, CancellationToken.None));
        Assert.True(t.Logger.Has(LogLevel.Error, "getting images for \"Broken\""));
    }

    [Fact]
    public void ImageProviderSupportsMoviesOnly()
    {
        using var t = new TestClient();
        var provider = new IfdbMovieImageProvider(t.Client);

        Assert.True(provider.Supports(new Movie()));
        Assert.False(provider.Supports(new Series()));
        Assert.Equal(new[] { ImageType.Primary, ImageType.Backdrop, ImageType.Thumb }, provider.GetSupportedImages(new Movie()));
    }

    [Fact]
    public void ExternalIdAppliesToMovies()
    {
        var externalId = new IfdbExternalId();

        Assert.Equal("IFDB", externalId.ProviderName);
        Assert.Equal("IFDB", externalId.Key);
        Assert.Equal(MediaBrowser.Model.Providers.ExternalIdMediaType.Movie, externalId.Type);
        Assert.True(externalId.Supports(new Movie()));
        Assert.False(externalId.Supports(new Series()));
    }
}
