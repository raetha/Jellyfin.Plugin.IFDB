using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Ifdb.Matching;

/// <summary>
/// Scores how well a fanedit title matches a searched title, from 0 to 100.
/// </summary>
/// <remarks>
/// Ported from circulon/fanedit-provider's scorer (which uses rapidfuzz). It takes the best of a
/// plain similarity ratio and a word-order-insensitive ratio, plus a substring ratio only when
/// every searched word appears in the candidate, so a short query sharing one common word with an
/// unrelated title doesn't score high. Mismatched installment numbers ("Episode I" vs "Episode II")
/// are penalised.
/// </remarks>
internal static partial class TitleScorer
{
    private const double InstallmentMismatchPenalty = 0.8;

    private static readonly HashSet<string> RomanNumerals = new(StringComparer.Ordinal)
    {
        "i", "ii", "iii", "iv", "v", "vi", "vii", "viii", "ix", "x",
        "xi", "xii", "xiii", "xiv", "xv", "xvi", "xvii", "xviii", "xix", "xx"
    };

    /// <summary>
    /// Scores a candidate title against the searched title.
    /// </summary>
    /// <param name="query">The searched title.</param>
    /// <param name="candidate">The candidate title.</param>
    /// <returns>A score from 0 (unrelated) to 100 (same title).</returns>
    public static int Score(string? query, string? candidate)
    {
        var q = Normalize(query);
        var c = Normalize(candidate);
        if (q.Length == 0 || c.Length == 0)
        {
            return 0;
        }

        var score = Math.Max(Ratio(q, c), Ratio(SortTokens(q), SortTokens(c)));
        if (q.Split(' ').All(token => c.Contains(token, StringComparison.Ordinal)))
        {
            score = Math.Max(score, PartialRatio(q, c));
        }

        var queryNumbers = InstallmentNumbers(q);
        var candidateNumbers = InstallmentNumbers(c);
        if (queryNumbers.Count > 0 && candidateNumbers.Count > 0 && !queryNumbers.Overlaps(candidateNumbers))
        {
            score *= InstallmentMismatchPenalty;
        }

        return (int)Math.Round(score, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Lowercases, strips accents and punctuation, and collapses whitespace,
    /// so "Star Wars: Episode I" and "star wars episode i" compare equal.
    /// </summary>
    /// <param name="text">The text to normalize.</param>
    /// <returns>The normalized text.</returns>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        var lower = builder.ToString().ToLowerInvariant();
        return NonAlphanumericRegex().Replace(lower, " ").Trim();
    }

    /// <summary>
    /// Similarity of two strings as 200 * (longest common subsequence) / (total length),
    /// the same measure as rapidfuzz's fuzz.ratio.
    /// </summary>
    /// <param name="a">The first string.</param>
    /// <param name="b">The second string.</param>
    /// <returns>The similarity, 0-100.</returns>
    public static double Ratio(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0)
        {
            return 100;
        }

        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        // Two-row dynamic programming for the longest common subsequence.
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                current[j] = a[i - 1] == b[j - 1]
                    ? previous[j - 1] + 1
                    : Math.Max(previous[j], current[j - 1]);
            }

            (previous, current) = (current, previous);
        }

        return 200.0 * previous[b.Length] / (a.Length + b.Length);
    }

    /// <summary>
    /// The best <see cref="Ratio"/> of the shorter string against every same-length window of the longer one.
    /// </summary>
    /// <param name="a">The first string.</param>
    /// <param name="b">The second string.</param>
    /// <returns>The similarity, 0-100.</returns>
    public static double PartialRatio(string a, string b)
    {
        var shorter = a.Length <= b.Length ? a : b;
        var longer = a.Length <= b.Length ? b : a;
        if (shorter.Length == 0)
        {
            return 0;
        }

        var best = 0.0;
        for (var start = 0; start + shorter.Length <= longer.Length; start++)
        {
            best = Math.Max(best, Ratio(shorter, longer.Substring(start, shorter.Length)));
            if (best >= 100)
            {
                break;
            }
        }

        return best;
    }

    private static string SortTokens(string text)
        => string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal));

    private static HashSet<string> InstallmentNumbers(string normalized)
        => normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.All(char.IsAsciiDigit) || RomanNumerals.Contains(token))
            .ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumericRegex();
}
