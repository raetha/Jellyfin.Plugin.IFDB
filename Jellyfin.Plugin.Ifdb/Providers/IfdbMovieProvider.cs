using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Ifdb.Api;
using Jellyfin.Plugin.Ifdb.Configuration;
using Jellyfin.Plugin.Ifdb.Matching;
using MediaBrowser.Controller.Entities;
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
    private readonly ILogger<IfdbMovieProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IfdbMovieProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="libraryManager">The library manager, used to clean up file names before searching.</param>
    /// <param name="logger">The logger.</param>
    public IfdbMovieProvider(IHttpClientFactory httpClientFactory, ILibraryManager libraryManager, ILogger<IfdbMovieProvider> logger)
    {
        _client = new IfdbClient(httpClientFactory, logger);
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => Constants.PluginName;

    private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

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
                return entry is null ? [] : [ToRemoteSearchResult(entry)];
            }

            var title = CleanTitle(searchInfo.Name);
            if (title.Length == 0)
            {
                return [];
            }

            var scored = await SearchAndScoreAsync(title, cancellationToken).ConfigureAwait(false);
            var minimum = Math.Clamp(Config.SearchMinimumScore, 0, 100);
            var shown = scored.Where(s => s.Score >= minimum).ToList();

            // Nothing close enough: still show the best few rather than an empty dialog.
            if (shown.Count == 0)
            {
                shown = scored.Take(10).ToList();
            }

            return shown.Select(s => ToRemoteSearchResult(s.Result)).ToList();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            IfdbClient.LogFailure(_logger, ex, "searching for \"" + searchInfo.Name + "\"");
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Movie>();
        try
        {
            var config = Config;
            var id = IfdbUrls.NormalizeId(info.GetProviderId(Constants.ProviderIdKey))
                ?? await FindMatchAsync(info, config, cancellationToken).ConfigureAwait(false);
            if (id is null)
            {
                return result;
            }

            var entry = await _client.GetEntryAsync(id, cancellationToken).ConfigureAwait(false);
            if (entry is null)
            {
                return result;
            }

            result.Item = BuildMovie(entry, config);
            result.HasMetadata = true;
            result.ResultLanguage = "en";

            var personKind = config.FaneditorCredit == FaneditorCredit.Editor ? PersonKind.Editor : PersonKind.Director;
            foreach (var editor in entry.Faneditors)
            {
                result.AddPerson(new PersonInfo
                {
                    Name = editor,
                    Type = personKind,
                    Role = Constants.FaneditorRole,
                });
            }

            return result;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            IfdbClient.LogFailure(_logger, ex, "getting metadata for \"" + info.Name + "\"");
            return result;
        }
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _client.GetImageResponseAsync(url, cancellationToken);

    internal static Movie BuildMovie(IfdbEntry entry, PluginConfiguration config)
    {
        var movie = new Movie
        {
            Name = entry.Title,
            OriginalTitle = entry.OriginalTitles.Count > 0 ? string.Join(", ", entry.OriginalTitles) : null,
            Overview = BuildOverview(entry, config),
            HomePageUrl = entry.Url,
            CommunityRating = entry.UserRating,

            // Jellyfin's critic rating is a 0-100 percentage; fanedit.org's Trusted Reviewer score is out of 10.
            CriticRating = entry.TrustedReviewerRating is { } critic ? Math.Clamp(critic * 10f, 0f, 100f) : null,
            PremiereDate = entry.ReleaseDate,
            ProductionYear = entry.ReleaseDate?.Year,
            Genres = entry.Genres.ToArray(),
        };

        if (entry.RuntimeMinutes is > 0)
        {
            movie.RunTimeTicks = TimeSpan.FromMinutes(entry.RuntimeMinutes.Value).Ticks;
        }

        var tags = new List<string>();
        if (config.AddFaneditTypeTags)
        {
            tags.AddRange(entry.FaneditTypes);
        }

        if (config.AddFranchiseTags)
        {
            tags.AddRange(entry.Franchises);
        }

        movie.Tags = tags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        movie.SetProviderId(Constants.ProviderIdKey, entry.Id);
        if (config.SetImdbId && !string.IsNullOrEmpty(entry.ImdbId))
        {
            movie.SetProviderId(MetadataProvider.Imdb, entry.ImdbId);
        }

        foreach (var trailer in entry.TrailerUrls)
        {
            movie.AddTrailerUrl(trailer);
        }

        return movie;
    }

    internal static string? BuildOverview(IfdbEntry entry, PluginConfiguration config)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(entry.Synopsis))
        {
            parts.Add(entry.Synopsis);
        }

        if (config.IncludeChangesInOverview)
        {
            foreach (var change in entry.Changes)
            {
                parts.Add(change.Label.Length > 0 ? change.Label + ":\n" + change.Text : change.Text);
            }
        }

        return parts.Count > 0 ? string.Join("\n\n", parts) : null;
    }

    // Picks the entry to use for an unidentified movie during a library scan. Only an unambiguous
    // match is used: exactly one result at or above the automatic-match score, or exactly one
    // perfect match, or exactly one qualifying match from the movie's year.
    private async Task<string?> FindMatchAsync(MovieInfo info, PluginConfiguration config, CancellationToken cancellationToken)
    {
        var title = CleanTitle(info.Name);
        if (title.Length == 0)
        {
            return null;
        }

        var scored = await SearchAndScoreAsync(title, cancellationToken).ConfigureAwait(false);
        var minimum = Math.Clamp(config.AutoMatchMinimumScore, 0, 100);
        var qualifying = scored.Where(s => s.Score >= minimum).ToList();

        if (qualifying.Count > 1)
        {
            var perfect = qualifying.Where(s => s.Score >= 100).ToList();
            var sameYear = qualifying.Where(s => info.Year is not null && s.Result.Year == info.Year).ToList();
            if (perfect.Count == 1)
            {
                qualifying = perfect;
            }
            else if (sameYear.Count == 1)
            {
                qualifying = sameYear;
            }
        }

        if (qualifying.Count == 1)
        {
            var match = qualifying[0];
            _logger.LogInformation("IFDB matched \"{Title}\" to \"{Match}\" ({Id}, score {Score})", title, match.Result.Title, match.Result.Id, match.Score);
            return match.Result.Id;
        }

        if (qualifying.Count > 1)
        {
            _logger.LogInformation("IFDB found {Count} equally good matches for \"{Title}\"; use Identify to pick one", qualifying.Count, title);
        }
        else
        {
            _logger.LogInformation("IFDB found no match for \"{Title}\" scoring {Minimum} or more ({Count} result(s) checked)", title, minimum, scored.Count);
        }

        return null;
    }

    private async Task<List<(IfdbSearchResult Result, int Score)>> SearchAndScoreAsync(string title, CancellationToken cancellationToken)
    {
        var results = await _client.SearchAsync(title, cancellationToken).ConfigureAwait(false);

        // fanedit.org's keyword search can trip over punctuation ("M4's"); retry once with plain words.
        if (results.Count == 0)
        {
            var simplified = string.Join(' ', TitleScorer.Normalize(title).Split(' ').Where(w => w.Length > 1));
            if (simplified.Length > 0 && !string.Equals(simplified, title, StringComparison.OrdinalIgnoreCase))
            {
                results = await _client.SearchAsync(simplified, cancellationToken).ConfigureAwait(false);
            }
        }

        return results
            .Select((result, index) => (Result: result, Score: TitleScorer.Score(title, result.Title), Index: index))
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Index)
            .Select(s => (s.Result, s.Score))
            .ToList();
    }

    private string CleanTitle(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        // Strips years, release tags and other file-name clutter the way the built-in providers do.
        var parsed = _libraryManager.ParseName(name)?.Name;
        return string.IsNullOrWhiteSpace(parsed) ? name.Trim() : parsed.Trim();
    }

    private RemoteSearchResult ToRemoteSearchResult(IfdbSearchResult result)
    {
        var remote = new RemoteSearchResult
        {
            Name = result.Title,
            ProductionYear = result.Year,
            ImageUrl = result.ImageUrl,
            Overview = result.Overview,
            SearchProviderName = Name,
        };
        remote.SetProviderId(Constants.ProviderIdKey, result.Id);
        return remote;
    }

    private RemoteSearchResult ToRemoteSearchResult(IfdbEntry entry)
    {
        var remote = new RemoteSearchResult
        {
            Name = entry.Title,
            ProductionYear = entry.ReleaseDate?.Year,
            PremiereDate = entry.ReleaseDate,
            ImageUrl = entry.Images.FirstOrDefault(i => !i.IsLandscape)?.Url ?? entry.Images.FirstOrDefault()?.Url,
            Overview = entry.Synopsis,
            SearchProviderName = Name,
        };
        remote.SetProviderId(Constants.ProviderIdKey, entry.Id);
        return remote;
    }
}
