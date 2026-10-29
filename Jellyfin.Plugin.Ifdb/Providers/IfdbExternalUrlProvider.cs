using System.Collections.Generic;
using Jellyfin.Plugin.Ifdb.Api;
using Jellyfin.Plugin.Ifdb.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Ifdb.Providers;

/// <summary>
/// Adds an "IFDB" link to identified fanedits' detail pages.
/// </summary>
public class IfdbExternalUrlProvider : IExternalUrlProvider
{
    /// <inheritdoc />
    public string Name => Constants.PluginName;

    /// <inheritdoc />
    public IEnumerable<string> GetExternalUrls(BaseItem item)
    {
        if (item is not Movie)
        {
            yield break;
        }

        var id = IfdbUrls.NormalizeId(item.GetProviderId(Constants.ProviderIdKey));
        if (id is not null)
        {
            var baseUrl = Plugin.Instance?.Configuration.BaseUrl ?? new PluginConfiguration().BaseUrl;
            yield return IfdbUrls.BuildEntryUrl(baseUrl, id);
        }
    }
}
