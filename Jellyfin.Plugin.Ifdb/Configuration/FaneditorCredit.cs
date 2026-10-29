namespace Jellyfin.Plugin.Ifdb.Configuration;

/// <summary>
/// The person type fan editors are credited as.
/// </summary>
public enum FaneditorCredit
{
    /// <summary>
    /// Credit fan editors as directors, so they show in the Director field.
    /// </summary>
    Director,

    /// <summary>
    /// Credit fan editors as editors, in the cast and crew list.
    /// </summary>
    Editor
}
