using System;
using System.IO;

namespace Jellyfin.Plugin.Ifdb.Tests;

internal static class Fixture
{
    public static string Read(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
