namespace PaintQueueLocalAi;

/// <summary>
/// Evaluates buffer lanes on the thread pool, at most <see cref="MaxDegreeOfParallelism"/> at a time.
/// The default cap is 8, matching the buffer bank on the target line. Physical release is not done here;
/// <see cref="IReleaseGate"/> serializes that separately.
/// </summary>
public sealed class ParallelQueueScheduler : IQueueEvaluationScheduler
{
    /// <summary>Shared scheduler capped at 8 concurrent lane reads.</summary>
    public static ParallelQueueScheduler Instance { get; } = new(8);

    private readonly int _maxDegreeOfParallelism;

    /// <summary>Create a scheduler that overlaps at most <paramref name="maxDegreeOfParallelism"/> lanes.</summary>
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

    /// <summary>Configured overlap cap.</summary>
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
            // Queue reads are usually synchronous PLC snapshots. Task.Run lets those reads
            // occupy different pool threads instead of draining one after another on the caller.
            await Task.Run(() => evaluation(cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            limiter.Release();
        }
    }
}
