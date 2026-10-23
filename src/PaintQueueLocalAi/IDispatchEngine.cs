namespace PaintQueueLocalAi;

/// <summary>
/// Rules-first release engine. Implementations must be safe for concurrent
/// <see cref="EvaluateAsync"/> calls. <see cref="CommitReleaseAsync"/> is what takes the shared gate.
/// </summary>
public interface IDispatchEngine
{
    /// <summary>Knobs captured when the engine was constructed.</summary>
    DispatchPolicy Policy { get; }

    /// <summary>
    /// Score the snapshot and return a decision. Does not acquire <see cref="IReleaseGate"/> and does not move hardware.
    /// Queue heads are read in parallel. A missing or slow advisor cannot change a safety hold.
    /// </summary>
    Task<DispatchDecision> EvaluateAsync(DispatchRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluate, then, when the decision releases a body, acquire the shared resource together with
    /// the lane or exit-A key and invoke <paramref name="physicalRelease"/> while the token is held.
    /// The token is disposed before this method returns, including when the callback throws.
    /// </summary>
    Task<ReleaseResult> CommitReleaseAsync(
        DispatchRequest request,
        Func<DispatchDecision, IReleaseToken, CancellationToken, Task>? physicalRelease = null,
        CancellationToken cancellationToken = default);
}
