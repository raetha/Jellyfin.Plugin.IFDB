using System;
using System.Net;
using System.Threading.Tasks;
using Jellyfin.Plugin.Ifdb.Api;
using Xunit;

namespace Jellyfin.Plugin.Ifdb.Tests;

public class IfdbRequestStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private static Task<IfdbPage> Page(string url)
        => Task.FromResult(new IfdbPage(new Uri(url), HttpStatusCode.OK, "<html></html>"));

    [Fact]
    public void FullCacheDropsExpiredPagesFirst()
    {
        using var state = new IfdbRequestState(maxCachedPages: 4);
        state.SetPage("a", Page("https://x/a"), Now.AddMinutes(-1), Now);
        state.SetPage("b", Page("https://x/b"), Now.AddMinutes(5), Now);
        state.SetPage("c", Page("https://x/c"), Now.AddMinutes(-1), Now);
        state.SetPage("d", Page("https://x/d"), Now.AddMinutes(5), Now);

        state.SetPage("e", Page("https://x/e"), Now.AddMinutes(5), Now);

        Assert.Equal(3, state.CachedPageCount);
        Assert.False(state.TryGetPage("a", Now, out _));
        Assert.True(state.TryGetPage("b", Now, out _));
        Assert.True(state.TryGetPage("e", Now, out _));
    }

    [Fact]
    public void FullCacheOfLivePagesDropsThoseClosestToExpiring()
    {
        using var state = new IfdbRequestState(maxCachedPages: 4);
        for (var i = 1; i <= 4; i++)
        {
            state.SetPage("p" + i, Page("https://x/" + i), Now.AddMinutes(i), Now);
        }

        state.SetPage("p5", Page("https://x/5"), Now.AddMinutes(5), Now);

        Assert.Equal(4, state.CachedPageCount);
        Assert.False(state.TryGetPage("p1", Now, out _));
        Assert.True(state.TryGetPage("p2", Now, out _));
    }

    [Fact]
    public async Task ReplacingACachedPageDoesntPrune()
    {
        using var state = new IfdbRequestState(maxCachedPages: 1);
        state.SetPage("a", Page("https://x/a"), Now.AddMinutes(5), Now);
        state.SetPage("a", Page("https://x/a2"), Now.AddMinutes(5), Now);

        Assert.True(state.TryGetPage("a", Now, out var fetch));
        Assert.Equal("https://x/a2", (await fetch).FinalUrl.ToString());
    }

    [Fact]
    public void FailedFetchesAreNotServedFromCache()
    {
        using var state = new IfdbRequestState();
        state.SetPage("faulted", Task.FromException<IfdbPage>(new InvalidOperationException()), Now.AddMinutes(5), Now);
        state.SetPage("cancelled", Task.FromCanceled<IfdbPage>(new System.Threading.CancellationToken(true)), Now.AddMinutes(5), Now);

        Assert.False(state.TryGetPage("faulted", Now, out _));
        Assert.False(state.TryGetPage("cancelled", Now, out _));
        Assert.False(state.TryGetPage("missing", Now, out _));
    }

    [Fact]
    public void LastRequestRoundTrips()
    {
        using var state = new IfdbRequestState();
        state.LastRequest = Now;

        Assert.Equal(Now, state.LastRequest);
    }

    [Fact]
    public void SharedStateIsASingleton()
        => Assert.Same(IfdbRequestState.Shared, IfdbRequestState.Shared);

    [Fact]
    public void BlockedWarningIsClaimedOncePerInterval()
    {
        using var state = new IfdbRequestState();
        var interval = TimeSpan.FromMinutes(15);

        Assert.True(state.TryClaimBlockedWarning(Now, interval));
        Assert.False(state.TryClaimBlockedWarning(Now.AddMinutes(14), interval));
        Assert.True(state.TryClaimBlockedWarning(Now.AddMinutes(15), interval));
    }
}
