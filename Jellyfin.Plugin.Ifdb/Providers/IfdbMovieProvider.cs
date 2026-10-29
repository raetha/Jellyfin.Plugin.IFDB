using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Ifdb.Api;
using Jellyfin.Plugin.Ifdb.Configuration;
using Jellyfin.Plugin.Ifdb.Matching;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Ifdb.Providers;

/// <summary>
/// Movie metadata from the Internet Fanedit Database (fanedit.org).
/// </summary>
public class IfdbMovieProvider : IRemoteMetadataProvider<Movie, MovieInfo>
{
    private readonly IfdbClient _client;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger _logger;
    private readonly Func<PluginConfiguration> _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="IfdbMovieProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="libraryManager">The library manager, used to clean up file names before searching.</param>
    /// <param name="logger">The logger.</param>
    public IfdbMovieProvider(IHttpClientFactory httpClientFactory, ILibraryManager libraryManager, ILogger<IfdbMovieProvider> logger)
        : this(IfdbClient.ForServer(httpClientFactory, logger), libraryManager, logger, PluginConfigurationProvider.Current)
    {
    }

    internal IfdbMovieProvider(IfdbClient client, ILibraryManager libraryManager, ILogger logger, Func<PluginConfiguration> configuration)
    {
        _client = client;
        _libraryManager = libraryManager;
        _logger = logger;
        _configuration = configuration;
    }

    /// <inheritdoc />
    public string Name => Constants.PluginName;

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo, CancellationToken cancellationToken)
    {
        try
        {
            // An IFDB id (or pasted fanedit.org URL) entered in the Identify dialog is looked up directly.
            var id = IfdbUrls.NormalizeId(searchInfo.GetProviderId(Constants.ProviderIdKey));
            if (id is not null)
            {
                var entry = await _client.GetEntryAsync(id, cancellationToken).ConfigureAwait(false);
                return entry is null ? [] : [IfdbMetadataMapper.ToRemoteSearchResult(entry)];
            }

            var title = CleanTitle(searchInfo.Name);
            if (title.Length == 0)
            {
                return [];
            }

            var scored = await IfdbMatcher.SearchAsync(_client, title, cancellationToken).ConfigureAwait(false);
            return IfdbMatcher.PickSearchResults(scored, _configuration().SearchMinimumScore)
                .Select(s => IfdbMetadataMapper.ToRemoteSearchResult(s.Result))
                .ToList();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _client.LogFailure(ex, "searching for \"" + searchInfo.Name + "\"");
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
    {
        try
        {
            var config = _configuration();
            var id = IfdbUrls.NormalizeId(info.GetProviderId(Constants.ProviderIdKey))
                ?? await FindMatchAsync(info, config, cancellationToken).ConfigureAwait(false);
            var entry = id is null ? null : await _client.GetEntryAsync(id, cancellationToken).ConfigureAwait(false);
            return entry is null ? new MetadataResult<Movie>() : IfdbMetadataMapper.ToMetadataResult(entry, config);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _client.LogFailure(ex, "getting metadata for \"" + info.Name + "\"");
            return new MetadataResult<Movie>();
        }
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _client.GetImageResponseAsync(url, cancellationToken);

    private async Task<string?> FindMatchAsync(MovieInfo info, PluginConfiguration config, CancellationToken cancellationToken)
    {
        var title = CleanTitle(info.Name);
        if (title.Length == 0)
        {
            return null;
        }

        var scored = await IfdbMatcher.SearchAsync(_client, title, cancellationToken).ConfigureAwait(false);
        var match = IfdbMatcher.PickAutomaticMatch(scored, config.AutoMatchMinimumScore, info.Year);
        if (_logger.IsEnabled(LogLevel.Information))
        {
            if (match is not null)
            {
                _logger.LogInformation("IFDB matched \"{Title}\" to \"{Match}\" ({Id}, score {Score})", title, match.Result.Title, match.Result.Id, match.Score);
            }
            else
            {
                _logger.LogInformation("IFDB found no single match for \"{Title}\" scoring {Minimum} or more among {Count} result(s); use Identify to pick one", title, config.AutoMatchMinimumScore, scored.Count);
            }
        }

        return match?.Result.Id;
    }

    // Strips years, release tags and other file-name clutter the way the built-in providers do.
    private string CleanTitle(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var parsed = _libraryManager.ParseName(name)?.Name;
        return string.IsNullOrWhiteSpace(parsed) ? name.Trim() : parsed.Trim();
    }
}
