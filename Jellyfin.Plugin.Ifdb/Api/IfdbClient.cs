using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
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
/// Requests from every provider instance share one queue and cache, so a library scan doesn't
/// hammer the site: requests are spaced out by <see cref="PluginConfiguration.RequestDelayMilliseconds"/>,
/// identical concurrent requests share one fetch, and pages are cached for <see cref="PluginConfiguration.CacheMinutes"/>.
/// </remarks>
internal sealed class IfdbClient
{
    private const int MaxCachedPages = 500;

    private static readonly ConcurrentDictionary<string, CachedPage> PageCache = new(StringComparer.Ordinal);
    private static readonly SemaphoreSlim RequestGate = new(1, 1);
    private static readonly TimeSpan BlockedWarningInterval = TimeSpan.FromMinutes(15);

    private static long _lastRequestTicks;
    private static long _lastBlockedWarningTicks;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;

    public IfdbClient(IHttpClientFactory httpClientFactory, ILogger logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    /// <summary>
    /// Gets the User-Agent sent to fanedit.org.
    /// </summary>
    public static string UserAgent
    {
        get
        {
            var configured = Config.UserAgent?.Trim();
            if (!string.IsNullOrEmpty(configured))
            {
                return configured;
            }

            var version = typeof(IfdbClient).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
            return "Jellyfin-IFDB/" + version + " (+" + Constants.ProjectUrl + ")";
        }
    }

    /// <summary>
    /// Searches fanedit.org by title.
    /// </summary>
    /// <param name="keywords">The title keywords.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The results fanedit.org lists, best first by its own ordering.</returns>
    public async Task<List<IfdbSearchResult>> SearchAsync(string keywords, CancellationToken cancellationToken)
    {
        var config = Config;
        var url = IfdbUrls.BuildSearchUrl(config.BaseUrl, keywords, config.MatchType);
        var page = await GetPageAsync(url, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(page, url);

        // A single exact-title match redirects straight to that entry's page.
        if (!IfdbUrls.IsSearchUrl(page.FinalUrl))
        {
            var entry = IfdbParser.ParseEntry(page.Html, page.FinalUrl);
            if (entry is null || entry.Id.Length == 0)
            {
                _logger.LogWarning("IFDB search for {Keywords} redirected to {Url}, which isn't a fanedit entry", keywords, page.FinalUrl);
                return [];
            }

            _logger.LogDebug("IFDB search for {Keywords} went straight to {Url}", keywords, page.FinalUrl);
            return [ToSearchResult(entry)];
        }

        var results = IfdbParser.ParseSearchResults(page.Html, page.FinalUrl);
        if (results.Count == 0 && page.Html.Contains("jrListingTitle", StringComparison.Ordinal))
        {
            _logger.LogWarning("IFDB search page for {Keywords} has listings but none could be read; fanedit.org's layout may have changed", keywords);
        }

        var max = Math.Clamp(config.MaxSearchResults, 1, 200);
        _logger.LogDebug("IFDB search for {Keywords} found {Count} result(s)", keywords, results.Count);
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
        var url = IfdbUrls.BuildEntryUrl(Config.BaseUrl, id);
        var page = await GetPageAsync(url, cancellationToken).ConfigureAwait(false);
        if (page.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("IFDB entry {Id} was not found at {Url}", id, url);
            return null;
        }

        EnsureSuccess(page, url);
        var entry = IfdbParser.ParseEntry(page.Html, page.FinalUrl);
        if (entry is null)
        {
            _logger.LogWarning("IFDB page {Url} for id {Id} isn't a readable fanedit entry (page title: {Title})", page.FinalUrl, id, IfdbParser.PageTitle(page.Html));
            return null;
        }

        if (entry.Id.Length == 0)
        {
            entry.Id = id;
        }

        return entry;
    }

    /// <summary>
    /// Downloads an image, sending the same headers as page requests.
    /// </summary>
    /// <param name="url">The image URL.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The response.</returns>
    public Task<HttpResponseMessage> GetImageResponseAsync(string url, CancellationToken cancellationToken)
    {
        var request = CreateRequest(url, "image/avif,image/webp,image/*,*/*;q=0.8");
        return _httpClientFactory.CreateClient(NamedClient.Default).SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Logs a failed fanedit.org call. Bot-protection blocks get one clear warning every
    /// 15 minutes instead of an error per item during a scan.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="exception">The failure.</param>
    /// <param name="action">What was being done, for the message.</param>
    public static void LogFailure(ILogger logger, Exception exception, string action)
    {
        if (exception is IfdbBlockedException)
        {
            var now = DateTime.UtcNow.Ticks;
            var last = Interlocked.Read(ref _lastBlockedWarningTicks);
            if (now - last > BlockedWarningInterval.Ticks
                && Interlocked.CompareExchange(ref _lastBlockedWarningTicks, now, last) == last)
            {
                logger.LogError(
                    "IFDB: fanedit.org blocked the request while {Action}: {Message}. Its bot protection is refusing requests from this server. Try again later, or set a different User-Agent in the IFDB plugin settings (currently {UserAgent}).",
                    action,
                    exception.Message,
                    UserAgent);
            }
            else
            {
                logger.LogDebug("IFDB: fanedit.org blocked the request while {Action}", action);
            }

            return;
        }

        if (exception is OperationCanceledException)
        {
            logger.LogWarning("IFDB: timed out or cancelled while {Action}", action);
            return;
        }

        logger.LogError(exception, "IFDB: failed while {Action}", action);
    }

    private static IfdbSearchResult ToSearchResult(IfdbEntry entry)
        => new()
        {
            Id = entry.Id,
            Url = entry.Url,
            Title = entry.Title,
            Year = entry.ReleaseDate?.Year,
            ImageUrl = entry.Images.FirstOrDefault(i => !i.IsLandscape)?.Url ?? entry.Images.FirstOrDefault()?.Url,
            Overview = entry.Synopsis,
        };

    private static void EnsureSuccess(IfdbPage page, string url)
    {
        if ((int)page.StatusCode is < 200 or > 299)
        {
            throw new HttpRequestException("fanedit.org returned " + (int)page.StatusCode + " for " + url, null, page.StatusCode);
        }
    }

    private static HttpRequestMessage CreateRequest(string url, string accept)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", accept);
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.8");
        return request;
    }

    private async Task<IfdbPage> GetPageAsync(string url, CancellationToken cancellationToken)
    {
        var cacheMinutes = Math.Max(0, Config.CacheMinutes);
        var now = DateTime.UtcNow;

        if (cacheMinutes > 0
            && PageCache.TryGetValue(url, out var cached)
            && cached.ExpiresUtc > now
            && !cached.Fetch.IsFaulted
            && !cached.Fetch.IsCanceled)
        {
            return await cached.Fetch.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        // The fetch isn't tied to this caller's token, so other callers sharing it aren't cancelled with it.
        var fetch = FetchAsync(url);
        if (cacheMinutes > 0)
        {
            PruneCache(now);
            PageCache[url] = new CachedPage(fetch, now.AddMinutes(cacheMinutes));
        }

        IfdbPage page;
        try
        {
            page = await fetch.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (fetch.IsFaulted || fetch.IsCanceled)
        {
            PageCache.TryRemove(url, out _);
            throw;
        }

        // Also cache an entry page under the address a redirect led to, so a later lookup by id reuses it.
        var finalUrl = page.FinalUrl.ToString();
        if (cacheMinutes > 0 && !string.Equals(finalUrl, url, StringComparison.Ordinal))
        {
            PageCache[finalUrl] = new CachedPage(fetch, now.AddMinutes(cacheMinutes));
        }

        return page;
    }

    private async Task<IfdbPage> FetchAsync(string url)
    {
        var config = Config;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(config.RequestTimeoutSeconds, 5, 300)));

        await RequestGate.WaitAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var delay = TimeSpan.FromMilliseconds(Math.Clamp(config.RequestDelayMilliseconds, 0, 60000));
            var wait = new DateTime(Interlocked.Read(ref _lastRequestTicks), DateTimeKind.Utc) + delay - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, timeout.Token).ConfigureAwait(false);
            }

            _logger.LogDebug("IFDB: fetching {Url}", url);
            using var request = CreateRequest(url, "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            using var response = await _httpClientFactory.CreateClient(NamedClient.Default)
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token)
                .ConfigureAwait(false);
            var html = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            Interlocked.Exchange(ref _lastRequestTicks, DateTime.UtcNow.Ticks);

            var mitigated = response.Headers.TryGetValues("cf-mitigated", out var values) ? values.FirstOrDefault() : null;
            if (!string.IsNullOrEmpty(mitigated) || IfdbParser.IsChallengePage(html))
            {
                throw new IfdbBlockedException("HTTP " + (int)response.StatusCode + " bot-protection challenge for " + url);
            }

            var finalUrl = response.RequestMessage?.RequestUri ?? new Uri(url);
            return new IfdbPage(finalUrl, response.StatusCode, html);
        }
        finally
        {
            RequestGate.Release();
        }
    }

    private static void PruneCache(DateTime now)
    {
        if (PageCache.Count < MaxCachedPages)
        {
            return;
        }

        foreach (var item in PageCache)
        {
            if (item.Value.ExpiresUtc <= now)
            {
                PageCache.TryRemove(item.Key, out _);
            }
        }

        // Still full of live pages: drop the ones closest to expiring.
        var excess = PageCache.Count - (MaxCachedPages * 3 / 4);
        if (excess > 0)
        {
            foreach (var key in PageCache.OrderBy(i => i.Value.ExpiresUtc).Take(excess).Select(i => i.Key).ToList())
            {
                PageCache.TryRemove(key, out _);
            }
        }
    }

    private sealed class CachedPage
    {
        public CachedPage(Task<IfdbPage> fetch, DateTime expiresUtc)
        {
            Fetch = fetch;
            ExpiresUtc = expiresUtc;
        }

        public Task<IfdbPage> Fetch { get; }

        public DateTime ExpiresUtc { get; }
    }
}
