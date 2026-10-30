using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Ifdb.Api;

/// <summary>
/// Makes text scraped from fanedit.org safe and tidy to store as Jellyfin metadata.
/// </summary>
/// <remarks>
/// Jellyfin stores metadata through parameterized database queries, so text can't inject into the
/// database. This is defence in depth for the places text is used afterwards: the web client renders
/// overviews as Markdown with HTML allowed (cleaned by DOMPurify), other clients show them as plain
/// text, and NFO files are XML, which can't contain most control characters.
/// </remarks>
internal static partial class ScrapedText
{
    /// <summary>
    /// The longest overview kept, in characters.
    /// </summary>
    public const int MaxOverviewLength = 20000;

    /// <summary>
    /// The longest single value kept (a title, name, genre or tag), in characters.
    /// </summary>
    public const int MaxValueLength = 300;

    /// <summary>
    /// Removes control characters (keeping line breaks), invisible text-direction overrides and
    /// anything shaped like an HTML tag, and limits the length.
    /// </summary>
    /// <param name="text">The text, already decoded from HTML.</param>
    /// <param name="maxLength">The longest result to return.</param>
    /// <returns>The cleaned text.</returns>
    public static string Clean(string text, int maxLength)
    {
        // Entities were decoded earlier, so "&lt;img ...&gt;" in the page is a real tag by now.
        var withoutTags = TagRegex().Replace(text, string.Empty);

        var builder = new StringBuilder(withoutTags.Length);
        foreach (var ch in withoutTags)
        {
            if (ch == '\n' || !IsUnwanted(ch))
            {
                builder.Append(ch);
            }
        }

        var result = builder.ToString().Trim();
        return result.Length > maxLength ? result[..maxLength].TrimEnd() + "\u2026" : result;
    }

    // Control characters break XML (NFO files); the bidirectional overrides and isolates can make
    // displayed text read differently from what's stored.
    private static bool IsUnwanted(char ch)
        => char.IsControl(ch)
            || ch is >= '\u202A' and <= '\u202E'
            || ch is >= '\u2066' and <= '\u2069'
            || ch == '\uFEFF';

    [GeneratedRegex(@"</?[a-zA-Z!][^<>]*>")]
    private static partial Regex TagRegex();
}
