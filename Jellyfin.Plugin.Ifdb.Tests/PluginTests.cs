using System;
using System.Linq;
using Jellyfin.Plugin.Ifdb.Api;
using Jellyfin.Plugin.Ifdb.Configuration;
using Jellyfin.Plugin.Ifdb.Providers;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Serialization;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.Ifdb.Tests;

// These tests set the static Plugin.Instance, so they don't run alongside each other.
[CollectionDefinition(nameof(PluginInstanceCollection), DisableParallelization = true)]
public class PluginInstanceCollection
{
}

[Collection(nameof(PluginInstanceCollection))]
public class PluginTests
{
    private static Plugin CreatePlugin()
    {
        var paths = Substitute.For<IApplicationPaths>();
        paths.PluginsPath.Returns(System.IO.Path.GetTempPath());
        paths.PluginConfigurationsPath.Returns(System.IO.Path.GetTempPath());
        var serializer = Substitute.For<IXmlSerializer>();
        serializer.DeserializeFromFile(Arg.Any<Type>(), Arg.Any<string>())
            .Returns(new PluginConfiguration { BaseUrl = "https://ifdb.fanedit.org", AutoMatchMinimumScore = 75 });
        return new Plugin(paths, serializer);
    }

    [Fact]
    public void PluginDescribesItself()
    {
        var plugin = CreatePlugin();

        Assert.Same(plugin, Plugin.Instance);
        Assert.Equal("IFDB", plugin.Name);
        Assert.Equal(Guid.Parse("0f18c602-5d24-4f88-bb6e-9e59a2efb1cd"), plugin.Id);
        Assert.Contains("fanedit.org", plugin.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationPageIsEmbedded()
    {
        var page = Assert.Single(CreatePlugin().GetPages());

        Assert.Equal("IFDB", page.Name);
        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(page.EmbeddedResourcePath);
        Assert.NotNull(stream);
    }

    [Fact]
    public void ConfigurationComesFromTheLoadedPlugin()
    {
        CreatePlugin();
        try
        {
            Assert.Equal(75, PluginConfigurationProvider.Current().AutoMatchMinimumScore);
        }
        finally
        {
            Plugin.Instance = null;
        }

        Assert.Equal(90, PluginConfigurationProvider.Current().AutoMatchMinimumScore);
    }

    [Fact]
    public void ExternalUrlsPointAtTheConfiguredSite()
    {
        var provider = new IfdbExternalUrlProvider();
        var movie = new Movie();
        movie.SetProviderId(Constants.ProviderIdKey, "17071");
        var series = new Series();
        series.SetProviderId(Constants.ProviderIdKey, "17071");

        Assert.Equal("IFDB", provider.Name);
        Assert.Equal(new[] { "https://fanedit.org/?p=17071" }, provider.GetExternalUrls(movie));
        Assert.Empty(provider.GetExternalUrls(new Movie()));
        Assert.Empty(provider.GetExternalUrls(series));

        CreatePlugin();
        try
        {
            Assert.Equal("https://ifdb.fanedit.org/?p=17071", provider.GetExternalUrls(movie).Single());
        }
        finally
        {
            Plugin.Instance = null;
        }
    }

    [Fact]
    public void DefaultsMatchTheDocumentedSettings()
    {
        var config = new PluginConfiguration();

        Assert.Equal("https://fanedit.org", config.BaseUrl);
        Assert.Equal(string.Empty, config.UserAgent);
        Assert.Equal(SearchMatchType.All, config.MatchType);
        Assert.Equal(90, config.AutoMatchMinimumScore);
        Assert.Equal(60, config.SearchMinimumScore);
        Assert.Equal(25, config.MaxSearchResults);
        Assert.True(config.IncludeChangesInOverview);
        Assert.True(config.AddFaneditTypeTags);
        Assert.True(config.AddFranchiseTags);
        Assert.False(config.SetImdbId);
        Assert.Equal(FaneditorCredit.Director, config.FaneditorCredit);
        Assert.Equal(1000, config.RequestDelayMilliseconds);
        Assert.Equal(30, config.RequestTimeoutSeconds);
        Assert.Equal(30, config.CacheMinutes);
    }

    [Fact]
    public void BlockedExceptionHasStandardConstructors()
    {
        var inner = new InvalidOperationException("inner");

        Assert.NotNull(new IfdbBlockedException().Message);
        Assert.Equal("blocked", new IfdbBlockedException("blocked").Message);
        Assert.Same(inner, new IfdbBlockedException("blocked", inner).InnerException);
    }
}
