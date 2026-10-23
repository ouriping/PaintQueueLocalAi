namespace PaintQueueLocalAi;

/// <summary>
/// One FIFO buffer lane. Index 0 is the head and the only body that can be released.
/// Bodies behind the head are visible for traces, but they are not candidates.
/// </summary>
public sealed record BufferQueue
{
    /// <summary>Lane id, unique inside one request. Also used as a lock key via <see cref="ReleaseResources.Queue"/>.</summary>
    public required string Id { get; init; }

    /// <summary>Head at index 0, then the bodies behind it. Empty when the lane has nothing to release.</summary>
    public IReadOnlyList<Vehicle> Vehicles { get; init; } = Array.Empty<Vehicle>();

    /// <summary>Physical slot count. Fill ratio is <c>Vehicles.Count / Capacity</c>, clamped to 1.</summary>
    public int Capacity { get; init; } = 10;

    /// <summary>Mechanical or quality hold on this lane. The head is not a candidate while this is set.</summary>
    public bool ReleaseBlocked { get; init; }

    /// <summary>Optional reason shown to operators. Not interpreted by the engine.</summary>
    public string? BlockReason { get; init; }
}
