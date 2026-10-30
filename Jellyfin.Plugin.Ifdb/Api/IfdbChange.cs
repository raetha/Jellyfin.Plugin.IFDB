namespace Jellyfin.Plugin.Ifdb.Api;

/// <summary>
/// One labelled section of a fanedit's Changes tab, such as "Cuts and Additions".
/// </summary>
internal sealed class IfdbChange
{
    public string Label { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}
