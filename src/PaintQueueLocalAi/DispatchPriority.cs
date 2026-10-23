namespace PaintQueueLocalAi;

/// <summary>Decision ladder, highest priority first. See <see cref="DispatchPriorities"/>.</summary>
public enum DispatchPriority
{
    /// <summary>Global interlock. A hold ends the decision.</summary>
    SafetyInterlocks = 0,

    /// <summary>Urgent exit demand. Lowers the min-sequence weight and commits on the current snapshot.</summary>
    UrgentExitRequest = 1,

    /// <summary>Booth exit A may release directly, ahead of every buffer head.</summary>
    FastLaneA = 2,

    /// <summary>Prefer the smallest production sequence among buffer heads. Weight is reduced while urgent.</summary>
    MinSequencePreference = 3,

    /// <summary>Prefer the fuller buffer when sequence closeness does not decide.</summary>
    BufferLoadBalance = 4
}

/// <summary>The priority contract implemented by <see cref="DispatchEngine"/>.</summary>
public static class DispatchPriorities
{
    /// <summary>Highest priority first: safety, urgent mode, fast lane A, min sequence, then load balance.</summary>
    public static IReadOnlyList<DispatchPriority> HighestFirst { get; } =
    [
        DispatchPriority.SafetyInterlocks,
        DispatchPriority.UrgentExitRequest,
        DispatchPriority.FastLaneA,
        DispatchPriority.MinSequencePreference,
        DispatchPriority.BufferLoadBalance
    ];
}
