namespace PaintQueueLocalAi;

/// <summary>Outcome of <see cref="IDispatchEngine.CommitReleaseAsync"/>.</summary>
public sealed record ReleaseResult
{
    /// <summary>The evaluation that was committed, or the hold that skipped the gate.</summary>
    public required DispatchDecision Decision { get; init; }

    /// <summary>True when the physical-release callback was invoked while the token was held.</summary>
    public required bool Committed { get; init; }

    /// <summary>Shared resource id from the request.</summary>
    public required string ResourceId { get; init; }

    /// <summary>Every lock taken for the commit. Empty when <see cref="Committed"/> is false.</summary>
    public required IReadOnlyList<string> LockedResourceIds { get; init; }

    /// <summary>Token id while the callback ran. Null when nothing was committed.</summary>
    public Guid? TokenId { get; init; }
}
