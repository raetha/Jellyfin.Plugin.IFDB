using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Ifdb.Api;
using Jellyfin.Plugin.Ifdb.Configuration;
using Jellyfin.Plugin.Ifdb.Providers;
using Jellyfin.Plugin.Ifdb.Tests.Support;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.Ifdb.Tests;

public class SanitizingTests
{
    private static readonly Uri PageUrl = new("https://fanedit.org/tricky-edit/");

    public static TheoryData<byte[], string?> Signatures => new()
    {
        { TestImages.Jpeg, "image/jpeg" },
        { TestImages.Png, "image/png" },
        { TestImages.Gif87, "image/gif" },
        { TestImages.Gif89, "image/gif" },
        { TestImages.Webp, "image/webp" },
        { TestImages.Svg, null },
        { TestImages.Html, null },
        { TestImages.Wav, null },
        { "RIFF"u8.ToArray(), null },
        { Array.Empty<byte>(), null },
    };

    [Theory]
    [MemberData(nameof(Signatures))]
    public void ImagesAreIdentifiedByContent(byte[] data, string? expected)
        => Assert.Equal(expected, ImageSniffer.DetectMediaType(data));

    [Fact]
    public async Task ImageTypeComesFromTheContentNotTheServer()
    {
        using var t = new TestClient();
        t.Handler.Bytes("poster", TestImages.Png, "application/octet-stream");

        using var response = await t.Client.GetImageResponseAsync("https://fanedit.org/poster", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("https://fanedit.org/poster", response.RequestMessage!.RequestUri!.ToString());
    }

    [Theory]
    [InlineData("svg")]
    [InlineData("html")]
    public async Task NonImagesAreRefusedAsNotFound(string kind)
    {
        using var t = new TestClient();
        t.Handler.Bytes("cover.jpg", kind == "svg" ? TestImages.Svg : TestImages.Html, "image/jpeg");

        using var response = await t.Client.GetImageResponseAsync("https://fanedit.org/cover.jpg", CancellationToken.None);

        // 404 makes Jellyfin skip to the next image instead of saving this one.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(t.Logger.Has(LogLevel.Warning, "not a JPEG, PNG, GIF or WebP image (sent as image/jpeg)"));
    }

    [Fact]
    public async Task NonImagesWithoutAContentTypeAreRefused()
    {
        using var t = new TestClient();
        t.Handler.Bytes("cover", TestImages.Html);

        using var response = await t.Client.GetImageResponseAsync("https://fanedit.org/cover", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(t.Logger.Has(LogLevel.Warning, "(sent as )"));
    }

    [Fact]
    public async Task OversizedImagesAreRefused()
    {
        using var t = new TestClient();
        t.Handler.Route(
            uri => uri.AbsoluteUri.Contains("huge.jpg", StringComparison.Ordinal),
            (request, _) =>
            {
                var response = FakeHttpHandler.Response(request, "x");
                response.Content.Headers.ContentLength = 21L * 1024 * 1024;
                return Task.FromResult(response);
            });

        using var refused = await t.Client.GetImageResponseAsync("https://fanedit.org/huge.jpg", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        Assert.True(t.Logger.Has(LogLevel.Warning, "image is too large"));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ImageErrorsArePassedThroughForJellyfinToHandle(HttpStatusCode status)
    {
        using var t = new TestClient();
        t.Handler.Bytes("missing.jpg", TestImages.Jpeg, "image/jpeg", status);

        using var response = await t.Client.GetImageResponseAsync("https://fanedit.org/missing.jpg", CancellationToken.None);

        Assert.Equal(status, response.StatusCode);
    }

    [Fact]
    public async Task RefusedImagesAreQuietWhenWarningsAreOff()
    {
        using var t = new TestClient(logLevel: LogLevel.Error);
        t.Handler.Bytes("cover.jpg", TestImages.Svg, "image/svg+xml");

        using var response = await t.Client.GetImageResponseAsync("https://fanedit.org/cover.jpg", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(t.Logger.Entries);
    }

    [Theory]
    [InlineData("Plain title", "Plain title")]
    [InlineData("Bad\u0000Null\u0007Bell\u001BEsc", "BadNullBellEsc")]
    [InlineData("Line one\nLine two", "Line one\nLine two")]
    [InlineData("evil\u202Etxt.exe", "eviltxt.exe")]
    [InlineData("iso\u2066late\u2069d", "isolated")]
    [InlineData("\uFEFFbom", "bom")]
    [InlineData("<img src=x onerror=alert(1)>Edit", "Edit")]
    [InlineData("Keep <3 and a < b", "Keep <3 and a < b")]
    [InlineData("  padded  ", "padded")]
    public void TextIsCleaned(string input, string expected)
        => Assert.Equal(expected, ScrapedText.Clean(input, 100));

    [Fact]
    public void LongTextIsCut()
        => Assert.Equal("abcde\u2026", ScrapedText.Clean("abcdefghij", 5));

    [Fact]
    public void ScrapedFieldsAreCleaned()
    {
        // Entities decode to real tags and control characters, which are then removed.
        const string Html = """
            <div id="primary"><h1><span itemprop="headline">Tricky&#x202E; &lt;script&gt;alert(1)&lt;/script&gt;Edit&#7;</span></h1>
            <div class="jrGenre"><div class="jrFieldValue"><ul><li>Drama&#0;</li></ul></div></div>
            <div class="jrBriefsynopsis"><div class="jrFieldValue">Fine &lt;img src=x onerror=alert(1)&gt;story<br>Second&#27; line</div></div>
            </div>
            """;

        var entry = IfdbParser.ParseEntry(Html, PageUrl);

        Assert.NotNull(entry);
        Assert.Equal("Tricky alert(1)Edit", entry.Title);
        Assert.Equal(new[] { "Drama" }, entry.Genres);
        Assert.Equal("Fine story\nSecond line", entry.Synopsis);
    }

    [Fact]
    public void OverviewIsCappedInLength()
    {
        var entry = new IfdbEntry { Synopsis = new string('a', ScrapedText.MaxOverviewLength) };
        entry.Changes.Add(new IfdbChange { Label = "More", Text = new string('b', 100) });

        var overview = IfdbMetadataMapper.BuildOverview(entry, new PluginConfiguration());

        Assert.Equal(ScrapedText.MaxOverviewLength + 1, overview!.Length);
        Assert.EndsWith("\u2026", overview, StringComparison.Ordinal);
    }
}
