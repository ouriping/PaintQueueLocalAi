namespace PaintQueueLocalAi;

/// <summary>Why the engine did or did not release.</summary>
public enum DispatchReason
{
    /// <summary>A global interlock forbids motion. No candidate was scored.</summary>
    SafetyInterlock = 0,

    /// <summary>Interlocks are clear, but no buffer head or fast-lane body can move.</summary>
    NoEligibleCandidate = 1,

    /// <summary>The deterministic ranking chose the body. The advisor was absent, agreed, or was rejected.</summary>
    PolicySelection = 2,

    /// <summary>An accepted advisor reorder changed which body is rank 1.</summary>
    AdvisorRerank = 3
}

/// <summary>What happened when the optional local advisor was asked to look at the top candidates.</summary>
public enum AdvisorOutcome
{
    /// <summary>The advisor was not called. Safety holds and empty candidate sets skip it.</summary>
    NotConsulted = 0,

    /// <summary>The advisor returned advice the engine accepted.</summary>
    Applied = 1,

    /// <summary>The advisor returned null. The rule order stands. This is the normal no-model path.</summary>
    Declined = 2,

    /// <summary>The advisor named an unknown id or a duplicate. The rule order stands.</summary>
    InvalidReorder = 3,

    /// <summary>The advisor exceeded <see cref="DispatchPolicy.AdvisorTimeout"/> or the caller token was still open.</summary>
    TimedOut = 4,

    /// <summary>The advisor threw. The rule order stands.</summary>
    Faulted = 5
}

/// <summary>Deterministic result of one evaluation. Physical motion has not happened yet.</summary>
public sealed record DispatchDecision
{
    /// <summary>Copy of <see cref="DispatchRequest.RequestId"/>.</summary>
    public required string RequestId { get; init; }

    /// <summary>True when a body should be released and <see cref="Selected"/> is populated.</summary>
    public required bool ShouldRelease { get; init; }

    /// <summary>Coarse reason. Combine with <see cref="AdvisorOutcome"/> for the advisor path.</summary>
    public required DispatchReason Reason { get; init; }

    /// <summary>Rank-1 candidate, or null when <see cref="ShouldRelease"/> is false.</summary>
    public CandidateScore? Selected { get; init; }

    /// <summary>Full ranking. Empty when nothing was scored. Rank 1 is <see cref="Selected"/>.</summary>
    public required IReadOnlyList<CandidateScore> RankedCandidates { get; init; }

    /// <summary>Chinese explanation suitable for a local HMI or the demo transcript.</summary>
    public required string Explanation { get; init; }

    /// <summary>Copy of the request urgency flag.</summary>
    public required bool IsUrgent { get; init; }

    /// <summary>Min-sequence weight actually used. Reduced when <see cref="IsUrgent"/> is true.</summary>
    public required decimal AppliedMinSequenceWeight { get; init; }

    /// <summary>Global interlock result. <see cref="SafetyHoldKind.None"/> when release was allowed to be scored.</summary>
    public required SafetyHoldKind SafetyHold { get; init; }

    /// <summary>How many buffer lanes the scheduler was asked to read.</summary>
    public required int QueuesEvaluated { get; init; }

    /// <summary>Advisor path taken for this decision.</summary>
    public required AdvisorOutcome AdvisorOutcome { get; init; }

    /// <summary>True when an accepted reorder changed the candidate sequence.</summary>
    public required bool AdvisorReordered { get; init; }

    /// <summary>Model text, or a short fault message. Null when the advisor said nothing.</summary>
    public string? AdvisorExplanation { get; init; }

    /// <summary>The advisor was called.</summary>
    public bool AdvisorConsulted => AdvisorOutcome != AdvisorOutcome.NotConsulted;

    /// <summary>The advisor's reply was accepted.</summary>
    public bool AdvisorApplied => AdvisorOutcome == AdvisorOutcome.Applied;

    /// <summary>
    /// The rule order was kept because the advisor was missing, too slow, invalid, or threw.
    /// A safety hold is not a fallback; the advisor is simply not asked.
    /// </summary>
    public bool FellBackToRules => AdvisorOutcome is AdvisorOutcome.Declined
        or AdvisorOutcome.InvalidReorder
        or AdvisorOutcome.TimedOut
        or AdvisorOutcome.Faulted;
}
