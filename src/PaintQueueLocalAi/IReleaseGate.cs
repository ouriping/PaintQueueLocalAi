namespace PaintQueueLocalAi;

/// <summary>
/// Serializes physical release against shared exits and conveyors.
/// Different resource ids run in parallel. Callers that pass overlapping sets are acquired
/// in ordinal id order so two opposite orders cannot deadlock.
/// Implementations must be safe for concurrent callers. This process is the boundary;
/// a named mutex or PLC token can replace <see cref="ResourceTokenReleaseGate"/> without changing the engine.
/// </summary>
public interface IReleaseGate
{
    /// <summary>Acquire one resource. Equivalent to <see cref="AcquireManyAsync"/> with a single id.</summary>
    ValueTask<IReleaseToken> AcquireAsync(string resourceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquire every id. Duplicates are ignored. Ids are sorted ordinally before any wait.
    /// Canceling the token releases any resources already taken by this call.
    /// </summary>
    ValueTask<IReleaseToken> AcquireManyAsync(IReadOnlyList<string> resourceIds, CancellationToken cancellationToken = default);
}
