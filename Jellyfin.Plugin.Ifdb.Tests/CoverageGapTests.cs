using System;
using System.Linq;
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

    [Theory]
    [InlineData("https://fanedit.org/x.jpg", true)]
    [InlineData("http://i.imgur.com/x.jpg", true)]
    [InlineData("https://fanedit.org./x.jpg", true)]
    [InlineData("ftp://fanedit.org/x.jpg", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("http://192.168.1.10/x.jpg", false)]
    [InlineData("http://[::1]/x.jpg", false)]
    [InlineData("http://localhost/x.jpg", false)]
    [InlineData("http://nas/x.jpg", false)]
    [InlineData("http://nas.local/x.jpg", false)]
    [InlineData("http://printer.home.arpa/x.jpg", false)]
    public void OnlyPublicWebAddressesAreFetched(string url, bool allowed)
        => Assert.Equal(allowed, IfdbUrls.IsPublicWebUrl(new Uri(url)));

    [Fact]
    public void RelativeAddressesAreNotPublicWebUrls()
        => Assert.False(IfdbUrls.IsPublicWebUrl(new Uri("/x.jpg", UriKind.Relative)));

    [Theory]
    [InlineData("http://192.168.1.10/poster.jpg")]
    [InlineData("not a url")]
    public async Task ImageDownloadsToPrivateAddressesAreRefused(string url)
    {
        using var t = new TestClient();

        await Assert.ThrowsAsync<ArgumentException>(() => t.Client.GetImageResponseAsync(url, CancellationToken.None));
        Assert.Equal(0, t.Handler.RequestCount);
    }

    [Fact]
    public async Task GoneEntriesAreTreatedAsMissingAndCached()
    {
        using var t = new TestClient();
        t.Handler.Html("removed-edit", "gone", HttpStatusCode.Gone);

        Assert.Null(await t.Client.GetEntryAsync("removed-edit", CancellationToken.None));
        Assert.Equal(1, t.State.CachedPageCount);
    }

    [Fact]
    public async Task OversizedPagesAreRefusedByLength()
    {
        using var t = new TestClient();
        t.Handler.Route(
            uri => uri.AbsoluteUri.Contains("huge", StringComparison.Ordinal),
            (request, _) =>
            {
                var response = FakeHttpHandler.Response(request, "x");
                response.Content.Headers.ContentLength = 6 * 1024 * 1024;
                return Task.FromResult(response);
            });

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => t.Client.GetEntryAsync("huge", CancellationToken.None));
        Assert.Contains("too large", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OversizedPagesAreRefusedWhileReading()
    {
        using var t = new TestClient();
        t.Handler.Route(
            uri => uri.AbsoluteUri.Contains("huge", StringComparison.Ordinal),
            (request, _) =>
            {
                var response = FakeHttpHandler.Response(request, string.Empty);
                response.Content = new StreamContent(new UnknownLengthStream(new byte[(5 * 1024 * 1024) + 1]));
                return Task.FromResult(response);
            });

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => t.Client.GetEntryAsync("huge", CancellationToken.None));
        Assert.Contains("larger than", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PagesWithAByteOrderMarkAreDecoded()
    {
        using var t = new TestClient();
        var html = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(Fixture.Read("entry.html"))).ToArray();
        t.Handler.Route(
            uri => uri.AbsoluteUri.Contains("bom-edit", StringComparison.Ordinal),
            (request, _) =>
            {
                var response = FakeHttpHandler.Response(request, string.Empty);
                response.Content = new ByteArrayContent(html);
                return Task.FromResult(response);
            });

        var entry = await t.Client.GetEntryAsync("bom-edit", CancellationToken.None);

        Assert.Equal("M4\u2019s The Hobbit Book Edit", entry!.Title);
    }

    [Fact]
    public async Task TimeSpentQueuedDoesntCountTowardTheTimeout()
    {
        using var t = new TestClient(new PluginConfiguration { RequestDelayMilliseconds = 0, RequestTimeoutSeconds = 5, CacheMinutes = 0 });
        var release = new TaskCompletionSource();
        t.Handler.Route(
            uri => uri.AbsoluteUri.Contains("slow-edit", StringComparison.Ordinal),
            async (request, token) =>
            {
                await release.Task.WaitAsync(token);
                return FakeHttpHandler.Response(request, Fixture.Read("entry.html"));
            });
        t.Handler.Html("queued-edit", Fixture.Read("entry.html"));

        var slow = t.Client.GetEntryAsync("slow-edit", CancellationToken.None);
        var queued = t.Client.GetEntryAsync("queued-edit", CancellationToken.None);

        // Four seconds pass with the second request queued behind the first. Together that's more
        // than the 5 second timeout, but the second request's clock only starts once it's sent.
        t.Time.Advance(TimeSpan.FromSeconds(4));
        release.SetResult();
        Assert.NotNull(await slow);

        t.Time.Advance(TimeSpan.FromSeconds(4));
        Assert.NotNull(await queued);
    }

    // A response body whose length isn't known up front, like a chunked download.
    private sealed class UnknownLengthStream : System.IO.MemoryStream
    {
        public UnknownLengthStream(byte[] buffer)
            : base(buffer)
        {
        }

        public override bool CanSeek => false;
    }
}
