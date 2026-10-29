using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Ifdb.Api;

/// <summary>
/// State shared by every <see cref="IfdbClient"/> on a server: the page cache, the request
/// queue that spaces requests out, and when the last blocked warning was logged.
/// </summary>
internal sealed class IfdbRequestState : IDisposable
{
    private readonly ConcurrentDictionary<string, CachedPage> _pages = new(StringComparer.Ordinal);
    private long _lastRequestTicks;
    private long _lastBlockedWarningTicks;

    public IfdbRequestState(int maxCachedPages = 500)
    {
        MaxCachedPages = Math.Max(1, maxCachedPages);
    }

    /// <summary>
    /// Gets the state used by the running server.
    /// </summary>
    public static IfdbRequestState Shared { get; } = new();

    public int MaxCachedPages { get; }

    public int CachedPageCount => _pages.Count;

    /// <summary>
    /// Gets the queue that lets one request to fanedit.org run at a time.
    /// </summary>
    public SemaphoreSlim RequestGate { get; } = new(1, 1);

    public DateTimeOffset LastRequest
    {
        get => new(Interlocked.Read(ref _lastRequestTicks), TimeSpan.Zero);
        set => Interlocked.Exchange(ref _lastRequestTicks, value.UtcTicks);
    }

    public bool TryGetPage(string url, DateTimeOffset now, [NotNullWhen(true)] out Task<IfdbPage>? fetch)
    {
        if (_pages.TryGetValue(url, out var cached)
            && cached.ExpiresAt > now
            && !cached.Fetch.IsFaulted
            && !cached.Fetch.IsCanceled)
        {
            fetch = cached.Fetch;
            return true;
        }

        fetch = null;
        return false;
    }

    public void SetPage(string url, Task<IfdbPage> fetch, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        if (!_pages.ContainsKey(url))
        {
            Prune(now);
        }

        _pages[url] = new CachedPage(fetch, expiresAt);
    }

    public void RemovePage(string url) => _pages.TryRemove(url, out _);

    /// <summary>
    /// Records that a blocked warning is due, at most once per <paramref name="interval"/>.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <param name="interval">The minimum time between warnings.</param>
    /// <returns><c>true</c> if this caller should log the warning.</returns>
    public bool TryClaimBlockedWarning(DateTimeOffset now, TimeSpan interval)
    {
        var last = Interlocked.Read(ref _lastBlockedWarningTicks);
        return now.UtcTicks - last >= interval.Ticks
            && Interlocked.CompareExchange(ref _lastBlockedWarningTicks, now.UtcTicks, last) == last;
    }

    public void Dispose() => RequestGate.Dispose();

    private void Prune(DateTimeOffset now)
    {
        if (_pages.Count < MaxCachedPages)
        {
            return;
        }

        foreach (var item in _pages.Where(i => i.Value.ExpiresAt <= now).ToList())
        {
            _pages.TryRemove(item.Key, out _);
        }

        // Still full of live pages: drop the ones closest to expiring, leaving a quarter free.
        var excess = _pages.Count - (MaxCachedPages * 3 / 4);
        foreach (var key in _pages.OrderBy(i => i.Value.ExpiresAt).Take(Math.Max(0, excess)).Select(i => i.Key).ToList())
        {
            _pages.TryRemove(key, out _);
        }
    }

    private sealed class CachedPage
    {
        public CachedPage(Task<IfdbPage> fetch, DateTimeOffset expiresAt)
        {
            Fetch = fetch;
            ExpiresAt = expiresAt;
        }

        public Task<IfdbPage> Fetch { get; }

        public DateTimeOffset ExpiresAt { get; }
    }
}
