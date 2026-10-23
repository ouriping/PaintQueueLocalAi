using PaintQueueLocalAi;

namespace PaintQueueLocalAi.Tests;

internal static class Shop
{
    public static Vehicle Car(string id, int sequence, BoothExitId source = BoothExitId.B, string? color = null) => new()
    {
        Id = id,
        SequenceNumber = sequence,
        SourceExit = source,
        ColorCode = color
    };

    public static BufferQueue Lane(string id, int capacity, params Vehicle[] vehicles) => new()
    {
        Id = id,
        Capacity = capacity,
        Vehicles = vehicles
    };

    public static BufferQueue BlockedLane(string id, int capacity, params Vehicle[] vehicles) => new()
    {
        Id = id,
        Capacity = capacity,
        ReleaseBlocked = true,
        Vehicles = vehicles
    };

    public static BoothExit Exit(BoothExitId id, Vehicle? present = null, bool fastLaneOpen = false, bool blocked = false) => new()
    {
        Id = id,
        PresentVehicle = present,
        FastLaneOpen = fastLaneOpen,
        ReleaseBlocked = blocked
    };

    public static DispatchRequest Request(
        string requestId,
        IReadOnlyList<BufferQueue> queues,
        bool urgent = false,
        BoothExitId requestingExit = BoothExitId.B,
        IReadOnlyList<BoothExit>? exits = null,
        SafetyInterlockState? safety = null,
        string resourceId = "MERGE-01") => new()
    {
        RequestId = requestId,
        SharedResourceId = resourceId,
        RequestingExit = requestingExit,
        IsUrgent = urgent,
        Queues = queues,
        Exits = exits ?? new[] { Exit(BoothExitId.A), Exit(BoothExitId.B) },
        Safety = safety ?? SafetyInterlockState.Nominal
    };
}

internal sealed class FlagAdvisor : ILlmAdvisor
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public Func<LlmAdviceRequest, LlmAdvice?> Reply { get; set; } = _ => null;

    public Task<LlmAdvice?> AdviseAsync(LlmAdviceRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Reply(request));
    }
}

internal sealed class SlowAdvisor : ILlmAdvisor
{
    private readonly TimeSpan _delay;

    public SlowAdvisor(TimeSpan delay) => _delay = delay;

    public async Task<LlmAdvice?> AdviseAsync(LlmAdviceRequest request, CancellationToken cancellationToken)
    {
        await Task.Delay(_delay, cancellationToken);
        return new LlmAdvice { Explanation = "来不及了" };
    }
}

internal sealed class ThrowingAdvisor : ILlmAdvisor
{
    public Task<LlmAdvice?> AdviseAsync(LlmAdviceRequest request, CancellationToken cancellationToken)
        => throw new InvalidOperationException("model file missing");
}

internal sealed class CountingGate : IReleaseGate
{
    private readonly ResourceTokenReleaseGate _inner = new();
    private int _acquires;

    public int Acquires => Volatile.Read(ref _acquires);

    public ValueTask<IReleaseToken> AcquireAsync(string resourceId, CancellationToken cancellationToken = default)
        => AcquireManyAsync(new[] { resourceId }, cancellationToken);

    public ValueTask<IReleaseToken> AcquireManyAsync(IReadOnlyList<string> resourceIds, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _acquires);
        return _inner.AcquireManyAsync(resourceIds, cancellationToken);
    }
}

internal sealed class RecordingScheduler : IQueueEvaluationScheduler
{
    public int LastCount { get; private set; }

    public async Task RunAsync(IReadOnlyList<Func<CancellationToken, Task>> queueEvaluations, CancellationToken cancellationToken)
    {
        LastCount = queueEvaluations.Count;
        foreach (var evaluation in queueEvaluations)
        {
            await evaluation(cancellationToken);
        }
    }
}

internal sealed class SkippingScheduler : IQueueEvaluationScheduler
{
    public Task RunAsync(IReadOnlyList<Func<CancellationToken, Task>> queueEvaluations, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
