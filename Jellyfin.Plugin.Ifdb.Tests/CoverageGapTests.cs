using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Ifdb.Api;
using Jellyfin.Plugin.Ifdb.Configuration;
using Jellyfin.Plugin.Ifdb.Matching;
using Jellyfin.Plugin.Ifdb.Providers;
using Jellyfin.Plugin.Ifdb.Tests.Support;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.Ifdb.Tests;

public class CoverageGapTests
{
    [Theory]
    [InlineData("http://", null)]
    [InlineData("?p=abc", null)]
    [InlineData("///", null)]
    public void OddIdsAreRejected(string input, string? expected)
        => Assert.Equal(expected, IfdbUrls.NormalizeId(input));

    [Theory]
    [InlineData("not a url at all", null)]
    [InlineData("https://www.youtube.com/watch?v", null)]
    [InlineData("https://www.youtube.com/watch?feature=share&v=abc", "https://www.youtube.com/watch?v=abc")]
    [InlineData("https://www.youtube.com/watch?flag&v=abc", "https://www.youtube.com/watch?v=abc")]
    [InlineData("https://www.youtube.com/embed/", null)]
    public void OddYouTubeAddresses(string input, string? expected)
        => Assert.Equal(expected, IfdbUrls.YouTubeWatchUrl(input));

    [Fact]
    public void EmptyStringsScoreAsExpected()
    {
        Assert.Equal(100, TitleScorer.Ratio(string.Empty, string.Empty));
        Assert.Equal(0, TitleScorer.Ratio(string.Empty, "a"));
        Assert.Equal(0, TitleScorer.Ratio("a", string.Empty));
        Assert.Equal(0, TitleScorer.PartialRatio(string.Empty, "abc"));
        Assert.Equal(100, TitleScorer.PartialRatio("the hobbit book edit", "hobbit"));
    }

    [Fact]
    public void NumberedInstallmentsArePenalisedToo()
        => Assert.True(TitleScorer.Score("Alien 3 Assembly Cut", "Alien 4 Assembly Cut") < 90);

    [Fact]
    public void SparseEntriesMapWithoutOptionalFields()
    {
        var entry = new IfdbEntry { Id = "x", Title = "X", Url = "https://fanedit.org/x/" };
        entry.Changes.Add(new IfdbChange { Text = "Unlabelled" });
        var config = new PluginConfiguration { IncludeChangesInOverview = false };

        var movie = IfdbMetadataMapper.ToMovie(entry, config);
        var search = IfdbMetadataMapper.ToRemoteSearchResult(entry);

        Assert.Null(movie.OriginalTitle);
        Assert.Null(movie.Overview);
        Assert.Null(movie.CriticRating);
        Assert.Null(movie.ProductionYear);
        Assert.Null(movie.RunTimeTicks);
        Assert.Null(search.ProductionYear);
        Assert.Null(search.ImageUrl);
        Assert.Equal("Unlabelled", IfdbMetadataMapper.BuildOverview(entry, new PluginConfiguration()));
        Assert.Empty(IfdbMetadataMapper.ToRemoteImages(entry.Images));
    }

    [Fact]
    public void ZeroRuntimeIsIgnored()
    {
        var entry = new IfdbEntry { Id = "x", Title = "X", RuntimeMinutes = 0 };

        Assert.Null(IfdbMetadataMapper.ToMovie(entry, new PluginConfiguration()).RunTimeTicks);
    }

    [Fact]
    public async Task RedirectsAndInformationalResponsesAreErrorsAndNotCached()
    {
        using var t = new TestClient();
        t.Handler.Html("moved", string.Empty, HttpStatusCode.Found);
        t.Handler.Html("odd", string.Empty, (HttpStatusCode)199);

        await Assert.ThrowsAsync<HttpRequestException>(() => t.Client.GetEntryAsync("moved", CancellationToken.None));
        await Assert.ThrowsAsync<HttpRequestException>(() => t.Client.GetEntryAsync("odd", CancellationToken.None));
        Assert.Equal(0, t.State.CachedPageCount);
    }

    [Fact]
    public void NullUserAgentSettingUsesTheDefault()
    {
        using var t = new TestClient(new PluginConfiguration { UserAgent = null! });

        Assert.StartsWith("Jellyfin-IFDB/", t.Client.UserAgent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RedirectedSearchWithoutAReleaseDateHasNoYear()
    {
        using var t = new TestClient();
        t.Handler.Html("search-results", Fixture.Read("entry-h1.html"), finalUrl: "https://fanedit.org/plain-heading-edit/");

        var result = Assert.Single(await t.Client.SearchAsync("Plain Heading Edit", CancellationToken.None));

        Assert.Null(result.Year);
        Assert.Null(result.ImageUrl);
    }

    [Fact]
    public async Task NameParserReturningNothingFallsBackToTheRawName()
    {
        using var t = new TestClient();
        var library = Substitute.For<ILibraryManager>();
        library.ParseName(Arg.Any<string>()).Returns((ItemLookupInfo)null!);
        var provider = new IfdbMovieProvider(t.Client, library, t.Logger, () => t.Config);
        t.Handler.Html("keywords=Hobbit&", Fixture.Read("search.html"));

        Assert.NotEmpty(await provider.GetSearchResults(new MovieInfo { Name = "Hobbit" }, CancellationToken.None));
    }
}
