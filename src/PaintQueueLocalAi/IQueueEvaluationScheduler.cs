namespace PaintQueueLocalAi;

/// <summary>
/// 每条缓存车道做一次独立读取。默认实现会重叠这些读取。
/// 替换实现必须把每个动作正好调用一次；若某个动作始终不结束，引擎会使该次决策失败。
/// 动作可以并发运行，且只能写入自己的结果槽。
/// </summary>
public interface IQueueEvaluationScheduler
{
    /// <summary>运行全部队列评估。空列表立即完成。</summary>
    Task RunAsync(IReadOnlyList<Func<CancellationToken, Task>> queueEvaluations, CancellationToken cancellationToken);
}
