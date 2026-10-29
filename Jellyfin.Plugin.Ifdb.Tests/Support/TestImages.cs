using System;
using System.Linq;
using System.Text;

namespace Jellyfin.Plugin.Ifdb.Tests.Support;

/// <summary>
/// The first bytes of each image format, which is all the plugin checks.
/// </summary>
internal static class TestImages
{
    public static byte[] Jpeg { get; } = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];

    public static byte[] Png { get; } = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];

    public static byte[] Gif87 { get; } = Encoding.ASCII.GetBytes("GIF87a....");

    public static byte[] Gif89 { get; } = Encoding.ASCII.GetBytes("GIF89a....");

    public static byte[] Webp { get; } = Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 ");

    public static byte[] Svg { get; } = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>");

    public static byte[] Html { get; } = Encoding.UTF8.GetBytes("<!DOCTYPE html><html><body>Not an image</body></html>");

    // A RIFF file that isn't WebP (a WAV header).
    public static byte[] Wav { get; } = Encoding.ASCII.GetBytes("RIFF\0\0\0\0WAVEfmt ");
}
