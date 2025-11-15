using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
// using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
// using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Ifdb.Providers
{
    /// <summary>
    /// The IFDB movie provider.
    /// </summary>
    public class IfdbMovieProvider : IRemoteMetadataProvider<Movie, MovieInfo>
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<IfdbMovieProvider> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="IfdbMovieProvider"/> class.
        /// </summary>
        /// <param name="logger">The logger used for diagnostic messages.</param>
        /// <param name="httpClientFactory">The HTTP client used to perform web requests.</param>
        public IfdbMovieProvider(IHttpClientFactory httpClientFactory, ILogger<IfdbMovieProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        /// <summary>
        /// Gets the name of the metadata provider.
        /// </summary>
        public string Name => Constants.PluginName;

        /// <summary>
        /// Searches for remote results for a given movie item.
        /// </summary>
        /// <param name="searchInfo">The movie to search for.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous search operation, containing the search results.</returns>
        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo, CancellationToken cancellationToken)
        {
            var query = Uri.EscapeDataString(searchInfo.Name ?? string.Empty);
            #pragma warning disable CA1863
            var searchUrl = string.Format(CultureInfo.InvariantCulture, Constants.IfdbSearchURL, query);
            _logger.LogInformation("Fetching IFDB search results from {Url}", searchUrl);

            try
            {
                var client = _httpClientFactory.CreateClient();
                var html = await client.GetStringAsync(searchUrl, cancellationToken).ConfigureAwait(false);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // Find all result containers
                var listingNodes = doc.DocumentNode.SelectNodes("//div[contains(@class,'jrRow')]");
                if (listingNodes == null || listingNodes.Count == 0)
                {
                    return Enumerable.Empty<RemoteSearchResult>();
                }

                var results = new List<RemoteSearchResult>();
                int index = 1;

                foreach (var node in listingNodes)
                    {
                        try
                        {
                            var checkBox = node.SelectSingleNode(".//input[contains(@class,'jrCheckListing')]");
                            if (checkBox == null)
                            {
                                continue;
                            }

                            // Extract values directly from checkbox data attributes
                            var id = checkBox.GetAttributeValue("value", null);
                            if (string.IsNullOrWhiteSpace(id))
                            {
                                continue;
                            }

                            var title = checkBox.GetAttributeValue("data-listingtitle", null);
                            var thumbUrl = checkBox.GetAttributeValue("data-thumburl", null);
                            var listingUrl = checkBox.GetAttributeValue("data-listingurl", null);

                            // Fallback for title if data attribute missing
                            if (string.IsNullOrWhiteSpace(title))
                            {
                                title = node.SelectSingleNode(".//div[@class='jrListingTitle']/a")?.InnerText?.Trim();
                            }

                            // Extract ProductionYear from "Fanedit Release Date" field
                            int? productionYear = null;
                            var releaseDateNode = node.SelectSingleNode(".//div[contains(@class,'jrFaneditreleasedate')]//a");
                            if (releaseDateNode != null)
                            {
                                var dateText = releaseDateNode.InnerText.Trim(); // e.g. "July 2025"
                                var parts = dateText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length == 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year))
                                {
                                    productionYear = year;
                                }
                            }

                            // Extract synopsis / overview
                            var overview = node.SelectSingleNode(".//div[contains(@class,'jrBriefsynopsis')]//div[@class='jrFieldValue']")?.InnerText?.Trim();

                            // Construct result
                            var result = new RemoteSearchResult
                            {
                                Name = title ?? "Unknown",
                                ProductionYear = productionYear,
                                ImageUrl = thumbUrl,
                                ProviderIds = new() { { Constants.PluginName, id } },
                                SearchProviderName = Constants.PluginName,
                                Overview = overview,
                                IndexNumber = index++,
                                // Url = listingUrl // assuming your model supports this field
                            };

                            results.Add(result);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error parsing IFDB search result node.");
                        }
                    }

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch or parse IFDB search results from {Url}", searchUrl);
                return Enumerable.Empty<RemoteSearchResult>();
            }
        }

        /// <summary>
        /// Searches for remote results for a given movie item.
        /// </summary>
        /// <param name="info">The movie to search for.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous search operation, containing the search results.</returns>
        public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
        {
            if (!info.ProviderIds.TryGetValue(Constants.PluginName, out var id))
            {
                _logger.LogWarning("No IFDB provider ID found for movie {Name}", info.Name);
                return new() { HasMetadata = false };
            }

            var detailsUrl = string.Format(CultureInfo.InvariantCulture, Constants.IfdbMovieURL, id);
            _logger.LogInformation("Fetching IFDB metadata for {Name} ({Url})", info.Name, detailsUrl);

            try
            {
                var client = _httpClientFactory.CreateClient();
                var html = await client.GetStringAsync(detailsUrl, cancellationToken).ConfigureAwait(false);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var metadata = new MetadataResult<Movie>
                {
                    HasMetadata = false,
                };

                // --- listingNode ---
                var listingNode = doc.DocumentNode.SelectSingleNode("//input[contains(@class,'jrCheckListing')]");

                // --- Original Movie Title ---
                var originalTitle = doc.DocumentNode
                    .SelectSingleNode("//div[contains(@class,'jrOriginalmovietitle')]//div[contains(@class,'jrFieldValue')]//li//a")
                    ?.InnerText?.Trim();

                // --- Overview / Synopsis ---
                var overview = doc.DocumentNode
                    .SelectSingleNode("//div[contains(@class,'jrBriefsynopsis')]//div[contains(@class,'jrFieldValue')]")
                    ?.InnerText?.Trim();

                // --- Ratings ---
                // float editorRating = 0, userRating = 0;
                var editorRatingNode = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'jrOverallEditor')]//span[contains(@class,'jrRatingValue')]/span[1]");
                var userRatingNode = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'jrOverallUser')]//span[contains(@class,'jrRatingValue')]/span[1]");
                float editorRating = editorRatingNode != null && float.TryParse(editorRatingNode.InnerText, out var er) ? er : 0;
                float userRating = userRatingNode != null && float.TryParse(userRatingNode.InnerText, out var ur) ? ur : 0;

                // --- Runtime ---
                int runtimeMinutes = 0;
                var runtimeNode = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'jrFaneditrunningtimemin')]//div[contains(@class,'jrFieldValue')]");
                if (runtimeNode != null && int.TryParse(runtimeNode.InnerText.Trim().Replace(" minutes", string.Empty, StringComparison.Ordinal), out int rt))
                {
                    runtimeMinutes = rt;
                }

                // --- Genres ---
                var genres = doc.DocumentNode.SelectNodes("//div[contains(@class,'jrGenre')]//li//a")
                    ?.Select(x => x.InnerText.Trim())
                    .ToList();

                // --- Media format ---
                var mediaFormat = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'jrReleaseinformation')]//li")?.InnerText.Trim();

                // --- Franchise / Collection ---
                var franchise = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'jrFranchise')]//li//a")?.InnerText.Trim();

                // --- Fanedit Release Date for ProductionYear ---
                int? productionYear = null;
                var faneditReleaseText = doc.DocumentNode
                    .SelectSingleNode("//div[contains(@class,'jrFaneditreleasedate')]//div[contains(@class,'jrFieldValue')]//a")
                    ?.InnerText?.Trim();

                if (!string.IsNullOrWhiteSpace(faneditReleaseText))
                {
                    var parts = faneditReleaseText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && int.TryParse(parts[^1], out int parsedYear))
                    {
                        productionYear = parsedYear;
                    }
                }

                // --- Assemble Movie Object ---
                metadata.Item = new Movie
                {
                    Name = listingNode?.GetAttributeValue("data-listingtitle", string.Empty).Trim(),
                    OriginalTitle = !string.IsNullOrWhiteSpace(originalTitle) ? originalTitle : null,
                    Overview = overview,
                    CommunityRating = userRating > 0 ? userRating : (float?)null,
                    CriticRating = editorRating > 0 ? editorRating : (float?)null,
                    ProductionYear = productionYear,
                    RunTimeTicks = runtimeMinutes > 0 ? TimeSpan.FromMinutes(runtimeMinutes).Ticks : (long?)null,
                    Genres = genres?.ToArray() ?? Array.Empty<string>(),
                    Container = mediaFormat,
                    CollectionName = franchise,
                    HomePageUrl = listingNode?.GetAttributeValue("data-listingurl", null),
                    ProviderIds = new Dictionary<string, string> { { Constants.PluginName, listingNode?.GetAttributeValue("value", string.Empty) ?? string.Empty } },
                };

                // --- Faneditor Name as Editor ---
                var editorName = doc.DocumentNode
                    .SelectSingleNode("//div[contains(@class,'jrFaneditorname')]//li//a")
                    ?.InnerText?.Trim();

                if (!string.IsNullOrWhiteSpace(editorName))
                {
                    metadata.AddPerson(new PersonInfo
                    {
                        Name = editorName,
                        Type = PersonKind.Editor,
                        Role = "Faneditor"
                    });
                }

                metadata.HasMetadata = true;

                return metadata;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch or parse IFDB metadata for {Name}", info.Name);
                return new() { HasMetadata = false };
            }
        }

        /// <inheritdoc />
        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(new Uri(url), cancellationToken);
        }
    }
}
