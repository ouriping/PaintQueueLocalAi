namespace PaintQueueLocalAi;

/// <summary>
/// 规则优先的放行引擎。实现必须能安全地并发调用
/// <see cref="EvaluateAsync"/>。获取共享放行闸的是 <see cref="CommitReleaseAsync"/>。
/// </summary>
public interface IDispatchEngine
{
    /// <summary>构造引擎时捕获的参数。</summary>
    DispatchPolicy Policy { get; }

    /// <summary>
    /// 对快照评分并返回决策。不获取 <see cref="IReleaseGate"/>，也不驱动硬件。
    /// 队列车头并行读取。顾问缺失或过慢不能改变安全保持。
    /// </summary>
    Task<DispatchDecision> EvaluateAsync(DispatchRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 先评估；若决策要放行车身，则同时获取共享资源以及
    /// 车道或出口 A 的锁键，并在持有令牌期间调用 <paramref name="physicalRelease"/>。
    /// 本方法返回前会释放令牌，回调抛出异常时也一样。
    /// </summary>
    Task<ReleaseResult> CommitReleaseAsync(
        DispatchRequest request,
        Func<DispatchDecision, IReleaseToken, CancellationToken, Task>? physicalRelease = null,
        CancellationToken cancellationToken = default);
}
