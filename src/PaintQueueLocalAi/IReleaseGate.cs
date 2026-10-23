namespace PaintQueueLocalAi;

/// <summary>
/// 对共享出口和输送链上的物理放行做串行化。
/// 不同资源编号可以并行。调用方传入有重叠的集合时，按编号的序数顺序获取，
/// 以免相反的获取顺序互相死锁。
/// 实现必须能安全地被并发调用。本进程是边界；
/// 命名互斥量或 PLC 令牌可以替换 <see cref="ResourceTokenReleaseGate"/>，而不必改引擎。
/// </summary>
public interface IReleaseGate
{
    /// <summary>获取一个资源。等价于对单个编号调用 <see cref="AcquireManyAsync"/>。</summary>
    ValueTask<IReleaseToken> AcquireAsync(string resourceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取全部编号。重复项会被忽略。任何等待之前先按序数顺序排序。
    /// 取消令牌会释放本次调用已经取得的资源。
    /// </summary>
    ValueTask<IReleaseToken> AcquireManyAsync(IReadOnlyList<string> resourceIds, CancellationToken cancellationToken = default);
}
