namespace Jellyfin.Plugin.Ifdb.Configuration;

/// <summary>
/// How fanedit.org matches search keywords against titles.
/// </summary>
public enum SearchMatchType
{
    /// <summary>
    /// Every keyword must appear in the title.
    /// </summary>
    All,

    /// <summary>
    /// Any keyword may appear in the title.
    /// </summary>
    Any,

    /// <summary>
    /// The keywords must appear as an exact phrase.
    /// </summary>
    Exact
}
