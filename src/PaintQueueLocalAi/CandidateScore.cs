namespace PaintQueueLocalAi;

/// <summary>Where a releasable body was found.</summary>
public enum CandidateOrigin
{
    /// <summary>Head of a buffer lane (index 0). Bodies behind the head never appear.</summary>
    BufferHead = 0,

    /// <summary>Body sitting on booth exit A with the fast lane open.</summary>
    FastLaneA = 1
}

/// <summary>One releasable body and the weights that ranked it.</summary>
public sealed record CandidateScore
{
    /// <summary>
    /// Id the advisor must use when reordering.
    /// Buffer heads use <c>buffer:{queueId}:{vehicleId}</c>. The fast lane uses <c>fast-lane-a:{vehicleId}</c>.
    /// </summary>
    public required string CandidateId { get; init; }

    /// <summary>Body id.</summary>
    public required string VehicleId { get; init; }

    /// <summary>Production sequence of this body.</summary>
    public required int SequenceNumber { get; init; }

    /// <summary>Buffer head or exit-A fast lane.</summary>
    public required CandidateOrigin Origin { get; init; }

    /// <summary>Lane id for a buffer head; null for the fast lane.</summary>
    public string? QueueId { get; init; }

    /// <summary>Booth that owns the release point. Fast-lane candidates are always <see cref="BoothExitId.A"/>.</summary>
    public required BoothExitId SourceExit { get; init; }

    /// <summary>
    /// Hard tier. Fast lane is 1, buffer heads are 0.
    /// The rule sort orders by tier first, then by <see cref="Score"/>.
    /// An accepted advisor reorder may place a lower tier first.
    /// </summary>
    public required int PriorityTier { get; init; }

    /// <summary><see cref="SequenceComponent"/> plus <see cref="LoadBalanceComponent"/>.</summary>
    public required decimal Score { get; init; }

    /// <summary>Applied min-sequence weight times closeness to the smallest buffer-head sequence.</summary>
    public required decimal SequenceComponent { get; init; }

    /// <summary>Load-balance weight times the lane fill ratio. Zero for the fast lane.</summary>
    public required decimal LoadBalanceComponent { get; init; }

    /// <summary>Lane fill from 0 to 1. Zero for the fast lane, which is not a buffer.</summary>
    public required decimal FillRatio { get; init; }

    /// <summary>1-based rank after the rule sort and any accepted advisor reorder. Rank 1 is released.</summary>
    public required int Rank { get; init; }

    /// <summary>Copy of <see cref="Vehicle.ColorCode"/> for the HMI.</summary>
    public string? ColorCode { get; init; }
}
