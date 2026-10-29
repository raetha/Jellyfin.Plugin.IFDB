namespace Jellyfin.Plugin.Ifdb.Configuration;

/// <summary>
/// Reads the plugin's current configuration.
/// </summary>
internal static class PluginConfigurationProvider
{
    /// <summary>
    /// Gets the saved configuration, or the defaults when the plugin isn't loaded (as in tests).
    /// </summary>
    /// <returns>The configuration.</returns>
    public static PluginConfiguration Current() => Plugin.Instance?.Configuration ?? new PluginConfiguration();
}
