using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace Jellyfin.Plugin.Ifdb.Api;

/// <summary>
/// Turns fanedit.org HTML into search results and entries.
/// </summary>
/// <remarks>
/// fanedit.org is a WordPress site running the JReviews directory plugin, which marks every
/// field with a "jr&lt;Fieldname&gt;" class. Selectors follow circulon/fanedit-provider, which
/// tracks the live site, with fallbacks for the older markup earlier versions of this plugin used.
/// If the site's markup changes, this is the only class that should need updating.
/// </remarks>
internal static partial class IfdbParser
{
    private const string LineBreakToken = "\uE000";

    private static readonly string[] ImageSourceAttributes = ["data-jr-src", "data-src", "data-lazy-src", "src"];

    private static readonly string[] DateFormats =
    [
        "MMMM yyyy",
        "MMM yyyy",
        "MMM. yyyy",
        "MMMM d, yyyy",
        "MMM d, yyyy",
        "d MMMM yyyy",
        "yyyy-MM-dd",
        "yyyy"
    ];

    /// <summary>
    /// Parses a search results page.
    /// </summary>
    /// <param name="html">The page HTML.</param>
    /// <param name="pageUrl">The URL the page was loaded from, for resolving relative links.</param>
    /// <returns>The listed fanedits, in page order, without duplicates.</returns>
    public static List<IfdbSearchResult> ParseSearchResults(string html, Uri pageUrl)
    {
        var doc = Load(html);
        var results = new List<IfdbSearchResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var titleNodes = doc.DocumentNode.SelectNodes("//div[contains(@class,'jrListingTitle')]");
        if (titleNodes is not null)
        {
            foreach (var titleNode in titleNodes)
            {
                var link = titleNode.SelectSingleNode(".//a[@href]");
                var title = CleanText(link?.InnerText);
                var url = Resolve(pageUrl, link?.GetAttributeValue("href", string.Empty));
                var id = url is null ? null : IfdbUrls.IdFromUrl(url);
                if (id is null || url is null || title.Length == 0 || !seen.Add(id))
                {
                    continue;
                }

                // The title sits two levels inside the listing's container (see fanedit-provider).
                // A node selected from a parsed document always has a parent.
                var container = titleNode.ParentNode!.ParentNode ?? titleNode;
                var result = new IfdbSearchResult
                {
                    Id = id,
                    Title = title,
                    Year = ParseDate(FieldText(container, "jrFaneditreleasedate"))?.Year,
                    Overview = NullIfEmpty(FieldText(container, "jrBriefsynopsis")),
                };

                var thumb = container.SelectSingleNode(".//div[contains(@class,'jrListingThumbnail')]//img");
                result.ImageUrl = Resolve(pageUrl, ImageSource(thumb))?.ToString();
                results.Add(result);
            }
        }

        if (results.Count > 0)
        {
            return results;
        }

        // Older markup: a hidden "compare" checkbox per listing carried the title and URLs as data attributes.
        var checkboxes = doc.DocumentNode.SelectNodes("//input[contains(@class,'jrCheckListing')]");
        if (checkboxes is null)
        {
            return results;
        }

        foreach (var checkbox in checkboxes)
        {
            var url = Resolve(pageUrl, checkbox.GetAttributeValue("data-listingurl", string.Empty));
            var id = url is null ? null : IfdbUrls.IdFromUrl(url);
            id ??= NullIfEmpty(checkbox.GetAttributeValue("value", string.Empty));
            var title = CleanText(checkbox.GetAttributeValue("data-listingtitle", string.Empty));
            if (id is null || title.Length == 0 || !seen.Add(id))
            {
                continue;
            }

            results.Add(new IfdbSearchResult
            {
                Id = id,
                Title = title,
                ImageUrl = Resolve(pageUrl, checkbox.GetAttributeValue("data-thumburl", string.Empty))?.ToString(),
            });
        }

        return results;
    }

    /// <summary>
    /// Gets a value indicating whether the page is a bot-protection challenge rather than site content.
    /// </summary>
    /// <param name="html">The page HTML.</param>
    /// <returns><c>true</c> for a challenge page.</returns>
    public static bool IsChallengePage(string html)
    {
        // Normal Cloudflare-served pages also load /cdn-cgi/challenge-platform/ scripts, so that
        // alone means nothing; only the interstitial page has these markers and no site content.
        var looksLikeChallenge = html.Contains("_cf_chl_opt", StringComparison.Ordinal)
            || html.Contains("<title>Just a moment", StringComparison.OrdinalIgnoreCase)
            || html.Contains("Performing security verification", StringComparison.OrdinalIgnoreCase);

        return looksLikeChallenge
            && !html.Contains("jrFieldValue", StringComparison.Ordinal)
            && !html.Contains("jrListing", StringComparison.Ordinal);
    }

    /// <summary>
    /// Gets the page's &lt;title&gt;, for diagnostics when a page can't be parsed.
    /// </summary>
    /// <param name="html">The page HTML.</param>
    /// <returns>The title text.</returns>
    public static string PageTitle(string html)
        => CleanText(Load(html).DocumentNode.SelectSingleNode("//title")?.InnerText);

    /// <summary>
    /// Parses a fanedit's detail page.
    /// </summary>
    /// <param name="html">The page HTML.</param>
    /// <param name="pageUrl">The URL the page was finally loaded from (after redirects).</param>
    /// <returns>The entry, or <c>null</c> if the page isn't a fanedit entry.</returns>
    public static IfdbEntry? ParseEntry(string html, Uri pageUrl)
    {
        var doc = Load(html);
        var root = doc.DocumentNode;

        // Fields are read from the main content area, so related listings in sidebars aren't mixed in.
        var main = root.SelectSingleNode("//div[@id='primary']") ?? root;

        var title = CleanText(main.SelectSingleNode(".//h1//*[@itemprop='headline']")?.InnerText);
        if (title.Length == 0)
        {
            title = CleanText(root.SelectSingleNode("//input[contains(@class,'jrCheckListing')]")?.GetAttributeValue("data-listingtitle", string.Empty));
        }

        if (title.Length == 0)
        {
            title = CleanText(main.SelectSingleNode(".//h1")?.InnerText);
        }

        // Without any JReviews field this is a 404 page, a blog post, or some other non-entry page.
        var hasFields = main.SelectSingleNode(".//div[contains(@class,'jrFieldValue')]") is not null;
        if (title.Length == 0 || !hasFields)
        {
            return null;
        }

        var entry = new IfdbEntry
        {
            Id = IfdbUrls.IdFromUrl(pageUrl) ?? string.Empty,
            Url = pageUrl.ToString(),
            Title = title,
        };

        entry.Faneditors.AddRange(FieldValues(main, "jrFaneditorname"));
        entry.OriginalTitles.AddRange(FieldValues(main, "jrOriginalmovietitle"));
        entry.Genres.AddRange(FieldValues(main, "jrGenre"));
        entry.FaneditTypes.AddRange(FieldValues(main, "jrFanedittype"));
        entry.Franchises.AddRange(FieldValues(main, "jrFranchise"));
        entry.ReleaseDate = ParseDate(FieldText(main, "jrFaneditreleasedate"));
        entry.RuntimeMinutes = ParseLeadingInt(FieldText(main, "jrFaneditrunningtimemin"));

        // The IMDb id is matched anywhere in Additional Links rather than parsed from one href,
        // because some entries have malformed (doubled-up) links that still contain a valid id.
        var links = FieldNode(main, "jrAdditionallinks");
        if (links is not null)
        {
            var imdb = ImdbIdRegex().Match(links.InnerHtml);
            if (imdb.Success)
            {
                entry.ImdbId = imdb.Value;
            }
        }

        // Sidebars list other fanedits with their own ratings, so prefer the main area's block.
        var ratings = main.SelectSingleNode(".//div[contains(@class,'jrOverallRatings')]")
            ?? root.SelectSingleNode("//div[contains(@class,'jrOverallRatings')]")
            ?? root;
        entry.TrustedReviewerRating = ParseRating(ratings, "jrOverallEditor");
        entry.UserRating = ParseRating(ratings, "jrOverallUser");

        var synopsisScope = root.SelectSingleNode("//div[@id='fanedit-info']") ?? main;
        entry.Synopsis = NullIfEmpty(FieldText(synopsisScope, "jrBriefsynopsis"));

        var changeRows = root.SelectNodes("//div[@id='changes']//div[contains(@class,'jrFieldRow')]");
        if (changeRows is not null)
        {
            foreach (var row in changeRows)
            {
                var value = row.SelectSingleNode(".//div[contains(@class,'jrFieldValue')]");
                var text = value is null ? string.Empty : MultilineText(value);
                if (text.Length == 0)
                {
                    continue;
                }

                var label = CleanText(row.SelectSingleNode(".//div[contains(@class,'jrFieldLabel')]")?.InnerText).TrimEnd(':').Trim();
                entry.Changes.Add(new IfdbChange { Label = label, Text = text });
            }
        }

        AddImages(entry, root, main, pageUrl);
        AddTrailers(entry, root, main);
        return entry;
    }

    /// <summary>
    /// Parses fanedit.org's loose release dates: "July 2025", "Jul. 2025", "2008", or a range like "2012 / 2013" (latest year wins).
    /// </summary>
    /// <param name="text">The date text.</param>
    /// <returns>The date (first of the month or year when only those are given), or <c>null</c>.</returns>
    public static DateTime? ParseDate(string? text)
    {
        var value = CleanText(text);
        if (value.Length == 0)
        {
            return null;
        }

        if (value.Contains('/', StringComparison.Ordinal))
        {
            var years = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (years.Length > 0 && years.All(y => y.Length == 4 && y.All(char.IsAsciiDigit)))
            {
                value = years[^1];
            }
        }

        // .NET spells September "Sep"; the site sometimes uses "Sept".
        value = SeptRegex().Replace(value, "Sep");

        if (DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date))
        {
            return DateTime.SpecifyKind(date, DateTimeKind.Utc);
        }

        // Last resort: a four-digit year anywhere in the text.
        var year = YearRegex().Match(value);
        if (year.Success && int.TryParse(year.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
        {
            return new DateTime(y, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        return null;
    }

    public static int? ParseLeadingInt(string? text)
    {
        var match = DigitsRegex().Match(text ?? string.Empty);
        return match.Success && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static void AddImages(IfdbEntry entry, HtmlNode root, HtmlNode main, Uri pageUrl)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? url, HtmlNode? img)
        {
            var resolved = Resolve(pageUrl, url)?.ToString();
            if (resolved is null || !seen.Add(resolved))
            {
                return;
            }

            var image = new IfdbImage { Url = resolved };
            var width = img?.GetAttributeValue("width", 0) ?? 0;
            var height = img?.GetAttributeValue("height", 0) ?? 0;

            // Attributes on a thumbnail describe the thumbnail, so only trust them for the image itself.
            if (width > 0 && height > 0 && string.Equals(ImageSource(img), url, StringComparison.Ordinal))
            {
                image.Width = width;
                image.Height = height;
            }
            else
            {
                // WordPress names resized copies "name-837x390.jpg" (sometimes with a "-1" de-duplication suffix).
                var size = SizeSuffixRegex().Match(resolved);
                if (size.Success)
                {
                    image.Width = int.Parse(size.Groups["w"].Value, CultureInfo.InvariantCulture);
                    image.Height = int.Parse(size.Groups["h"].Value, CultureInfo.InvariantCulture);
                }
            }

            entry.Images.Add(image);
        }

        // Main listing image: the link goes to the full-size file; the <img> is a lazy-loaded thumbnail.
        var mainLink = main.SelectSingleNode(".//div[contains(@class,'jrListingMainImage')]//a[@href]");
        var mainImg = main.SelectSingleNode(".//div[contains(@class,'jrListingMainImage')]//img");
        var mainUrl = mainLink?.GetAttributeValue("href", string.Empty);
        if (!IsImageUrl(mainUrl))
        {
            mainUrl = ImageSource(mainImg);
        }

        Add(mainUrl, mainImg);

        // Photo gallery tab (cover art and similar).
        var galleryLinks = root.SelectNodes("//div[@id='photoTab']//a[img]");
        if (galleryLinks is not null)
        {
            foreach (var link in galleryLinks)
            {
                var href = link.GetAttributeValue("href", string.Empty);
                var img = link.SelectSingleNode(".//img");
                Add(IsImageUrl(href) ? href : ImageSource(img), img);
            }
        }

        // Some entries have a cover-art field instead of a gallery.
        var coverImages = main.SelectNodes(".//div[contains(@class,'jrCoverart')]//div[contains(@class,'jrFieldValue')]//img");
        if (coverImages is not null)
        {
            foreach (var img in coverImages)
            {
                // An element selected from a parsed document always has a parent.
                var parentLink = img.ParentNode!.Name == "a" ? img.ParentNode.GetAttributeValue("href", string.Empty) : null;
                Add(IsImageUrl(parentLink) ? parentLink : ImageSource(img), img);
            }
        }
    }

    private static void AddTrailers(IfdbEntry entry, HtmlNode root, HtmlNode main)
    {
        // The Video tab, else YouTube embeds in the main area only: site-wide embeds in sidebars
        // or footers would otherwise be attached to every entry as a trailer.
        var frames = root.SelectNodes("//div[@id='video']//iframe")
            ?? main.SelectNodes(".//iframe[contains(@src,'youtube') or contains(@data-src,'youtube')]");
        if (frames is null)
        {
            return;
        }

        foreach (var frame in frames)
        {
            var src = frame.GetAttributeValue("src", string.Empty);
            if (!src.Contains("youtu", StringComparison.OrdinalIgnoreCase))
            {
                src = frame.GetAttributeValue("data-src", string.Empty);
            }

            var watch = src.Contains("youtu", StringComparison.OrdinalIgnoreCase) ? IfdbUrls.YouTubeWatchUrl(HtmlEntity.DeEntitize(src)) : null;
            if (watch is not null && !entry.TrailerUrls.Contains(watch, StringComparer.OrdinalIgnoreCase))
            {
                entry.TrailerUrls.Add(watch);
            }
        }
    }

    private static float? ParseRating(HtmlNode scope, string className)
    {
        var block = scope.SelectSingleNode(".//div[contains(@class,'" + className + "')]");
        if (block is null)
        {
            return null;
        }

        // A block with no reviews renders "0.0" and no count; that isn't a real score.
        var count = ParseLeadingInt(block.SelectSingleNode(".//span[contains(@class,'count')]")?.InnerText);
        if (count is null or <= 0)
        {
            return null;
        }

        var valueText = CleanText(block.SelectSingleNode(".//span[contains(@class,'jrRatingValue')]/span[1]")?.InnerText);
        if (!float.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            return null;
        }

        return value;
    }

    private static HtmlNode? FieldNode(HtmlNode scope, string className)
        => scope.SelectSingleNode(".//div[contains(@class,'" + className + "')]//div[contains(@class,'jrFieldValue')]");

    private static string FieldText(HtmlNode scope, string className)
    {
        var node = FieldNode(scope, className);
        return node is null ? string.Empty : MultilineText(node);
    }

    // Reads a multi-value field: list items when present, otherwise links, otherwise the whole text.
    private static List<string> FieldValues(HtmlNode scope, string className)
    {
        var values = new List<string>();
        var node = FieldNode(scope, className);
        if (node is null)
        {
            return values;
        }

        var parts = node.SelectNodes(".//li") ?? node.SelectNodes(".//a");
        IEnumerable<string> texts = parts is null
            ? new[] { CleanText(node.InnerText) }
            : parts.Select(p => CleanText(p.InnerText));

        foreach (var text in texts)
        {
            if (text.Length > 0 && !values.Contains(text, StringComparer.OrdinalIgnoreCase))
            {
                values.Add(text);
            }
        }

        return values;
    }

    // Renders a field as plain text, keeping <br> and paragraph breaks as line breaks and
    // collapsing other whitespace the way a browser does.
    private static string MultilineText(HtmlNode node)
    {
        var markup = BreakTagRegex().Replace(node.InnerHtml, LineBreakToken);
        var text = HtmlEntity.DeEntitize(Load(markup).DocumentNode.InnerText);
        text = WhitespaceRegex().Replace(text, " ");

        var lines = new List<string>();
        foreach (var segment in text.Split(LineBreakToken, StringSplitOptions.None))
        {
            var line = segment.Trim();
            if (line.Length > 0)
            {
                lines.Add(line);
            }
            else if (lines.Count > 0 && lines[^1].Length > 0)
            {
                lines.Add(string.Empty);
            }
        }

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return ScrapedText.Clean(string.Join('\n', lines), ScrapedText.MaxOverviewLength);
    }

    private static string CleanText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var decoded = HtmlEntity.DeEntitize(text);
        return ScrapedText.Clean(WhitespaceRegex().Replace(decoded, " "), ScrapedText.MaxValueLength);
    }

    private static string? NullIfEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    // Gets an image's real address. Listing images are lazy-loaded, with a placeholder GIF in src.
    private static string? ImageSource(HtmlNode? img)
    {
        if (img is null)
        {
            return null;
        }

        foreach (var attribute in ImageSourceAttributes)
        {
            var value = img.GetAttributeValue(attribute, string.Empty).Trim();
            if (value.Length > 0 && !value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    private static bool IsImageUrl(string? url)
        => !string.IsNullOrWhiteSpace(url) && ImageExtensionRegex().IsMatch(url);

    private static Uri? Resolve(Uri baseUrl, string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var value = HtmlEntity.DeEntitize(url.Trim());

        // Jellyfin downloads these addresses from the server, so anything scraped is limited to
        // http(s) on a public host name; see IfdbUrls.IsPublicWebUrl.
        return Uri.TryCreate(baseUrl, value, out var resolved) && IfdbUrls.IsPublicWebUrl(resolved) ? resolved : null;
    }

    private static HtmlDocument Load(string html)
    {
        var doc = new HtmlDocument { OptionDefaultStreamEncoding = Encoding.UTF8 };
        doc.LoadHtml(html);
        return doc;
    }

    [GeneratedRegex(@"tt\d{7,9}")]
    private static partial Regex ImdbIdRegex();

    [GeneratedRegex(@"\d+")]
    private static partial Regex DigitsRegex();

    [GeneratedRegex(@"\b(19|20)\d{2}\b")]
    private static partial Regex YearRegex();

    [GeneratedRegex(@"\bSept\b", RegexOptions.IgnoreCase)]
    private static partial Regex SeptRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"<br\s*/?>|</p>|</li>|</div>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakTagRegex();

    [GeneratedRegex(@"-(?<w>\d{2,5})x(?<h>\d{2,5})(?:-\d+)?\.(?:jpe?g|png|webp|gif)(?:$|\?)", RegexOptions.IgnoreCase)]
    private static partial Regex SizeSuffixRegex();

    [GeneratedRegex(@"\.(?:jpe?g|png|webp|gif)(?:$|\?)", RegexOptions.IgnoreCase)]
    private static partial Regex ImageExtensionRegex();
}
