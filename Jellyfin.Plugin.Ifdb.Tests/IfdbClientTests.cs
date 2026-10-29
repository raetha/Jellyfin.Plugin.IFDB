using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Ifdb.Api;
using Jellyfin.Plugin.Ifdb.Configuration;
using Jellyfin.Plugin.Ifdb.Tests.Support;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.Ifdb.Tests;

public class IfdbClientTests
{
    private const string EntryUrl = "https://fanedit.org/m4s-the-hobbit-book-edit/";

    [Fact]
    public async Task SearchParsesListingsAndSendsIdentifyingHeaders()
    {
        using var t = new TestClient();
        t.Handler.Html("search-results", Fixture.Read("search.html"));

        var results = await t.Client.SearchAsync("hobbit", CancellationToken.None);

        Assert.Equal(2, results.Count);
        var request = Assert.Single(t.Handler.Requests);
        Assert.Equal(
            "https://fanedit.org/fanedit-search/search-results/?query=all&scope=title&keywords=hobbit&order=rdate",
            request.RequestUri!.ToString());
        Assert.StartsWith("Jellyfin-IFDB/", request.Headers.UserAgent.ToString(), StringComparison.Ordinal);
        Assert.Contains("text/html", request.Headers.Accept.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfiguredUserAgentAndSiteAreUsed()
    {
        using var t = new TestClient(new PluginConfiguration
        {
            RequestDelayMilliseconds = 0,
            UserAgent = "  Mozilla/5.0 Test  ",
            BaseUrl = "ifdb.fanedit.org",
            MatchType = SearchMatchType.Exact,
        });
        t.Handler.Html("search-results", Fixture.Read("search.html"));

        await t.Client.SearchAsync("hobbit", CancellationToken.None);

        var request = Assert.Single(t.Handler.Requests);
        Assert.Equal("Mozilla/5.0 Test", t.Client.UserAgent);
        Assert.Equal("Mozilla/5.0 Test", request.Headers.UserAgent.ToString());
        Assert.StartsWith("https://ifdb.fanedit.org/fanedit-search/search-results/?query=exact", request.RequestUri!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultUserAgentNamesThePluginVersionAndProject()
    {
        using var t = new TestClient();

        Assert.Matches(@"^Jellyfin-IFDB/\d+\.\d+\.\d+ \(\+https://github\.com/raetha/Jellyfin\.Plugin\.IFDB\)$", t.Client.UserAgent);
    }

    [Fact]
    public async Task SearchRedirectedToAnEntryReturnsThatEntry()
    {
        using var t = new TestClient();
        t.Handler.Html("search-results", Fixture.Read("entry.html"), finalUrl: EntryUrl);

        var results = await t.Client.SearchAsync("M4's The Hobbit Book Edit", CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal("m4s-the-hobbit-book-edit", result.Id);
        Assert.Equal(2015, result.Year);
        Assert.Equal("https://fanedit.org/wp-content/uploads/2016/01/hobbit-cover.jpg", result.ImageUrl);
        Assert.Equal("A tighter, book-faithful Hobbit in one sitting.", result.Overview);
    }

    [Fact]
    public async Task SearchRedirectedToAnEntryIsCachedForTheEntryLookup()
    {
        using var t = new TestClient();
        t.Handler.Html("search-results", Fixture.Read("entry.html"), finalUrl: EntryUrl);

        await t.Client.SearchAsync("M4's The Hobbit Book Edit", CancellationToken.None);
        var entry = await t.Client.GetEntryAsync("m4s-the-hobbit-book-edit", CancellationToken.None);

        Assert.NotNull(entry);
        Assert.Equal(1, t.Handler.RequestCount);
    }

    [Fact]
    public async Task SearchRedirectedToANonEntryPageIsEmpty()
    {
        using var t = new TestClient();
        t.Handler.Html("search-results", Fixture.Read("not-entry.html"), finalUrl: "https://fanedit.org/some-blog-post/");

        Assert.Empty(await t.Client.SearchAsync("x", CancellationToken.None));
        Assert.True(t.Logger.Has(LogLevel.Warning, "isn't a fanedit entry"));
    }

    [Fact]
    public async Task UnreadableListingsAreReportedAsALayoutChange()
    {
        using var t = new TestClient();
        t.Handler.Html("search-results", "<html><body><div class=\"jrListingTitle\">No link here</div></body></html>");

        Assert.Empty(await t.Client.SearchAsync("x", CancellationToken.None));
        Assert.True(t.Logger.Has(LogLevel.Warning, "layout may have changed"));
    }

    [Fact]
    public async Task SearchResultsAreLimitedToTheConfiguredMaximum()
    {
        using var t = new TestClient(new PluginConfiguration { RequestDelayMilliseconds = 0, MaxSearchResults = 1 });
        t.Handler.Html("search-results", Fixture.Read("search.html"));

        var results = await t.Client.SearchAsync("hobbit", CancellationToken.None);

        Assert.Equal("m4s-the-hobbit-book-edit", Assert.Single(results).Id);
    }

    [Fact]
    public async Task ServerErrorsThrowAndAreNotCached()
    {
        using var t = new TestClient();
        t.Handler.Html("search-results", "oops", HttpStatusCode.InternalServerError);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => t.Client.SearchAsync("x", CancellationToken.None));
        Assert.Equal(HttpStatusCode.InternalServerError, error.StatusCode);

        t.Handler.Html("search-results", Fixture.Read("search.html"));
        Assert.Equal(2, (await t.Client.SearchAsync("x", CancellationToken.None)).Count);
        Assert.Equal(2, t.Handler.RequestCount);
    }

    [Fact]
    public async Task ChallengePagesThrowBlocked()
    {
        using var t = new TestClient();
        t.Handler.Html("search-results", Fixture.Read("challenge.html"), HttpStatusCode.Forbidden);

        var error = await Assert.ThrowsAsync<IfdbBlockedException>(() => t.Client.SearchAsync("x", CancellationToken.None));
        Assert.Contains("HTTP 403", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CloudflareMitigationHeaderThrowsBlocked()
    {
        using var t = new TestClient();
        t.Handler.Html("search-results", "<html></html>", HttpStatusCode.Forbidden, header: "cf-mitigated: challenge");

        await Assert.ThrowsAsync<IfdbBlockedException>(() => t.Client.SearchAsync("x", CancellationToken.None));
    }

    [Fact]
    public async Task BlockedFetchesAreRetriedNextTime()
    {
        using var t = new TestClient();
        t.Handler.Html("search-results", Fixture.Read("challenge.html"));
        await Assert.ThrowsAsync<IfdbBlockedException>(() => t.Client.SearchAsync("x", CancellationToken.None));

        t.Handler.Html("search-results", Fixture.Read("search.html"));
        Assert.Equal(2, (await t.Client.SearchAsync("x", CancellationToken.None)).Count);
    }

    [Fact]
    public async Task GetEntryParsesTheEntry()
    {
        using var t = new TestClient();
        t.Handler.Html(EntryUrl, Fixture.Read("entry.html"));

        var entry = await t.Client.GetEntryAsync("m4s-the-hobbit-book-edit", CancellationToken.None);

        Assert.NotNull(entry);
        Assert.Equal("m4s-the-hobbit-book-edit", entry.Id);
        Assert.Equal(EntryUrl, Assert.Single(t.Handler.Requests).RequestUri!.ToString());
    }

    [Fact]
    public async Task LegacyPostNumbersFollowTheRedirectToTheSlug()
    {
        using var t = new TestClient();
        t.Handler.Html("?p=17071", Fixture.Read("entry.html"), finalUrl: EntryUrl);

        var entry = await t.Client.GetEntryAsync("17071", CancellationToken.None);

        Assert.NotNull(entry);
        Assert.Equal("m4s-the-hobbit-book-edit", entry.Id);
        Assert.Equal("https://fanedit.org/?p=17071", Assert.Single(t.Handler.Requests).RequestUri!.ToString());
    }

    [Fact]
    public async Task EntryWithoutAnIdInItsAddressKeepsTheRequestedId()
    {
        using var t = new TestClient();
        t.Handler.Html("?p=17071", Fixture.Read("entry.html"), finalUrl: "https://fanedit.org/");

        var entry = await t.Client.GetEntryAsync("17071", CancellationToken.None);

        Assert.NotNull(entry);
        Assert.Equal("17071", entry.Id);
    }

    [Fact]
    public async Task MissingEntriesReturnNull()
    {
        using var t = new TestClient();

        Assert.Null(await t.Client.GetEntryAsync("no-such-edit", CancellationToken.None));
        Assert.True(t.Logger.Has(LogLevel.Warning, "was not found"));
    }

    [Fact]
    public async Task NonEntryPagesReturnNullAndLogThePageTitle()
    {
        using var t = new TestClient();
        t.Handler.Html("some-page", Fixture.Read("not-entry.html"));

        Assert.Null(await t.Client.GetEntryAsync("some-page", CancellationToken.None));
        Assert.True(t.Logger.Has(LogLevel.Warning, "Page not found"));
    }

    [Fact]
    public async Task GetEntryServerErrorsThrow()
    {
        using var t = new TestClient();
        t.Handler.Html("broken", "oops", HttpStatusCode.BadGateway);

        await Assert.ThrowsAsync<HttpRequestException>(() => t.Client.GetEntryAsync("broken", CancellationToken.None));
    }

    [Fact]
    public async Task PagesAreCachedUntilTheyExpire()
    {
        using var t = new TestClient(new PluginConfiguration { RequestDelayMilliseconds = 0, CacheMinutes = 10 });
        t.Handler.Html(EntryUrl, Fixture.Read("entry.html"));

        await t.Client.GetEntryAsync("m4s-the-hobbit-book-edit", CancellationToken.None);
        await t.Client.GetEntryAsync("m4s-the-hobbit-book-edit", CancellationToken.None);
        Assert.Equal(1, t.Handler.RequestCount);

        t.Time.Advance(TimeSpan.FromMinutes(11));
        await t.Client.GetEntryAsync("m4s-the-hobbit-book-edit", CancellationToken.None);
        Assert.Equal(2, t.Handler.RequestCount);
    }

    [Fact]
    public async Task CachingCanBeTurnedOff()
    {
        using var t = new TestClient(new PluginConfiguration { RequestDelayMilliseconds = 0, CacheMinutes = 0 });
        t.Handler.Html(EntryUrl, Fixture.Read("entry.html"));

        await t.Client.GetEntryAsync("m4s-the-hobbit-book-edit", CancellationToken.None);
        await t.Client.GetEntryAsync("m4s-the-hobbit-book-edit", CancellationToken.None);

        Assert.Equal(2, t.Handler.RequestCount);
        Assert.Equal(0, t.State.CachedPageCount);
    }

    [Fact]
    public async Task ConcurrentIdenticalRequestsShareOneFetch()
    {
        using var t = new TestClient();
        var release = new TaskCompletionSource();
        t.Handler.Route(
            uri => uri.ToString() == EntryUrl,
            async (request, _) =>
            {
                await release.Task;
                return FakeHttpHandler.Response(request, Fixture.Read("entry.html"));
            });

        var first = t.Client.GetEntryAsync("m4s-the-hobbit-book-edit", CancellationToken.None);
        var second = t.Client.GetEntryAsync("m4s-the-hobbit-book-edit", CancellationToken.None);
        release.SetResult();

        Assert.NotNull(await first);
        Assert.NotNull(await second);
        Assert.Equal(1, t.Handler.RequestCount);
    }

    [Fact]
    public async Task CancellingOneCallerDoesntCancelTheSharedFetch()
    {
        using var t = new TestClient();
        var release = new TaskCompletionSource();
        t.Handler.Route(
            uri => uri.ToString() == EntryUrl,
            async (request, _) =>
            {
                await release.Task;
                return FakeHttpHandler.Response(request, Fixture.Read("entry.html"));
            });

        using var cancel = new CancellationTokenSource();
        var cancelled = t.Client.GetEntryAsync("m4s-the-hobbit-book-edit", cancel.Token);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);

        release.SetResult();
        Assert.NotNull(await t.Client.GetEntryAsync("m4s-the-hobbit-book-edit", CancellationToken.None));
        Assert.Equal(1, t.Handler.RequestCount);
    }

    [Fact]
    public async Task SlowRequestsTimeOut()
    {
        using var t = new TestClient(new PluginConfiguration { RequestDelayMilliseconds = 0, RequestTimeoutSeconds = 5 });
        t.Handler.Route(
            uri => uri.ToString() == EntryUrl,
            async (request, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return FakeHttpHandler.Response(request, string.Empty);
            });

        var fetch = t.Client.GetEntryAsync("m4s-the-hobbit-book-edit", CancellationToken.None);
        await WaitUntil(() => t.Handler.RequestCount == 1);
        t.Time.Advance(TimeSpan.FromSeconds(6));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetch);
        Assert.Equal(0, t.State.CachedPageCount);
    }

    [Fact]
    public async Task RequestsAreSpacedOutByTheConfiguredDelay()
    {
        using var t = new TestClient(new PluginConfiguration { RequestDelayMilliseconds = 1000, CacheMinutes = 0 });
        t.Handler.Html(EntryUrl, Fixture.Read("entry.html"));

        // The first request goes straight out (the last one was long ago).
        await t.Client.GetEntryAsync("m4s-the-hobbit-book-edit", CancellationToken.None);

        // The second waits for the delay, which only passes when the clock moves.
        var second = t.Client.GetEntryAsync("m4s-the-hobbit-book-edit", CancellationToken.None);
        await Task.Delay(50);
        Assert.False(second.IsCompleted);
        Assert.Equal(1, t.Handler.RequestCount);

        t.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.NotNull(await second);
        Assert.Equal(2, t.Handler.RequestCount);
    }

    [Fact]
    public async Task ImageRequestsSendIdentifyingHeaders()
    {
        using var t = new TestClient();
        t.Handler.Html("cover.jpg", "image-bytes");

        using var response = await t.Client.GetImageResponseAsync("https://fanedit.org/cover.jpg", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var request = Assert.Single(t.Handler.Requests);
        Assert.StartsWith("Jellyfin-IFDB/", request.Headers.UserAgent.ToString(), StringComparison.Ordinal);
        Assert.Contains("image/", request.Headers.Accept.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void BlockedRequestsLogOneErrorPerQuarterHour()
    {
        using var t = new TestClient();
        var blocked = new IfdbBlockedException("HTTP 403 challenge");

        t.Client.LogFailure(blocked, "searching");
        t.Client.LogFailure(blocked, "searching");
        t.Time.Advance(TimeSpan.FromMinutes(16));
        t.Client.LogFailure(blocked, "searching");

        Assert.Equal(2, t.Logger.Entries.Count(e => e.Level == LogLevel.Error));
        Assert.Equal(1, t.Logger.Entries.Count(e => e.Level == LogLevel.Debug));
        Assert.True(t.Logger.Has(LogLevel.Error, "different User-Agent"));
    }

    [Fact]
    public void TimeoutsAndOtherFailuresAreLogged()
    {
        using var t = new TestClient();
        var failure = new HttpRequestException("boom");

        t.Client.LogFailure(new TaskCanceledException(), "fetching");
        t.Client.LogFailure(failure, "fetching");

        Assert.True(t.Logger.Has(LogLevel.Warning, "timed out while fetching"));
        Assert.Contains(t.Logger.Entries, e => e.Level == LogLevel.Error && e.Exception == failure);
    }

    [Fact]
    public async Task NothingIsLoggedWhenLoggingIsOff()
    {
        using var t = new TestClient(logLevel: LogLevel.None);
        t.Handler.Html("search-results", "<html><body><div class=\"jrListingTitle\">x</div></body></html>");
        t.Handler.Html("not-entry", Fixture.Read("not-entry.html"));
        t.Handler.Html("redirected", Fixture.Read("not-entry.html"), finalUrl: "https://fanedit.org/blog/");

        await t.Client.SearchAsync("x", CancellationToken.None);
        await t.Client.GetEntryAsync("not-entry", CancellationToken.None);
        await t.Client.GetEntryAsync("missing", CancellationToken.None);
        t.Config = new PluginConfiguration { RequestDelayMilliseconds = 0, BaseUrl = "https://fanedit.org" };
        t.Handler.Html("search-results", Fixture.Read("not-entry.html"), finalUrl: "https://fanedit.org/blog/");
        await t.Client.SearchAsync("y", CancellationToken.None);
        t.Handler.Html("search-results", Fixture.Read("entry.html"), finalUrl: EntryUrl);
        await t.Client.SearchAsync("z", CancellationToken.None);
        t.Client.LogFailure(new IfdbBlockedException(), "a");
        t.Client.LogFailure(new IfdbBlockedException(), "b");
        t.Client.LogFailure(new OperationCanceledException(), "c");

        Assert.All(t.Logger.Entries, e => Assert.True(e.Level >= LogLevel.Error));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }
}
