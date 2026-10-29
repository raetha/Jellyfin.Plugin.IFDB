using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Ifdb.Tests.Support;

/// <summary>
/// Answers HTTP requests from a list of routes instead of the network, and records every request.
/// </summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly List<(Func<Uri, bool> Match, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond)> _routes = [];

    public ConcurrentQueue<HttpRequestMessage> Requests { get; } = new();

    public int RequestCount => Requests.Count;

    /// <summary>
    /// Answers requests whose URL contains <paramref name="urlPart"/> with an HTML page.
    /// </summary>
    public FakeHttpHandler Html(string urlPart, string html, HttpStatusCode status = HttpStatusCode.OK, string? finalUrl = null, string? header = null)
        => Route(
            uri => uri.AbsoluteUri.Contains(urlPart, StringComparison.Ordinal),
            (request, _) => Task.FromResult(Response(request, html, status, finalUrl, header)));

    public FakeHttpHandler Route(Func<Uri, bool> match, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        _routes.Add((match, respond));
        return this;
    }

    public static HttpResponseMessage Response(HttpRequestMessage request, string html, HttpStatusCode status = HttpStatusCode.OK, string? finalUrl = null, string? header = null)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(html),

            // HttpClient sets RequestMessage to the last request after following redirects; mimic that.
            RequestMessage = finalUrl is null ? request : new HttpRequestMessage(HttpMethod.Get, finalUrl),
        };

        if (header is not null)
        {
            var parts = header.Split(':', 2);
            response.Headers.TryAddWithoutValidation(parts[0], parts[1].Trim());
        }

        return response;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(request);

        // Later routes win, so a test can override an earlier answer.
        var route = _routes.LastOrDefault(r => r.Match(request.RequestUri!));
        return route.Respond is null
            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("not found"), RequestMessage = request })
            : route.Respond(request, cancellationToken);
    }
}
