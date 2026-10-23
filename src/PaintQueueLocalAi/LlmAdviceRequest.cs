namespace PaintQueueLocalAi;

/// <summary>What the engine shows the advisor. Safety holds are never submitted.</summary>
public sealed record LlmAdviceRequest
{
    /// <summary>The shop snapshot the rules already scored.</summary>
    public required DispatchRequest Dispatch { get; init; }

    /// <summary>Rule order, already trimmed to <see cref="DispatchPolicy.AdvisorTopK"/>.</summary>
    public required IReadOnlyList<CandidateScore> TopCandidates { get; init; }

    /// <summary>Policy that produced <see cref="TopCandidates"/>.</summary>
    public required DispatchPolicy Policy { get; init; }

    /// <summary>Sequence weight used for this request, after the urgent reduction.</summary>
    public required decimal AppliedMinSequenceWeight { get; init; }

    /// <summary>Short Chinese summary of the rule ranking, suitable as prompt context.</summary>
    public required string RuleSummary { get; init; }
}
