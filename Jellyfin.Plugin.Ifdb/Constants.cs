namespace Jellyfin.Plugin.Ifdb;

/// <summary>
/// Shared constants for the IFDB plugin.
/// </summary>
internal static class Constants
{
    /// <summary>
    /// The plugin and metadata provider display name.
    /// </summary>
    public const string PluginName = "IFDB";

    /// <summary>
    /// The plugin GUID. Must never change, or existing installs lose their configuration.
    /// </summary>
    public const string PluginGuid = "0f18c602-5d24-4f88-bb6e-9e59a2efb1cd";

    /// <summary>
    /// The key the IFDB entry id is stored under in an item's provider ids.
    /// Kept as "IFDB" so items identified by earlier versions keep their match.
    /// </summary>
    public const string ProviderIdKey = "IFDB";

    /// <summary>
    /// The default fanedit.org site address.
    /// </summary>
    public const string DefaultBaseUrl = "https://fanedit.org";

    /// <summary>
    /// The path of fanedit.org's search results page.
    /// </summary>
    public const string SearchPath = "/fanedit-search/search-results/";

    /// <summary>
    /// The project page, sent in the default user agent so the site owners can see who is calling.
    /// </summary>
    public const string ProjectUrl = "https://github.com/raetha/Jellyfin.Plugin.IFDB";

    /// <summary>
    /// Role shown for credited fan editors.
    /// </summary>
    public const string FaneditorRole = "Fan Editor";
}
