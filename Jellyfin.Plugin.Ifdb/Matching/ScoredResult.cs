using Jellyfin.Plugin.Ifdb.Api;

namespace Jellyfin.Plugin.Ifdb.Matching;

/// <summary>
/// A search result with its title match score.
/// </summary>
internal sealed class ScoredResult
{
    public ScoredResult(IfdbSearchResult result, int score)
    {
        Result = result;
        Score = score;
    }

    public IfdbSearchResult Result { get; }

    /// <summary>
    /// Gets the title match score, from 0 to 100.
    /// </summary>
    public int Score { get; }
}
