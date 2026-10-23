using PaintQueueLocalAi;
using PaintQueueLocalAi.Simulation;

namespace PaintQueueLocalAi.Tests;

public class SafetyAndGateTests
{
    [Fact]
    public async Task EStop_holds_without_asking_the_advisor_or_the_gate()
    {
        var advisor = new FlagAdvisor();
        var gate = new CountingGate();
        var engine = new DispatchEngine(advisor: advisor, releaseGate: gate);
        var called = false;
        var request = Shop.Request(
            "estop",
            SampleSnapshots.FastLane().Queues,
            urgent: true,
            exits: SampleSnapshots.FastLane().Exits,
            safety: new SafetyInterlockState { EStopActive = true, Message = "线体急停" });

        var result = await engine.CommitReleaseAsync(request, (_, _, _) =>
        {
            called = true;
            return Task.CompletedTask;
        });

        Assert.False(result.Committed);
        Assert.False(result.Decision.ShouldRelease);
        Assert.Equal(DispatchReason.SafetyInterlock, result.Decision.Reason);
        Assert.Equal(SafetyHoldKind.EStop, result.Decision.SafetyHold);
        Assert.Equal(AdvisorOutcome.NotConsulted, result.Decision.AdvisorOutcome);
        Assert.False(result.Decision.FellBackToRules);
        Assert.Equal(0, result.Decision.QueuesEvaluated);
        Assert.Equal(0, advisor.Calls);
        Assert.Equal(0, gate.Acquires);
        Assert.False(called);
        Assert.Empty(result.LockedResourceIds);
        Assert.Contains("急停", result.Decision.Explanation);
    }

    [Fact]
    public async Task Conveyor_not_ready_and_blocked_merge_also_hold()
    {
        var engine = new DispatchEngine();
        var queues = new[] { Shop.Lane("BUF-01", 10, Shop.Car("B-0001", 1)) };
        var conveyor = await engine.EvaluateAsync(Shop.Request(
            "conveyor",
            queues,
            safety: new SafetyInterlockState { ConveyorReady = false }));
        var blocked = await engine.EvaluateAsync(Shop.Request(
            "blocked-merge",
            queues,
            safety: new SafetyInterlockState { BlockedResourceIds = new[] { "MERGE-01" } }));

        Assert.Equal(SafetyHoldKind.ConveyorNotReady, conveyor.SafetyHold);
        Assert.Equal(SafetyHoldKind.SharedResourceBlocked, blocked.SafetyHold);
        Assert.False(conveyor.ShouldRelease);
        Assert.False(blocked.ShouldRelease);
    }

    [Fact]
    public async Task Evaluate_does_not_acquire_the_gate_and_commit_locks_the_lane_plus_merge()
    {
        var gate = new CountingGate();
        var engine = new DispatchEngine(releaseGate: gate);
        var request = SampleSnapshots.MinSequence();

        var decision = await engine.EvaluateAsync(request);
        Assert.Equal(0, gate.Acquires);

        var result = await engine.CommitReleaseAsync(request);
        Assert.True(result.Committed);
        Assert.Equal(1, gate.Acquires);
        Assert.Contains(SampleSnapshots.Merge, result.LockedResourceIds);
        Assert.Contains(ReleaseResources.Queue("BUF-02"), result.LockedResourceIds);
        Assert.NotNull(result.TokenId);
    }

    [Fact]
    public async Task Fast_lane_commit_locks_exit_a()
    {
        var engine = new DispatchEngine();
        var result = await engine.CommitReleaseAsync(SampleSnapshots.FastLane());

        Assert.Equal("B-0020", result.Decision.Selected!.VehicleId);
        Assert.Contains(ReleaseResources.ExitA, result.LockedResourceIds);
        Assert.Contains(SampleSnapshots.Merge, result.LockedResourceIds);
    }

    [Fact]
    public async Task Two_queues_sharing_a_gate_do_not_overlap()
    {
        var gate = new ResourceTokenReleaseGate();
        var engine = new DispatchEngine(releaseGate: gate);
        var pair = SampleSnapshots.ContendedPair();
        var current = 0;
        var max = 0;

        async Task<ReleaseResult> ReleaseAsync(DispatchRequest request)
        {
            return await engine.CommitReleaseAsync(request, async (_, _, ct) =>
            {
                var now = Interlocked.Increment(ref current);
                int observed;
                do
                {
                    observed = max;
                    if (now <= observed)
                    {
                        break;
                    }
                }
                while (Interlocked.CompareExchange(ref max, now, observed) != observed);

                try
                {
                    await Task.Delay(80, ct);
                }
                finally
                {
                    Interlocked.Decrement(ref current);
                }
            });
        }

        var results = await Task.WhenAll(ReleaseAsync(pair.Left), ReleaseAsync(pair.Right));

        Assert.Equal(1, max);
        Assert.All(results, result => Assert.True(result.Committed));
        Assert.Equal(new[] { "B-0001", "B-0002" }, results.Select(result => result.Decision.Selected!.VehicleId).OrderBy(id => id).ToArray());
    }

    [Fact]
    public async Task Independent_resources_may_release_together()
    {
        var gate = new ResourceTokenReleaseGate();
        var engine = new DispatchEngine(releaseGate: gate);
        using var barrier = new Barrier(2);

        Task<ReleaseResult> Go(string resourceId, string queueId, string vehicleId) =>
            engine.CommitReleaseAsync(
                Shop.Request(resourceId, new[] { Shop.Lane(queueId, 5, Shop.Car(vehicleId, 1)) }, resourceId: resourceId),
                (_, _, _) =>
                {
                    if (!barrier.SignalAndWait(TimeSpan.FromSeconds(2)))
                    {
                        throw new TimeoutException("Disjoint resources were serialized.");
                    }

                    return Task.CompletedTask;
                });

        var results = await Task.WhenAll(
            Go("MERGE-A", "BUF-01", "B-0001"),
            Go("MERGE-B", "BUF-02", "B-0002"));

        Assert.All(results, result => Assert.True(result.Committed));
    }

    [Fact]
    public async Task Opposite_lock_order_does_not_deadlock()
    {
        var gate = new ResourceTokenReleaseGate();
        var holding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task HoldAsync(IReadOnlyList<string> ids, bool signal)
        {
            await using var token = await gate.AcquireManyAsync(ids);
            if (signal)
            {
                holding.TrySetResult();
            }

            await Task.Delay(40);
        }

        var first = HoldAsync(new[] { "merge", "queue:A" }, signal: true);
        await holding.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = HoldAsync(new[] { "queue:A", "merge" }, signal: false);
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Callback_exception_releases_the_gate()
    {
        var gate = new ResourceTokenReleaseGate();
        var engine = new DispatchEngine(releaseGate: gate);
        var request = Shop.Request("fault", new[] { Shop.Lane("BUF-01", 5, Shop.Car("B-0001", 1)) });

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.CommitReleaseAsync(
            request,
            (_, _, _) => throw new InvalidOperationException("plc fault")));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var token = await gate.AcquireManyAsync(
            new[] { request.SharedResourceId, ReleaseResources.Queue("BUF-01") },
            timeout.Token);
        Assert.Equal(2, token.ResourceIds.Count);
    }
}
