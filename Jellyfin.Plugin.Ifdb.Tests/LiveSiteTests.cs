using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Ifdb.Api;
using Jellyfin.Plugin.Ifdb.Configuration;
using Jellyfin.Plugin.Ifdb.Matching;
using Jellyfin.Plugin.Ifdb.Tests.Support;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.Ifdb.Tests;

/// <summary>
/// Known searches and entries checked against the real fanedit.org. Run with IFDB_LIVE_TESTS=1;
/// IFDB_USER_AGENT overrides the User-Agent. A failure means the site changed its layout, an entry
/// changed, or its bot protection blocked the request (the exception says which).
/// </summary>
public sealed class LiveSiteTests : IDisposable
{
    private readonly IfdbRequestState _state = new();
    private readonly IfdbClient _client;

    public LiveSiteTests()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient());
        var config = new PluginConfiguration
        {
            UserAgent = Environment.GetEnvironmentVariable("IFDB_USER_AGENT") ?? string.Empty,
        };
        _client = new IfdbClient(factory, new ListLogger(), () => config, _state, TimeProvider.System);
    }

    public void Dispose() => _state.Dispose();

    [LiveFact]
    public async Task HobbitBookEditIsFoundAndMatchedAutomatically()
    {
        var scored = await IfdbMatcher.SearchAsync(_client, "M4's The Hobbit Book Edit", CancellationToken.None);

        Assert.Contains(scored, s => s.Result.Id == "m4s-the-hobbit-book-edit");
        Assert.Equal("m4s-the-hobbit-book-edit", IfdbMatcher.PickAutomaticMatch(scored, 90, null)?.Result.Id);
    }

    [LiveFact]
    public async Task HobbitBookEditDetailsAreRead()
    {
        var entry = await _client.GetEntryAsync("m4s-the-hobbit-book-edit", CancellationToken.None);

        Assert.NotNull(entry);
        Assert.Contains("Hobbit Book Edit", entry.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(245, entry.RuntimeMinutes);
        Assert.False(string.IsNullOrWhiteSpace(entry.Synopsis));
        Assert.NotEmpty(entry.Images);
    }

    [LiveFact]
    public async Task BroadSearchListsManyReadableResults()
    {
        var results = await _client.SearchAsync("star wars", CancellationToken.None);

        Assert.True(results.Count >= 5, "Expected several Star Wars fanedits, got " + results.Count);
        Assert.All(results, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Id));
            Assert.False(string.IsNullOrWhiteSpace(r.Title));
            Assert.StartsWith("https://", r.Url, StringComparison.Ordinal);
        });
    }

    [LiveFact]
    public async Task UnknownTitleFindsNothing()
        => Assert.Empty(await _client.SearchAsync("zzqx no such fanedit title zzqx", CancellationToken.None));
}
