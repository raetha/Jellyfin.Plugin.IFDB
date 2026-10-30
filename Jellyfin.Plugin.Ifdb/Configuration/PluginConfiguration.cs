using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Ifdb.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the fanedit.org site address. Both https://fanedit.org and https://ifdb.fanedit.org serve the database.
    /// </summary>
    public string BaseUrl { get; set; } = Constants.DefaultBaseUrl;

    /// <summary>
    /// Gets or sets the User-Agent sent to fanedit.org. Empty uses the plugin's default.
    /// </summary>
    public string UserAgent { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how fanedit.org matches search keywords.
    /// </summary>
    public SearchMatchType MatchType { get; set; } = SearchMatchType.All;

    /// <summary>
    /// Gets or sets the minimum title score (0-100) for automatically matching an unidentified movie.
    /// A movie is only matched when exactly one result reaches this score.
    /// </summary>
    public int AutoMatchMinimumScore { get; set; } = 90;

    /// <summary>
    /// Gets or sets the minimum title score (0-100) for results listed in the Identify dialog.
    /// </summary>
    public int SearchMinimumScore { get; set; } = 60;

    /// <summary>
    /// Gets or sets the maximum number of search results considered per search.
    /// </summary>
    public int MaxSearchResults { get; set; } = 25;

    /// <summary>
    /// Gets or sets a value indicating whether the Changes section is appended to the overview.
    /// </summary>
    public bool IncludeChangesInOverview { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the fanedit type (for example "FanFix") is added as a tag.
    /// </summary>
    public bool AddFaneditTypeTags { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the franchise is added as a tag.
    /// </summary>
    public bool AddFranchiseTags { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the original film's IMDb id is stored on the fanedit.
    /// Off by default: other metadata providers on the library would then fetch the original film's details.
    /// </summary>
    public bool SetImdbId { get; set; }

    /// <summary>
    /// Gets or sets how fan editors are credited.
    /// </summary>
    public FaneditorCredit FaneditorCredit { get; set; } = FaneditorCredit.Director;

    /// <summary>
    /// Gets or sets the minimum delay between requests to fanedit.org, in milliseconds.
    /// </summary>
    public int RequestDelayMilliseconds { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the request timeout, in seconds.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets how long fetched pages are cached, in minutes. 0 disables caching.
    /// </summary>
    public int CacheMinutes { get; set; } = 30;
}
