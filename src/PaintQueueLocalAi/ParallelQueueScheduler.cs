namespace PaintQueueLocalAi;

/// <summary>
/// 在线程池上评估缓存车道，同时最多 <see cref="MaxDegreeOfParallelism"/> 条。
/// 默认上限为 8，与目标产线的涂装缓存数量一致。这里不做物理放行；
/// 那一步由 <see cref="IReleaseGate"/> 另行串行化。
/// </summary>
public sealed class ParallelQueueScheduler : IQueueEvaluationScheduler
{
    /// <summary>共享调度器，最多同时读取 8 条车道。</summary>
    public static ParallelQueueScheduler Instance { get; } = new(8);

    private readonly int _maxDegreeOfParallelism;

    /// <summary>创建调度器，重叠的车道数不超过 <paramref name="maxDegreeOfParallelism"/>。</summary>
    public ParallelQueueScheduler(int maxDegreeOfParallelism)
    {
        if (maxDegreeOfParallelism <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDegreeOfParallelism),
                maxDegreeOfParallelism,
                "Parallelism must be at least 1.");
        }

        _maxDegreeOfParallelism = maxDegreeOfParallelism;
    }

    /// <summary>配置的重叠上限。</summary>
    public int MaxDegreeOfParallelism => _maxDegreeOfParallelism;

    /// <inheritdoc />
    public async Task RunAsync(
        IReadOnlyList<Func<CancellationToken, Task>> queueEvaluations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(queueEvaluations);
        if (queueEvaluations.Count == 0)
        {
            return;
        }

        using var limiter = new SemaphoreSlim(_maxDegreeOfParallelism, _maxDegreeOfParallelism);
        var tasks = new Task[queueEvaluations.Count];
        for (var i = 0; i < queueEvaluations.Count; i++)
        {
            var evaluation = queueEvaluations[i] ?? throw new ArgumentException("Queue evaluation is required.", nameof(queueEvaluations));
            tasks[i] = RunOneAsync(evaluation, limiter, cancellationToken);
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static async Task RunOneAsync(
        Func<CancellationToken, Task> evaluation,
        SemaphoreSlim limiter,
        CancellationToken cancellationToken)
    {
        await limiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 队列读取通常是同步的 PLC 快照。Task.Run 让这些读取
            // 占用不同的线程池线程，而不是在调用方上一条接一条排空。
            await Task.Run(() => evaluation(cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            limiter.Release();
        }
    }
}
