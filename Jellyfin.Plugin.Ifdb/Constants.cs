namespace Jellyfin.Plugin.Ifdb
{
    internal sealed class Constants
    {
        public const string PluginName = "IFDB";
        public const string PluginGuid = "0f18c602-5d24-4f88-bb6e-9e59a2efb1cd";
        public const string IfdbSearchURL = "https://fanedit.org/fanedit-search/search-results/?query=all&scope=title&keywords={0}&order=alpha";
        public const string IfdbMovieURL = "https://fanedit.org/fanedit-search/search-results/?p={0}";
        public const int RequestDelay = 0;
    }
}
