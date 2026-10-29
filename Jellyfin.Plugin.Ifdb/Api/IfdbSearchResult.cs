namespace Jellyfin.Plugin.Ifdb.Api;

/// <summary>
/// One fanedit listed on a fanedit.org search results page.
/// </summary>
internal sealed class IfdbSearchResult
{
    public string Id { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int? Year { get; set; }

    public string? ImageUrl { get; set; }

    public string? Overview { get; set; }
}
