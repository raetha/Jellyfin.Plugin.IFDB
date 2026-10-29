using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly ILogger<IfdbMovieImageProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IfdbMovieImageProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public IfdbMovieImageProvider(IHttpClientFactory httpClientFactory, ILogger<IfdbMovieImageProvider> logger)
    {
        _client = new IfdbClient(httpClientFactory, logger);
        _logger = logger;
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
            return entry is null ? [] : ToRemoteImages(entry.Images);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            IfdbClient.LogFailure(_logger, ex, "getting images for \"" + item.Name + "\"");
            return [];
        }
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _client.GetImageResponseAsync(url, cancellationToken);

    /// <summary>
    /// Sorts fanedit images into Jellyfin image types. Portrait (or unknown-shape) images are
    /// offered as posters; clearly landscape ones, such as "Fanedit of the Month" banners, as
    /// backdrops and thumbs. A landscape image is only offered as a poster when there's nothing else.
    /// </summary>
    /// <param name="images">The entry's images, main image first.</param>
    /// <returns>The images to offer.</returns>
    internal static List<RemoteImageInfo> ToRemoteImages(IReadOnlyList<IfdbImage> images)
    {
        var result = new List<RemoteImageInfo>();
        foreach (var image in images)
        {
            if (image.IsLandscape)
            {
                result.Add(ToRemoteImage(image, ImageType.Backdrop));
                result.Add(ToRemoteImage(image, ImageType.Thumb));
            }
            else
            {
                result.Add(ToRemoteImage(image, ImageType.Primary));
            }
        }

        if (!result.Any(i => i.Type == ImageType.Primary) && images.Count > 0)
        {
            result.Add(ToRemoteImage(images[0], ImageType.Primary));
        }

        return result;
    }

    private static RemoteImageInfo ToRemoteImage(IfdbImage image, ImageType type)
        => new()
        {
            ProviderName = Constants.PluginName,
            Url = image.Url,
            Type = type,
            Width = image.Width,
            Height = image.Height,
        };
}
