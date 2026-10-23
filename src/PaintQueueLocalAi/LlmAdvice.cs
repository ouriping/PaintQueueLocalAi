namespace PaintQueueLocalAi;

/// <summary>Optional local-model reply. Returning null from the advisor means "no advice".</summary>
public sealed record LlmAdvice
{
    /// <summary>
    /// Desired order of <see cref="CandidateScore.CandidateId"/> values from the top-K window.
    /// Null keeps the rule order. Ids outside that window, duplicates, or an empty list are rejected.
    /// A partial list is completed with the remaining top-K ids in their original order.
    /// </summary>
    public IReadOnlyList<string>? ReorderedCandidateIds { get; init; }

    /// <summary>Optional explanation. The engine stores at most 2000 characters. It never changes the ranking by itself.</summary>
    public string? Explanation { get; init; }
}
