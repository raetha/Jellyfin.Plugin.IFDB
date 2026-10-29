using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Ifdb.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Ifdb.Providers;

/// <summary>
/// Movie images from the Internet Fanedit Database (fanedit.org).
/// </summary>
public class IfdbMovieImageProvider : IRemoteImageProvider
{
    private readonly IfdbClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="IfdbMovieImageProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public IfdbMovieImageProvider(IHttpClientFactory httpClientFactory, ILogger<IfdbMovieImageProvider> logger)
        : this(IfdbClient.ForServer(httpClientFactory, logger))
    {
    }

    internal IfdbMovieImageProvider(IfdbClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public string Name => Constants.PluginName;

    /// <inheritdoc />
    public bool Supports(BaseItem item) => item is Movie;

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        => [ImageType.Primary, ImageType.Backdrop, ImageType.Thumb];

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        var id = IfdbUrls.NormalizeId(item.GetProviderId(Constants.ProviderIdKey));
        if (id is null)
        {
            return [];
        }

        try
        {
            var entry = await _client.GetEntryAsync(id, cancellationToken).ConfigureAwait(false);
            return entry is null ? [] : IfdbMetadataMapper.ToRemoteImages(entry.Images);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _client.LogFailure(ex, "getting images for \"" + item.Name + "\"");
            return [];
        }
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _client.GetImageResponseAsync(url, cancellationToken);
}
