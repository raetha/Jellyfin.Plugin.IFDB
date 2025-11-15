using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
// using SixLabors.ImageSharp.Metadata;
// using SixLabors.ImageSharp.PixelFormats;

namespace Jellyfin.Plugin.Ifdb.Providers
{
    /// <summary>
    /// The IFDB movie image provider.
    /// </summary>
    public class IfdbMovieImageProvider : IRemoteImageProvider
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<IfdbMovieImageProvider> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="IfdbMovieImageProvider"/> class.
        /// </summary>
        /// <param name="logger">The logger used for diagnostic messages.</param>
        /// <param name="httpClientFactory">The HTTP client used to perform web requests.</param>
        public IfdbMovieImageProvider(IHttpClientFactory httpClientFactory, ILogger<IfdbMovieImageProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        /// <summary>
        /// Gets the name of the metadata provider.
        /// </summary>
        public string Name => Constants.PluginName;

        /// <inheritdoc />
        public bool Supports(BaseItem item)
        {
            return item is Movie;
        }

        /// <inheritdoc />
        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            yield return ImageType.Primary;
            yield return ImageType.Thumb;
        }

        /// <summary>
        /// Retrieves image for item.
        /// </summary>
        /// <param name="item">The movie to search for.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous search operation, containing the search results.</returns>
        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();

            if (!item.ProviderIds.TryGetValue(Constants.PluginName, out var id))
            {
                _logger.LogWarning("No IFDB provider ID found for movie {Name}", item.Name);
                return images;
            }

            #pragma warning disable CA1863
            var detailsUrl = string.Format(CultureInfo.InvariantCulture, Constants.IfdbMovieURL, id);
            _logger.LogInformation("Fetching IFDB images for {Name} from {Url}", item.Name, detailsUrl);

            try
            {
                var client = _httpClientFactory.CreateClient();
                var html = await client.GetStringAsync(detailsUrl, cancellationToken).ConfigureAwait(false);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var primaryUrl = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'jrListingMainImage')]//a")
                    ?.GetAttributeValue("href", null);

                if (!string.IsNullOrWhiteSpace(primaryUrl))
                {
                    var (primaryWidth, primaryHeight) = await GetImageDimensionsAsync(primaryUrl, cancellationToken).ConfigureAwait(false);

                    images.Add(new RemoteImageInfo
                    {
                        ProviderName = Name,
                        Url = primaryUrl,
                        Type = ImageType.Primary,
                        Width = primaryWidth,
                        Height = primaryHeight,
                        Language = "en",
                    });
                }

                var thumbUrl = doc.DocumentNode.SelectSingleNode("//input[contains(@class,'jrCheckListing')]")
                    ?.GetAttributeValue("data-thumburl", null);

                if (!string.IsNullOrWhiteSpace(thumbUrl))
                {
                    var (thumbWidth, thumbHeight) = await GetImageDimensionsAsync(thumbUrl, cancellationToken).ConfigureAwait(false);

                    images.Add(new RemoteImageInfo
                    {
                        ProviderName = Name,
                        Url = thumbUrl,
                        Type = ImageType.Thumb,
                        Width = thumbWidth,
                        Height = thumbHeight,
                        Language = "en",
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch or parse IFDB images for movie {Name}", item.Name);
                return images;
            }

            return images;
        }

        /// <inheritdoc />
        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(new Uri(url), cancellationToken);
        }

        private async Task<(int Width, int Height)> GetImageDimensionsAsync(string url, CancellationToken cancellationToken)
        {
            try
            {
                var client = _httpClientFactory.CreateClient(NamedClient.Default);
                using var stream = await client.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);

                // Use ImageSharp to load metadata without decoding entire image
                using var image = await Image.LoadAsync(stream, cancellationToken).ConfigureAwait(false);
                return (image.Width, image.Height);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read image dimensions from {Url}", url);
                return (0, 0);
            }
        }
    }
}
