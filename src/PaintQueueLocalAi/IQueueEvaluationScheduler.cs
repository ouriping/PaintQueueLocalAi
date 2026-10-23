namespace PaintQueueLocalAi;

/// <summary>
/// Runs one independent read per buffer lane. The default implementation overlaps those reads.
/// A replacement must invoke every action exactly once; the engine fails the decision if an action
/// never finishes. Actions may run concurrently and must only write to their own result slot.
/// </summary>
public interface IQueueEvaluationScheduler
{
    /// <summary>Run every queue evaluation. An empty list completes immediately.</summary>
    Task RunAsync(IReadOnlyList<Func<CancellationToken, Task>> queueEvaluations, CancellationToken cancellationToken);
}
