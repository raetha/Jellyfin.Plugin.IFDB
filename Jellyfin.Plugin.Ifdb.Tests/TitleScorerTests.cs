using Jellyfin.Plugin.Ifdb.Matching;
using Xunit;

namespace Jellyfin.Plugin.Ifdb.Tests;

public class TitleScorerTests
{
    [Theory]
    [InlineData("M4's The Hobbit Book Edit", "M4’s The Hobbit Book Edit")]
    [InlineData("Star Wars Episode I", "Star Wars: Episode I")]
    [InlineData("Amelie", "Amélie")]
    public void PunctuationCaseAndAccentsDontMatter(string query, string candidate)
        => Assert.Equal(100, TitleScorer.Score(query, candidate));

    [Fact]
    public void QueryContainedInLongerTitleScoresHigh()
        => Assert.True(TitleScorer.Score("Hobbit Book Edit", "M4's The Hobbit Book Edit") >= 90);

    [Fact]
    public void WordOrderDoesntMatter()
        => Assert.True(TitleScorer.Score("Book Edit Hobbit", "Hobbit Book Edit") >= 90);

    [Fact]
    public void SharingOneWordScoresLow()
        => Assert.True(TitleScorer.Score("The Hobbit", "The Matrix Reloaded Fixed") < 60);

    [Fact]
    public void DifferentInstallmentsArePenalised()
    {
        var same = TitleScorer.Score("Star Wars Episode I The Phantom Edit", "Star Wars Episode I The Phantom Edit");
        var other = TitleScorer.Score("Star Wars Episode I The Phantom Edit", "Star Wars Episode II The Phantom Edit");

        Assert.Equal(100, same);
        Assert.True(other < 90);
    }

    [Theory]
    [InlineData("", "anything")]
    [InlineData(null, "anything")]
    [InlineData("anything", "")]
    public void EmptyTitlesScoreZero(string? query, string candidate)
        => Assert.Equal(0, TitleScorer.Score(query, candidate));

    [Fact]
    public void RatioMatchesRapidfuzz()
    {
        // rapidfuzz: fuzz.ratio("kitten", "sitting") == 61.538...
        Assert.Equal(61.54, TitleScorer.Ratio("kitten", "sitting"), 2);
        Assert.Equal(100, TitleScorer.PartialRatio("hobbit", "the hobbit book edit"));
    }
}
