using System;

namespace Jellyfin.Plugin.Ifdb.Api;

/// <summary>
/// Identifies image files by their content, not by what the server says they are.
/// </summary>
/// <remarks>
/// Jellyfin saves whatever a provider's image response contains, naming it from the response's
/// Content-Type. Checking the file's own signature means only real raster images get through: an
/// HTML page, script or SVG (which can carry script) can't be saved by claiming to be an image.
/// </remarks>
internal static class ImageSniffer
{
    /// <summary>
    /// Gets the media type of a JPEG, PNG, GIF or WebP file from its first bytes.
    /// </summary>
    /// <param name="data">The file's contents.</param>
    /// <returns>The media type, or <c>null</c> for anything else.</returns>
    public static string? DetectMediaType(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return "image/jpeg";
        }

        if (data.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "image/png";
        }

        if (data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8))
        {
            return "image/gif";
        }

        if (data.Length >= 12 && data.StartsWith("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }
}
