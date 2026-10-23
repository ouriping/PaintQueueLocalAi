namespace PaintQueueLocalAi;

/// <summary>
/// Immutable snapshot of one release request. Do not mutate the lists while
/// <see cref="IDispatchEngine.EvaluateAsync"/> is running; queue reads proceed in parallel.
/// </summary>
public sealed record DispatchRequest
{
    /// <summary>Caller-defined id, copied onto the decision.</summary>
    public required string RequestId { get; init; }

    /// <summary>
    /// Shared physical resource this release will occupy (merge conveyor, skid lift, common exit).
    /// Callers that can move the same hardware must pass the same id and share one <see cref="IReleaseGate"/>.
    /// </summary>
    public required string SharedResourceId { get; init; }

    /// <summary>Booth-exit line that is asking for a body. Trace context; it does not open the fast lane.</summary>
    public required BoothExitId RequestingExit { get; init; }

    /// <summary>
    /// The exit needs a body now. The engine still releases the best current candidate,
    /// but the min-sequence weight is multiplied by <see cref="DispatchPolicy.UrgentMinSequenceWeightFactor"/>.
    /// </summary>
    public bool IsUrgent { get; init; }

    /// <summary>Buffer lanes. About eight on the target line; the engine does not require that count.</summary>
    public required IReadOnlyList<BufferQueue> Queues { get; init; }

    /// <summary>Exit snapshots. Missing A simply means the fast lane has no candidate.</summary>
    public required IReadOnlyList<BoothExit> Exits { get; init; }

    /// <summary>Interlocks sampled with the queues. A global hold suppresses every candidate.</summary>
    public required SafetyInterlockState Safety { get; init; }

    /// <summary>Trace timestamp. The score does not read the clock.</summary>
    public DateTimeOffset RequestedAt { get; init; } = DateTimeOffset.UtcNow;
}
