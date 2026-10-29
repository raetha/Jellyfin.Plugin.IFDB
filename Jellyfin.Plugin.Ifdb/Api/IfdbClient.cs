using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Ifdb.Configuration;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Ifdb.Api;

/// <summary>
/// Fetches and parses fanedit.org pages.
/// </summary>
/// <remarks>
/// On a server every client shares <see cref="IfdbRequestState.Shared"/>, so a library scan doesn't
/// hammer the site: requests run one at a time, spaced by <see cref="PluginConfiguration.RequestDelayMilliseconds"/>;
/// identical concurrent requests share one fetch; and pages are cached for <see cref="PluginConfiguration.CacheMinutes"/>.
/// </remarks>
internal sealed class IfdbClient
{
    private const string HtmlAccept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
    private const string ImageAccept = "image/avif,image/webp,image/*,*/*;q=0.8";

    // fanedit.org pages are around 150 KB; anything far bigger isn't a page we want.
    private const int MaxPageBytes = 5 * 1024 * 1024;

    // Posters and cover art are rarely over a few MB.
    private const int MaxImageBytes = 20 * 1024 * 1024;

    private static readonly TimeSpan BlockedWarningInterval = TimeSpan.FromMinutes(15);

    private static readonly string DefaultUserAgent =
        "Jellyfin-IFDB/" + typeof(IfdbClient).Assembly.GetName().Version!.ToString(3) + " (+" + Constants.ProjectUrl + ")";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;
    private readonly Func<PluginConfiguration> _configuration;
    private readonly IfdbRequestState _state;
    private readonly TimeProvider _time;

    public IfdbClient(
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        Func<PluginConfiguration> configuration,
        IfdbRequestState state,
        TimeProvider time)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _configuration = configuration;
        _state = state;
        _time = time;
    }

    /// <summary>
    /// Gets the User-Agent sent to fanedit.org: the configured one, or "Jellyfin-IFDB/&lt;version&gt; (+&lt;project page&gt;)".
    /// </summary>
    public string UserAgent
    {
        get
        {
            var configured = _configuration().UserAgent?.Trim();
            return string.IsNullOrEmpty(configured) ? DefaultUserAgent : configured;
        }
    }

    /// <summary>
    /// Creates a client for the running server, using the plugin's configuration and the shared request state.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    /// <returns>The client.</returns>
    public static IfdbClient ForServer(IHttpClientFactory httpClientFactory, ILogger logger)
        => new(httpClientFactory, logger, PluginConfigurationProvider.Current, IfdbRequestState.Shared, TimeProvider.System);

    /// <summary>
    /// Searches fanedit.org by title.
    /// </summary>
    /// <param name="keywords">The title keywords.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The results fanedit.org lists, in its own order.</returns>
    public async Task<List<IfdbSearchResult>> SearchAsync(string keywords, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var config = _configuration();
        var url = IfdbUrls.BuildSearchUrl(config.BaseUrl, keywords, config.MatchType);
        var page = await GetPageAsync(url, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(page, url);

        // A single exact-title match redirects straight to that entry's page.
        if (!IfdbUrls.IsSearchUrl(page.FinalUrl))
        {
            var entry = IfdbParser.ParseEntry(page.Html, page.FinalUrl);
            if (entry is null || entry.Id.Length == 0)
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning("IFDB search for {Keywords} redirected to {Url}, which isn't a fanedit entry", keywords, page.FinalUrl);
                }

                return [];
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("IFDB search for {Keywords} went straight to {Url}", keywords, page.FinalUrl);
            }

            return [ToSearchResult(entry)];
        }

        var results = IfdbParser.ParseSearchResults(page.Html, page.FinalUrl);
        if (results.Count == 0 && page.Html.Contains("jrListingTitle", StringComparison.Ordinal))
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning("IFDB search page for {Keywords} has listings but none could be read; fanedit.org's layout may have changed", keywords);
            }
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("IFDB search for {Keywords} found {Count} result(s)", keywords, results.Count);
        }

        var max = Math.Clamp(config.MaxSearchResults, 1, 200);
        return results.Count > max ? results.GetRange(0, max) : results;
    }

    /// <summary>
    /// Gets a fanedit's details.
    /// </summary>
    /// <param name="id">The IFDB id (permalink slug or legacy post number).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The entry, or <c>null</c> if it doesn't exist.</returns>
    public async Task<IfdbEntry?> GetEntryAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var url = IfdbUrls.BuildEntryUrl(_configuration().BaseUrl, id);
        var page = await GetPageAsync(url, cancellationToken).ConfigureAwait(false);
        if (page.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning("IFDB entry {Id} was not found at {Url}", id, url);
            }

            return null;
        }

        EnsureSuccess(page, url);
        var entry = IfdbParser.ParseEntry(page.Html, page.FinalUrl);
        if (entry is null)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning("IFDB page {Url} for id {Id} isn't a readable fanedit entry (page title: {Title})", page.FinalUrl, id, IfdbParser.PageTitle(page.Html));
            }

            return null;
        }

        if (entry.Id.Length == 0)
        {
            entry.Id = id;
        }

        return entry;
    }

    /// <summary>
    /// Downloads an image for Jellyfin to save. Only a real JPEG, PNG, GIF or WebP file of at most
    /// 20 MB is passed on, labelled with the type its content shows rather than what the server
    /// claimed. Anything else comes back as 404, which makes Jellyfin skip to the next image.
    /// </summary>
    /// <param name="url">The image URL.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentException">The URL isn't an http(s) address on a public host.</exception>
    public async Task<HttpResponseMessage> GetImageResponseAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IfdbUrls.IsPublicWebUrl(uri))
        {
            throw new ArgumentException("Only http(s) images on public hosts are downloaded: " + url, nameof(url));
        }

        using var request = CreateRequest(url, ImageAccept);
        using var response = await _httpClientFactory.CreateClient(NamedClient.Default)
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new HttpResponseMessage(response.StatusCode) { RequestMessage = new HttpRequestMessage(HttpMethod.Get, uri) };
        }

        byte[] bytes;
        string? mediaType;
        try
        {
            bytes = await ReadLimitedAsync(response.Content, MaxImageBytes, "image", url, cancellationToken).ConfigureAwait(false);
            mediaType = ImageSniffer.DetectMediaType(bytes);
        }
        catch (HttpRequestException ex)
        {
            return RejectImage(uri, ex.Message);
        }

        if (mediaType is null)
        {
            return RejectImage(uri, "not a JPEG, PNG, GIF or WebP image (sent as " + response.Content.Headers.ContentType?.MediaType + ")");
        }

        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content,
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, response.RequestMessage!.RequestUri),
        };
    }

    /// <summary>
    /// Logs a failed fanedit.org call. Bot-protection blocks get one clear error every 15 minutes,
    /// rather than one per item during a library scan.
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <param name="action">What was being done, for the message.</param>
    public void LogFailure(Exception exception, string action)
    {
        switch (exception)
        {
            case IfdbBlockedException when _state.TryClaimBlockedWarning(_time.GetUtcNow(), BlockedWarningInterval):
                if (_logger.IsEnabled(LogLevel.Error))
                {
                    _logger.LogError(
                        "IFDB: fanedit.org blocked the request while {Action} ({Reason}). Its bot protection is refusing requests from this server. Try again later, or set a different User-Agent in the IFDB plugin settings (currently {UserAgent}).",
                        action,
                        exception.Message,
                        UserAgent);
                }

                break;
            case IfdbBlockedException:
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("IFDB: fanedit.org blocked the request while {Action}", action);
                }

                break;
            case OperationCanceledException:
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning("IFDB: timed out while {Action}", action);
                }

                break;
            default:
                _logger.LogError(exception, "IFDB: failed while {Action}", action);
                break;
        }
    }

    private static IfdbSearchResult ToSearchResult(IfdbEntry entry)
        => new()
        {
            Id = entry.Id,
            Title = entry.Title,
            Year = entry.ReleaseDate?.Year,
            ImageUrl = entry.PosterUrl,
            Overview = entry.Synopsis,
        };

    private static void EnsureSuccess(IfdbPage page, string url)
    {
        if ((int)page.StatusCode is < 200 or > 299)
        {
            throw new HttpRequestException("fanedit.org returned HTTP " + (int)page.StatusCode + " for " + url, null, page.StatusCode);
        }
    }

    private HttpRequestMessage CreateRequest(string url, string accept)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", accept);
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.8");
        return request;
    }

    private async Task<IfdbPage> GetPageAsync(string url, CancellationToken cancellationToken)
    {
        var config = _configuration();
        var cacheFor = TimeSpan.FromMinutes(Math.Clamp(config.CacheMinutes, 0, 1440));
        var now = _time.GetUtcNow();

        if (cacheFor > TimeSpan.Zero && _state.TryGetPage(url, now, out var cached))
        {
            return await cached.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        // The fetch isn't tied to this caller's token, so other callers sharing it aren't cancelled with it.
        var fetch = FetchAsync(url, config);
        if (cacheFor > TimeSpan.Zero)
        {
            _state.SetPage(url, fetch, now + cacheFor, now);
        }

        IfdbPage page;
        try
        {
            page = await fetch.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (fetch.IsFaulted || fetch.IsCanceled)
        {
            _state.RemovePage(url);
            throw;
        }

        // Server errors are worth retrying soon, so only pages and missing entries stay cached.
        var cacheable = page.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone || (int)page.StatusCode is >= 200 and <= 299;
        if (!cacheable)
        {
            _state.RemovePage(url);
            return page;
        }

        // Also cache an entry page under the address a redirect led to, so a later lookup by id reuses it.
        var finalUrl = page.FinalUrl.ToString();
        if (cacheFor > TimeSpan.Zero && !string.Equals(finalUrl, url, StringComparison.Ordinal))
        {
            _state.SetPage(finalUrl, fetch, now + cacheFor, now);
        }

        return page;
    }

    private async Task<IfdbPage> FetchAsync(string url, PluginConfiguration config)
    {
        // Waiting in the queue doesn't count toward the timeout: during a big scan the queue can be
        // long, and the request ahead always finishes or times out, so the wait is bounded.
        await _state.RequestGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var delay = TimeSpan.FromMilliseconds(Math.Clamp(config.RequestDelayMilliseconds, 0, 60000));
            var wait = _state.LastRequest + delay - _time.GetUtcNow();
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, _time).ConfigureAwait(false);
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(config.RequestTimeoutSeconds, 5, 300)), _time);

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("IFDB: fetching {Url}", url);
            }

            using var request = CreateRequest(url, HtmlAccept);
            using var response = await _httpClientFactory.CreateClient(NamedClient.Default)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            var bytes = await ReadLimitedAsync(response.Content, MaxPageBytes, "page", url, timeout.Token).ConfigureAwait(false);
            var html = DecodeText(bytes);
            _state.LastRequest = _time.GetUtcNow();

            if (response.Headers.Contains("cf-mitigated") || IfdbParser.IsChallengePage(html))
            {
                throw new IfdbBlockedException("HTTP " + (int)response.StatusCode + " bot-protection challenge for " + url);
            }

            // HttpClient always sets RequestMessage to the last request it sent, after any redirects.
            return new IfdbPage(response.RequestMessage!.RequestUri!, response.StatusCode, html);
        }
        finally
        {
            _state.RequestGate.Release();
        }
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, int maxBytes, string what, string url, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maxBytes)
        {
            throw new HttpRequestException("fanedit.org " + what + " is too large (" + content.Headers.ContentLength + " bytes): " + url);
        }

        var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                {
                    throw new HttpRequestException("fanedit.org " + what + " is larger than " + maxBytes + " bytes: " + url);
                }

                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            return buffer.ToArray();
        }
    }

    // fanedit.org serves UTF-8; a byte-order mark, if any, still wins.
    private static string DecodeText(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private HttpResponseMessage RejectImage(Uri uri, string reason)
    {
        if (_logger.IsEnabled(LogLevel.Warning))
        {
            _logger.LogWarning("IFDB: skipped image {Url}: {Reason}", uri, reason);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = new HttpRequestMessage(HttpMethod.Get, uri) };
    }
}
