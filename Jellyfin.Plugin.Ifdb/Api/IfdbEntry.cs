using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Ifdb.Api;

/// <summary>
/// The details scraped from a fanedit's page on fanedit.org.
/// </summary>
internal sealed class IfdbEntry
{
    public string Id { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public List<string> Faneditors { get; } = [];

    public List<string> OriginalTitles { get; } = [];

    public List<string> Genres { get; } = [];

    public List<string> FaneditTypes { get; } = [];

    public List<string> Franchises { get; } = [];

    public DateTime? ReleaseDate { get; set; }

    public int? RuntimeMinutes { get; set; }

    public string? ImdbId { get; set; }

    public float? TrustedReviewerRating { get; set; }

    public float? UserRating { get; set; }

    public string? Synopsis { get; set; }

    public List<IfdbChange> Changes { get; } = [];

    public List<IfdbImage> Images { get; } = [];

    public List<string> TrailerUrls { get; } = [];
}
