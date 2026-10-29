using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Ifdb.Api;

namespace Jellyfin.Plugin.Ifdb.Matching;

/// <summary>
/// Searches fanedit.org for a title and ranks the results by how well they match it.
/// </summary>
internal static class IfdbMatcher
{
    /// <summary>
    /// Searches for a title and scores every result against it, best first.
    /// fanedit.org's keyword search can trip over punctuation ("M4's"), so an empty search is retried once with plain words.
    /// </summary>
    /// <param name="client">The fanedit.org client.</param>
    /// <param name="title">The title to search for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The results with their scores, best first, ties in fanedit.org's order.</returns>
    public static async Task<List<ScoredResult>> SearchAsync(IfdbClient client, string title, CancellationToken cancellationToken)
    {
        var results = await client.SearchAsync(title, cancellationToken).ConfigureAwait(false);
        if (results.Count == 0)
        {
            var simplified = string.Join(' ', TitleScorer.Normalize(title).Split(' ').Where(w => w.Length > 1));
            if (simplified.Length > 0 && !string.Equals(simplified, title, StringComparison.OrdinalIgnoreCase))
            {
                results = await client.SearchAsync(simplified, cancellationToken).ConfigureAwait(false);
            }
        }

        return Rank(title, results);
    }

    /// <summary>
    /// Scores results against a title, best first; ties keep their original order.
    /// </summary>
    /// <param name="title">The searched title.</param>
    /// <param name="results">The search results.</param>
    /// <returns>The scored results.</returns>
    public static List<ScoredResult> Rank(string title, IEnumerable<IfdbSearchResult> results)
        => results
            .Select(result => new ScoredResult(result, TitleScorer.Score(title, result.Title)))
            .OrderByDescending(s => s.Score) // OrderBy is stable, so ties keep fanedit.org's order.
            .ToList();

    /// <summary>
    /// Picks the result for an unidentified movie, only when the choice is unambiguous: exactly one
    /// result at or above <paramref name="minimumScore"/>; or, when several qualify, exactly one
    /// perfect match, or else exactly one from the movie's year.
    /// </summary>
    /// <param name="scored">The ranked results.</param>
    /// <param name="minimumScore">The automatic-match score.</param>
    /// <param name="year">The movie's year, if known.</param>
    /// <returns>The match, or <c>null</c> when there is no unambiguous one.</returns>
    public static ScoredResult? PickAutomaticMatch(IReadOnlyList<ScoredResult> scored, int minimumScore, int? year)
    {
        var minimum = Math.Clamp(minimumScore, 0, 100);
        var qualifying = scored.Where(s => s.Score >= minimum).ToList();
        if (qualifying.Count == 1)
        {
            return qualifying[0];
        }

        var perfect = qualifying.Where(s => s.Score >= 100).ToList();
        if (perfect.Count == 1)
        {
            return perfect[0];
        }

        var sameYear = qualifying.Where(s => year is not null && s.Result.Year == year).ToList();
        return sameYear.Count == 1 ? sameYear[0] : null;
    }

    /// <summary>
    /// Picks the results to list in the Identify dialog: those at or above <paramref name="minimumScore"/>,
    /// or the best ten when none are, so the dialog is never empty when fanedit.org found something.
    /// </summary>
    /// <param name="scored">The ranked results.</param>
    /// <param name="minimumScore">The Identify dialog score.</param>
    /// <returns>The results to show.</returns>
    public static List<ScoredResult> PickSearchResults(IReadOnlyList<ScoredResult> scored, int minimumScore)
    {
        var minimum = Math.Clamp(minimumScore, 0, 100);
        var shown = scored.Where(s => s.Score >= minimum).ToList();
        return shown.Count > 0 ? shown : scored.Take(10).ToList();
    }
}
