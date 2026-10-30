using System;
using System.Net;

namespace Jellyfin.Plugin.Ifdb.Api;

/// <summary>
/// A fetched fanedit.org page.
/// </summary>
internal sealed class IfdbPage
{
    public IfdbPage(Uri finalUrl, HttpStatusCode statusCode, string html)
    {
        FinalUrl = finalUrl;
        StatusCode = statusCode;
        Html = html;
    }

    /// <summary>
    /// Gets the URL the page was finally loaded from, after redirects.
    /// </summary>
    public Uri FinalUrl { get; }

    public HttpStatusCode StatusCode { get; }

    public string Html { get; }
}
