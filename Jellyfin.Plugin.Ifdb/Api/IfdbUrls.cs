using System;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.Ifdb.Configuration;

namespace Jellyfin.Plugin.Ifdb.Api;

/// <summary>
/// Builds fanedit.org URLs and converts between URLs and IFDB ids.
/// </summary>
/// <remarks>
/// An IFDB id is the entry's permalink slug, for example "m4s-the-hobbit-book-edit".
/// Version 1.10.11 stored WordPress post numbers (for example "17071"); those still resolve,
/// because fanedit.org redirects "/?p=17071" to the entry's permalink, and are replaced by the
/// slug on the item's next refresh.
/// </remarks>
internal static class IfdbUrls
{
    private static readonly string[] PrivateHostSuffixes = [".localhost", ".local", ".internal", ".lan", ".home.arpa"];

    public static string NormalizeBaseUrl(string? baseUrl)
    {
        var value = baseUrl?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return Constants.DefaultBaseUrl;
        }

        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "https://" + value;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return Constants.DefaultBaseUrl;
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    public static string BuildSearchUrl(string baseUrl, string keywords, SearchMatchType matchType)
    {
        var query = matchType switch
        {
            SearchMatchType.Any => "any",
            SearchMatchType.Exact => "exact",
            _ => "all"
        };

        return NormalizeBaseUrl(baseUrl)
            + Constants.SearchPath
            + "?query=" + query
            + "&scope=title&keywords=" + Uri.EscapeDataString(keywords)
            + "&order=rdate";
    }

    public static string BuildEntryUrl(string baseUrl, string id)
    {
        var root = NormalizeBaseUrl(baseUrl);
        if (IsPostNumber(id))
        {
            return root + "/?p=" + id;
        }

        var segments = id.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString);
        return root + "/" + string.Join('/', segments) + "/";
    }

    /// <summary>
    /// Turns whatever a user or an older plugin version stored as an IFDB id into a canonical id.
    /// Accepts a bare slug, a post number, or a full fanedit.org URL.
    /// </summary>
    /// <param name="value">The stored or entered value.</param>
    /// <returns>The id, or <c>null</c> when the value doesn't identify an entry.</returns>
    public static string? NormalizeId(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Contains("://", StringComparison.Ordinal)
            || trimmed.StartsWith("fanedit.org", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("ifdb.fanedit.org", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("www.fanedit.org", StringComparison.OrdinalIgnoreCase))
        {
            var candidate = trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : "https://" + trimmed;
            return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ? IdFromUrl(uri) : null;
        }

        if (trimmed.StartsWith("?p=", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("/?p=", StringComparison.OrdinalIgnoreCase))
        {
            var number = trimmed[(trimmed.IndexOf('=', StringComparison.Ordinal) + 1)..];
            return IsPostNumber(number) ? number : null;
        }

        var slug = trimmed.Trim('/');
        return slug.Length == 0 ? null : slug;
    }

    /// <summary>
    /// Gets the IFDB id an entry URL points at. Entry pages live at "/&lt;slug&gt;/" (or "/ifdb/&lt;slug&gt;/"
    /// on the older layout); deeper paths such as "/&lt;slug&gt;/reviews/" are pages of that entry.
    /// </summary>
    /// <param name="url">A fanedit.org entry URL.</param>
    /// <returns>The id, or <c>null</c> for URLs that aren't entry pages.</returns>
    public static string? IdFromUrl(Uri url)
    {
        var segments = Uri.UnescapeDataString(url.AbsolutePath).Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length > 0)
        {
            if (segments[0].StartsWith("fanedit-search", StringComparison.OrdinalIgnoreCase)
                || segments[0].StartsWith("wp-", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return string.Equals(segments[0], "ifdb", StringComparison.OrdinalIgnoreCase) && segments.Length > 1
                ? segments[0] + "/" + segments[1]
                : segments[0];
        }

        var postNumber = GetQueryValue(url, "p");
        return IsPostNumber(postNumber) ? postNumber : null;
    }

    /// <summary>
    /// Gets a value indicating whether a fetched URL is still a search results page,
    /// as opposed to an entry page fanedit.org redirected to when exactly one title matched.
    /// </summary>
    /// <param name="url">The URL that was finally loaded.</param>
    /// <returns><c>true</c> for a search results page.</returns>
    public static bool IsSearchUrl(Uri url)
        => url.AbsolutePath.Contains(Constants.SearchPath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    public static bool IsPostNumber(string? value)
        => !string.IsNullOrEmpty(value) && value.All(char.IsAsciiDigit);

    /// <summary>
    /// Gets a value indicating whether an address is safe for the server to download: http(s) on a
    /// public host name. Addresses scraped from pages are checked with this so a bad listing can't
    /// make Jellyfin fetch IP addresses, localhost or local-network names (server-side request forgery).
    /// </summary>
    /// <param name="url">The address.</param>
    /// <returns><c>true</c> if it may be fetched.</returns>
    public static bool IsPublicWebUrl(Uri url)
    {
        if (!url.IsAbsoluteUri
            || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)
            || url.HostNameType != UriHostNameType.Dns)
        {
            return false;
        }

        var host = url.IdnHost.TrimEnd('.');
        return host.Contains('.', StringComparison.Ordinal)
            && !string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            && !PrivateHostSuffixes.Any(suffix => host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Converts a YouTube embed URL into a normal watch URL, which Jellyfin clients can open.
    /// </summary>
    /// <param name="url">The iframe URL.</param>
    /// <returns>The watch URL, or <c>null</c> if no video id is found.</returns>
    public static string? YouTubeWatchUrl(string url)
    {
        var src = url.StartsWith("//", StringComparison.Ordinal) ? "https:" + url : url;
        if (!Uri.TryCreate(src, UriKind.Absolute, out var uri))
        {
            return null;
        }

        string? videoId = null;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var embedIndex = Array.FindIndex(segments, s => string.Equals(s, "embed", StringComparison.OrdinalIgnoreCase));
        if (embedIndex >= 0 && embedIndex + 1 < segments.Length)
        {
            videoId = segments[embedIndex + 1];
        }
        else if (uri.Host.EndsWith("youtu.be", StringComparison.OrdinalIgnoreCase) && segments.Length > 0)
        {
            videoId = segments[0];
        }
        else
        {
            videoId = GetQueryValue(uri, "v");
        }

        if (string.IsNullOrEmpty(videoId) || string.Equals(videoId, "videoseries", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return string.Create(CultureInfo.InvariantCulture, $"https://www.youtube.com/watch?v={Uri.EscapeDataString(videoId)}");
    }

    private static string? GetQueryValue(Uri url, string name)
    {
        var query = url.Query.TrimStart('?');
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            var key = separator < 0 ? pair : pair[..separator];
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return separator < 0 ? string.Empty : Uri.UnescapeDataString(pair[(separator + 1)..]);
            }
        }

        return null;
    }
}
