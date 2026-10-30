using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Ifdb.Api;
using Jellyfin.Plugin.Ifdb.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Ifdb.Providers;

/// <summary>
/// Turns IFDB data into Jellyfin metadata.
/// </summary>
internal static class IfdbMetadataMapper
{
    /// <summary>
    /// Builds the metadata result for a fanedit.
    /// </summary>
    /// <param name="entry">The fanedit.</param>
    /// <param name="config">The plugin configuration.</param>
    /// <returns>The metadata result, including fan editor credits.</returns>
    public static MetadataResult<Movie> ToMetadataResult(IfdbEntry entry, PluginConfiguration config)
    {
        var result = new MetadataResult<Movie>
        {
            Item = ToMovie(entry, config),
            HasMetadata = true,
            ResultLanguage = "en",
        };

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

    public static Movie ToMovie(IfdbEntry entry, PluginConfiguration config)
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
            RunTimeTicks = entry.RuntimeMinutes is > 0 ? TimeSpan.FromMinutes(entry.RuntimeMinutes.Value).Ticks : null,
        };

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

    /// <summary>
    /// Builds the overview: the synopsis, then (optionally) each labelled section of the Changes tab.
    /// </summary>
    /// <remarks>
    /// Jellyfin's web client renders overviews as Markdown, where a single line break is just a space.
    /// Lines within a paragraph therefore end with Markdown's two-space line break (invisible in clients
    /// that show plain text), and paragraphs are separated by a blank line.
    /// </remarks>
    /// <param name="entry">The fanedit.</param>
    /// <param name="config">The plugin configuration.</param>
    /// <returns>The overview, or <c>null</c> when there's nothing to show.</returns>
    public static string? BuildOverview(IfdbEntry entry, PluginConfiguration config)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(entry.Synopsis))
        {
            parts.Add(entry.Synopsis);
        }

        if (config.IncludeChangesInOverview)
        {
            parts.AddRange(entry.Changes.Select(c => c.Label.Length > 0 ? c.Label + ":\n" + c.Text : c.Text));
        }

        if (parts.Count == 0)
        {
            return null;
        }

        var paragraphs = string.Join("\n\n", parts)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(p => string.Join("  \n", p.Split('\n', StringSplitOptions.RemoveEmptyEntries)));
        return ScrapedText.Clean(string.Join("\n\n", paragraphs), ScrapedText.MaxOverviewLength);
    }

    public static RemoteSearchResult ToRemoteSearchResult(IfdbSearchResult result)
    {
        var remote = new RemoteSearchResult
        {
            Name = result.Title,
            ProductionYear = result.Year,
            ImageUrl = result.ImageUrl,
            Overview = result.Overview,
            SearchProviderName = Constants.PluginName,
        };
        remote.SetProviderId(Constants.ProviderIdKey, result.Id);
        return remote;
    }

    public static RemoteSearchResult ToRemoteSearchResult(IfdbEntry entry)
    {
        var remote = new RemoteSearchResult
        {
            Name = entry.Title,
            ProductionYear = entry.ReleaseDate?.Year,
            PremiereDate = entry.ReleaseDate,
            ImageUrl = entry.PosterUrl,
            Overview = entry.Synopsis,
            SearchProviderName = Constants.PluginName,
        };
        remote.SetProviderId(Constants.ProviderIdKey, entry.Id);
        return remote;
    }

    /// <summary>
    /// Sorts fanedit images into Jellyfin image types. Portrait (or unknown-shape) images are
    /// offered as posters; clearly wide ones, such as "Fanedit of the Month" banners, as
    /// backdrops and thumbs. A wide image is only offered as a poster when there's nothing else.
    /// </summary>
    /// <param name="images">The entry's images, main image first.</param>
    /// <returns>The images to offer.</returns>
    public static List<RemoteImageInfo> ToRemoteImages(IReadOnlyList<IfdbImage> images)
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

        if (images.Count > 0 && !result.Exists(i => i.Type == ImageType.Primary))
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
