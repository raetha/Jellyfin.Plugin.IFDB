using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Ifdb.Providers;

/// <summary>
/// Adds an IFDB id field to a movie's metadata editor and Identify dialog.
/// The field accepts the entry's permalink slug or a full fanedit.org URL.
/// </summary>
public class IfdbExternalId : IExternalId
{
    /// <inheritdoc />
    public string ProviderName => Constants.PluginName;

    /// <inheritdoc />
    public string Key => Constants.ProviderIdKey;

    /// <inheritdoc />
    public ExternalIdMediaType? Type => ExternalIdMediaType.Movie;

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item) => item is Movie;
}
