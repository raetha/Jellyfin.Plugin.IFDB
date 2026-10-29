using System;
using Xunit;

namespace Jellyfin.Plugin.Ifdb.Tests.Support;

/// <summary>
/// A test that talks to the real fanedit.org. Skipped unless the IFDB_LIVE_TESTS environment
/// variable is "1", so normal builds never depend on the site (or its bot protection).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("IFDB_LIVE_TESTS") != "1")
        {
            Skip = "Live fanedit.org test; set IFDB_LIVE_TESTS=1 to run.";
        }
    }
}
