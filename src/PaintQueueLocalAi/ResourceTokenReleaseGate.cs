using System.Collections.Concurrent;

namespace PaintQueueLocalAi;

/// <summary>
/// In-process token gate. One semaphore per resource id. Safe for concurrent evaluators;
/// only the commit path should call it. The dictionary grows with distinct ids, which on this
/// line is a handful of conveyors and lanes.
/// </summary>
public sealed class ResourceTokenReleaseGate : IReleaseGate
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask<IReleaseToken> AcquireAsync(string resourceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            throw new ArgumentException("Resource id is required.", nameof(resourceId));
        }

        return AcquireManyAsync(new[] { resourceId }, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<IReleaseToken> AcquireManyAsync(
        IReadOnlyList<string> resourceIds,
        CancellationToken cancellationToken = default)
    {
        var ordered = Normalize(resourceIds);
        var acquired = new List<SemaphoreSlim>(ordered.Count);
        try
        {
            foreach (var id in ordered)
            {
                var semaphore = _locks.GetOrAdd(id, static _ => new SemaphoreSlim(1, 1));
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                acquired.Add(semaphore);
            }
        }
        catch
        {
            Release(acquired);
            throw;
        }

        return new Token(ordered, acquired.ToArray());
    }

    private static IReadOnlyList<string> Normalize(IReadOnlyList<string> resourceIds)
    {
        if (resourceIds is null || resourceIds.Count == 0)
        {
            throw new ArgumentException("At least one resource id is required.", nameof(resourceIds));
        }

        var distinct = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in resourceIds)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Resource id is required.", nameof(resourceIds));
            }

            distinct.Add(id.Trim());
        }

        var ordered = distinct.ToArray();
        Array.Sort(ordered, StringComparer.Ordinal);
        return ordered;
    }

    private static void Release(List<SemaphoreSlim> acquired)
    {
        for (var i = acquired.Count - 1; i >= 0; i--)
        {
            acquired[i].Release();
        }
    }

    private sealed class Token : IReleaseToken
    {
        private readonly SemaphoreSlim[] _semaphores;
        private int _disposed;

        public Token(IReadOnlyList<string> resourceIds, SemaphoreSlim[] semaphores)
        {
            ResourceIds = resourceIds;
            _semaphores = semaphores;
            TokenId = Guid.NewGuid();
            AcquiredAt = DateTimeOffset.UtcNow;
        }

        public IReadOnlyList<string> ResourceIds { get; }

        public Guid TokenId { get; }

        public DateTimeOffset AcquiredAt { get; }

        public void Dispose() => ReleaseAll();

        public ValueTask DisposeAsync()
        {
            ReleaseAll();
            return ValueTask.CompletedTask;
        }

        private void ReleaseAll()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            for (var i = _semaphores.Length - 1; i >= 0; i--)
            {
                _semaphores[i].Release();
            }
        }
    }
}
