namespace PaintQueueLocalAi;

/// <summary>
/// 一次放行请求的不可变快照。<see cref="IDispatchEngine.EvaluateAsync"/> 运行期间不要修改这些列表；
/// 队列读取会并行进行。
/// </summary>
public sealed record DispatchRequest
{
    /// <summary>调用方定义的编号，会复制到决策上。</summary>
    public required string RequestId { get; init; }

    /// <summary>
    /// 本次放行将占用的共享物理资源（合流输送链、滑橇升降机、公共出口）。
    /// 能驱动同一硬件的调用方必须传入相同编号，并共用一个 <see cref="IReleaseGate"/>。
    /// </summary>
    public required string SharedResourceId { get; init; }

    /// <summary>正在要车的喷房出口线。仅作追溯，不会因此打开快车道。</summary>
    public required BoothExitId RequestingExit { get; init; }

    /// <summary>
    /// 出口现在就要车。引擎仍放行当前最优候选，
    /// 但最小序号权重会乘以 <see cref="DispatchPolicy.UrgentMinSequenceWeightFactor"/>。
    /// </summary>
    public bool IsUrgent { get; init; }

    /// <summary>缓存车道。目标产线大约八条；引擎不要求必须是这个数量。</summary>
    public required IReadOnlyList<BufferQueue> Queues { get; init; }

    /// <summary>出口快照。缺少 A 只表示快车道没有候选。</summary>
    public required IReadOnlyList<BoothExit> Exits { get; init; }

    /// <summary>与队列一起采样的联锁。全局保持会抑制全部候选。</summary>
    public required SafetyInterlockState Safety { get; init; }

    /// <summary>追溯用时间戳。评分不读时钟。</summary>
    public DateTimeOffset RequestedAt { get; init; } = DateTimeOffset.UtcNow;
}
