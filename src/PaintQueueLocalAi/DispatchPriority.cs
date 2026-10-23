namespace PaintQueueLocalAi;

/// <summary>决策阶梯，优先级从高到低。参见 <see cref="DispatchPriorities"/>。</summary>
public enum DispatchPriority
{
    /// <summary>全局联锁。一旦保持，决策即结束。</summary>
    SafetyInterlocks = 0,

    /// <summary>出口紧急要车。降低最小序号权重，并按当前快照提交。</summary>
    UrgentExitRequest = 1,

    /// <summary>喷房出口 A 可以直放，排在所有缓存车头之前。</summary>
    FastLaneA = 2,

    /// <summary>在缓存车头中优先最小生产序号。紧急时降低该项权重。</summary>
    MinSequencePreference = 3,

    /// <summary>序号接近程度无法决定时，优先更满的缓存。</summary>
    BufferLoadBalance = 4
}

/// <summary><see cref="DispatchEngine"/> 实现的优先级约定。</summary>
public static class DispatchPriorities
{
    /// <summary>优先级从高到低：安全、紧急模式、A 口快车道、最小序号，然后是负载均衡。</summary>
    public static IReadOnlyList<DispatchPriority> HighestFirst { get; } =
    [
        DispatchPriority.SafetyInterlocks,
        DispatchPriority.UrgentExitRequest,
        DispatchPriority.FastLaneA,
        DispatchPriority.MinSequencePreference,
        DispatchPriority.BufferLoadBalance
    ];
}
