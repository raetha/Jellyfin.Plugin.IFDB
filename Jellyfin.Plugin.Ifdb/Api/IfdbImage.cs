namespace Jellyfin.Plugin.Ifdb.Api;

/// <summary>
/// An image found on a fanedit's detail page.
/// </summary>
internal sealed class IfdbImage
{
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the width, when it could be read from the page or the file name.
    /// </summary>
    public int? Width { get; set; }

    /// <summary>
    /// Gets or sets the height, when it could be read from the page or the file name.
    /// </summary>
    public int? Height { get; set; }

    /// <summary>
    /// Gets a value indicating whether the image is clearly wider than it is tall,
    /// so better suited as a backdrop or thumb than a poster.
    /// </summary>
    public bool IsLandscape => Width is > 0 && Height is > 0 && Width.Value > Height.Value * 1.2;
}
