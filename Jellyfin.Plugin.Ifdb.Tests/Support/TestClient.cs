using System;
using System.Net.Http;
using Jellyfin.Plugin.Ifdb.Api;
using Jellyfin.Plugin.Ifdb.Configuration;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Jellyfin.Plugin.Ifdb.Tests.Support;

/// <summary>
/// Builds an <see cref="IfdbClient"/> wired to a fake handler, with its own state, clock and settings.
/// </summary>
internal sealed class TestClient : IDisposable
{
    public TestClient(PluginConfiguration? config = null, LogLevel logLevel = LogLevel.Trace, int maxCachedPages = 500)
    {
        Config = config ?? new PluginConfiguration { RequestDelayMilliseconds = 0 };
        Logger = new ListLogger(logLevel);
        State = new IfdbRequestState(maxCachedPages);

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(Handler, disposeHandler: false));
        Factory = factory;

        Client = new IfdbClient(Factory, Logger, () => Config, State, Time);
    }

    public static Uri SearchPage { get; } = new("https://fanedit.org/fanedit-search/search-results/?query=all&scope=title&keywords=hobbit&order=rdate");

    public PluginConfiguration Config { get; set; }

    public FakeHttpHandler Handler { get; } = new();

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero));

    public ListLogger Logger { get; }

    public IfdbRequestState State { get; }

    public IHttpClientFactory Factory { get; }

    public IfdbClient Client { get; }

    public void Dispose()
    {
        State.Dispose();
        Handler.Dispose();
    }
}
