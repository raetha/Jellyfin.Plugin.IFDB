using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Ifdb.Api;
using Jellyfin.Plugin.Ifdb.Matching;
using Jellyfin.Plugin.Ifdb.Tests.Support;
using Xunit;

namespace Jellyfin.Plugin.Ifdb.Tests;

public class IfdbMatcherTests
{
    private static ScoredResult Scored(string id, int score, int? year = null)
        => new(new IfdbSearchResult { Id = id, Title = id, Year = year }, score);

    [Fact]
    public async Task SearchRanksResultsAgainstTheTitle()
    {
        using var t = new TestClient();
        t.Handler.Html("keywords=Hobbit%20Book%20Edit", Fixture.Read("search.html"));

        var scored = await IfdbMatcher.SearchAsync(t.Client, "Hobbit Book Edit", CancellationToken.None);

        Assert.Equal("m4s-the-hobbit-book-edit", scored[0].Result.Id);
        Assert.Equal(100, scored[0].Score);
        Assert.True(scored[1].Score < scored[0].Score);
        Assert.Equal(1, t.Handler.RequestCount);
    }

    [Fact]
    public async Task EmptySearchIsRetriedWithPlainWords()
    {
        using var t = new TestClient();
        t.Handler.Html("keywords=M4%27s", "<html><body>No results</body></html>");
        t.Handler.Html("keywords=m4%20the%20hobbit%20book%20edit", Fixture.Read("search.html"));

        var scored = await IfdbMatcher.SearchAsync(t.Client, "M4's The Hobbit Book Edit", CancellationToken.None);

        Assert.Equal(2, t.Handler.RequestCount);
        Assert.Equal("m4s-the-hobbit-book-edit", scored[0].Result.Id);
    }

    [Theory]
    [InlineData("hobbit")]
    [InlineData("!!")]
    public async Task NoRetryWhenThePlainWordsAreTheSame(string title)
    {
        using var t = new TestClient();
        t.Handler.Html("search-results", "<html><body>No results</body></html>");

        Assert.Empty(await IfdbMatcher.SearchAsync(t.Client, title, CancellationToken.None));
        Assert.Equal(1, t.Handler.RequestCount);
    }

    [Fact]
    public void RankKeepsSiteOrderForTies()
    {
        var results = new[]
        {
            new IfdbSearchResult { Id = "first", Title = "Same Title" },
            new IfdbSearchResult { Id = "other", Title = "Something Else Entirely" },
            new IfdbSearchResult { Id = "second", Title = "Same Title" },
        };

        var ranked = IfdbMatcher.Rank("Same Title", results);

        Assert.Equal(new[] { "first", "second", "other" }, ranked.Select(r => r.Result.Id));
    }

    [Fact]
    public void AutomaticMatchNeedsExactlyOneGoodResult()
    {
        Assert.Equal("a", IfdbMatcher.PickAutomaticMatch([Scored("a", 95), Scored("b", 60)], 90, null)?.Result.Id);
        Assert.Null(IfdbMatcher.PickAutomaticMatch([Scored("a", 80)], 90, null));
        Assert.Null(IfdbMatcher.PickAutomaticMatch([], 90, null));
    }

    [Fact]
    public void AutomaticMatchPrefersASinglePerfectScore()
        => Assert.Equal("b", IfdbMatcher.PickAutomaticMatch([Scored("a", 95), Scored("b", 100)], 90, null)?.Result.Id);

    [Fact]
    public void AutomaticMatchFallsBackToTheMoviesYear()
    {
        var scored = new[] { Scored("a", 100, 2019), Scored("b", 100, 2021) };

        Assert.Equal("b", IfdbMatcher.PickAutomaticMatch(scored, 90, 2021)?.Result.Id);
        Assert.Null(IfdbMatcher.PickAutomaticMatch(scored, 90, 2020));
        Assert.Null(IfdbMatcher.PickAutomaticMatch(scored, 90, null));
    }

    [Fact]
    public void AutomaticMatchScoreIsClamped()
        => Assert.Equal("a", IfdbMatcher.PickAutomaticMatch([Scored("a", 100)], 500, null)?.Result.Id);

    [Fact]
    public void SearchResultsAreFilteredByScore()
    {
        var scored = new[] { Scored("a", 90), Scored("b", 40) };

        Assert.Equal(new[] { "a" }, IfdbMatcher.PickSearchResults(scored, 60).Select(s => s.Result.Id));
    }

    [Fact]
    public void SearchResultsFallBackToTheBestTen()
    {
        var scored = Enumerable.Range(0, 12).Select(i => Scored("r" + i, 20)).ToArray();

        Assert.Equal(10, IfdbMatcher.PickSearchResults(scored, 60).Count);
        Assert.Equal(12, IfdbMatcher.PickSearchResults(scored, -5).Count);
    }
}
