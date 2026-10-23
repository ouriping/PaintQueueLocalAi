using PaintQueueLocalAi;
using PaintQueueLocalAi.Simulation;

namespace PaintQueueLocalAi.Tests;

public class DispatchBehaviorTests
{
    [Fact]
    public void Priority_ladder_is_safety_then_urgent_then_fast_lane_then_sequence_then_load()
    {
        Assert.Equal(
            new[]
            {
                DispatchPriority.SafetyInterlocks,
                DispatchPriority.UrgentExitRequest,
                DispatchPriority.FastLaneA,
                DispatchPriority.MinSequencePreference,
                DispatchPriority.BufferLoadBalance
            },
            DispatchPriorities.HighestFirst);
    }

    [Fact]
    public void Default_policy_matches_the_published_knobs()
    {
        var policy = DispatchPolicy.Default;
        Assert.True(policy.EnableFastLaneA);
        Assert.Equal(400m, policy.MinSequenceWeight);
        Assert.Equal(0.2m, policy.UrgentMinSequenceWeightFactor);
        Assert.Equal(150m, policy.LoadBalanceWeight);
        Assert.Equal(3, policy.AdvisorTopK);
        Assert.Equal(TimeSpan.FromMilliseconds(200), policy.AdvisorTimeout);
    }

    [Fact]
    public void Policy_rejects_an_urgent_factor_that_would_raise_sequence_weight()
    {
        var policy = DispatchPolicy.Default with { UrgentMinSequenceWeightFactor = 1.1m };
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => new DispatchEngine(policy));
        Assert.Equal("UrgentMinSequenceWeightFactor", error.ParamName);
    }

    [Fact]
    public async Task MinSequence_releases_smallest_head_and_ignores_buried_and_exit_b()
    {
        var request = SampleSnapshots.MinSequence();
        var decision = await new DispatchEngine().EvaluateAsync(request);

        var heads = request.Queues.Where(queue => queue.Vehicles.Count > 0).Select(queue => queue.Vehicles[0]).ToArray();
        var smallest = heads.Min(vehicle => vehicle.SequenceNumber);
        Assert.True(decision.ShouldRelease);
        Assert.Equal(DispatchReason.PolicySelection, decision.Reason);
        Assert.Equal(smallest, decision.Selected!.SequenceNumber);
        Assert.Equal("B-0012", decision.Selected.VehicleId);
        Assert.Equal("BUF-02", decision.Selected.QueueId);
        Assert.Equal(CandidateOrigin.BufferHead, decision.Selected.Origin);
        Assert.Equal(8, decision.QueuesEvaluated);
        Assert.Equal(AdvisorOutcome.Declined, decision.AdvisorOutcome);
        Assert.True(decision.FellBackToRules);
        Assert.Contains("B-0012", decision.Explanation);

        var buried = request.Queues.SelectMany(queue => queue.Vehicles.Skip(1)).Select(vehicle => vehicle.Id).ToArray();
        Assert.NotEmpty(buried);
        Assert.All(buried, id => Assert.DoesNotContain(decision.RankedCandidates, candidate => candidate.VehicleId == id));
        Assert.DoesNotContain(decision.RankedCandidates, candidate => candidate.VehicleId == "B-0004");
        Assert.DoesNotContain(decision.RankedCandidates, candidate => candidate.Origin == CandidateOrigin.FastLaneA);
    }

    [Fact]
    public async Task FastLaneA_beats_a_smaller_buffer_sequence()
    {
        var decision = await new DispatchEngine().EvaluateAsync(SampleSnapshots.FastLane());

        Assert.Equal(CandidateOrigin.FastLaneA, decision.Selected!.Origin);
        Assert.Equal("B-0020", decision.Selected.VehicleId);
        Assert.Equal(BoothExitId.A, decision.Selected.SourceExit);
        Assert.Equal(1, decision.Selected.PriorityTier);
        var smaller = decision.RankedCandidates.Single(candidate => candidate.VehicleId == "B-0005");
        Assert.True(smaller.SequenceNumber < decision.Selected.SequenceNumber);
        Assert.True(smaller.Score > decision.Selected.Score);
        Assert.True(decision.Selected.Rank < smaller.Rank);
        Assert.Contains("快车道", decision.Explanation);
    }

    [Fact]
    public async Task Disabling_fast_lane_returns_to_min_sequence()
    {
        var engine = new DispatchEngine(DispatchPolicy.Default with { EnableFastLaneA = false });
        var decision = await engine.EvaluateAsync(SampleSnapshots.FastLane());

        Assert.Equal("B-0005", decision.Selected!.VehicleId);
        Assert.Equal(CandidateOrigin.BufferHead, decision.Selected.Origin);
    }

    [Fact]
    public async Task Closed_or_blocked_fast_lane_is_not_a_candidate()
    {
        var vehicle = Shop.Car("B-0002", 2, BoothExitId.A);
        var closed = Shop.Request(
            "closed",
            new[] { Shop.Lane("BUF-01", 10, Shop.Car("B-0009", 9)) },
            exits: new[] { Shop.Exit(BoothExitId.A, vehicle, fastLaneOpen: false), Shop.Exit(BoothExitId.B) });
        var blocked = Shop.Request(
            "blocked-exit",
            new[] { Shop.Lane("BUF-01", 10, Shop.Car("B-0009", 9)) },
            exits: new[] { Shop.Exit(BoothExitId.A, vehicle, fastLaneOpen: true, blocked: true), Shop.Exit(BoothExitId.B) });

        var closedDecision = await new DispatchEngine().EvaluateAsync(closed);
        var blockedDecision = await new DispatchEngine().EvaluateAsync(blocked);

        Assert.Equal("B-0009", closedDecision.Selected!.VehicleId);
        Assert.Equal("B-0009", blockedDecision.Selected!.VehicleId);
    }

    [Fact]
    public async Task Exit_b_cannot_release_directly_even_if_marked_open()
    {
        var decision = await new DispatchEngine().EvaluateAsync(SampleSnapshots.MinSequence());
        var exitB = SampleSnapshots.MinSequence().Exits.Single(exit => exit.Id == BoothExitId.B);
        Assert.True(exitB.FastLaneOpen);
        Assert.DoesNotContain(decision.RankedCandidates, candidate => candidate.VehicleId == exitB.PresentVehicle!.Id);
    }

    [Fact]
    public async Task Urgent_request_lowers_min_sequence_weight_and_full_buffer_wins()
    {
        var policy = DispatchPolicy.Default;
        var calmRequest = SampleSnapshots.Urgent(urgent: false);
        var urgentRequest = SampleSnapshots.Urgent(urgent: true);
        var engine = new DispatchEngine();
        var calm = await engine.EvaluateAsync(calmRequest);
        var urgent = await engine.EvaluateAsync(urgentRequest);

        Assert.False(calm.IsUrgent);
        Assert.True(urgent.IsUrgent);
        Assert.Equal(policy.MinSequenceWeight, calm.AppliedMinSequenceWeight);
        Assert.Equal(policy.MinSequenceWeight * policy.UrgentMinSequenceWeightFactor, urgent.AppliedMinSequenceWeight);
        Assert.Equal("B-0100", calm.Selected!.VehicleId);
        Assert.Equal("B-0110", urgent.Selected!.VehicleId);
        Assert.True(calm.Selected.SequenceNumber < urgent.Selected.SequenceNumber);
        Assert.True(urgent.Selected.FillRatio > calm.Selected.FillRatio);
        Assert.Equal(0.1m, calm.Selected.FillRatio);
        Assert.Equal(1m, urgent.Selected.FillRatio);
        Assert.Equal(policy.MinSequenceWeight, calm.Selected.SequenceComponent);
        Assert.Equal(policy.LoadBalanceWeight / 10m, calm.Selected.LoadBalanceComponent);
        Assert.Equal(0m, urgent.Selected.SequenceComponent);
        Assert.Equal(policy.LoadBalanceWeight, urgent.Selected.LoadBalanceComponent);
        Assert.Equal(8, urgent.QueuesEvaluated);
        Assert.Contains("紧急", urgent.Explanation);

        var urgentQueue = urgentRequest.Queues.Single(queue => queue.Id == urgent.Selected.QueueId);
        Assert.Equal(urgentQueue.Vehicles[0].Id, urgent.Selected.VehicleId);
        var buried = urgentRequest.Queues.SelectMany(queue => queue.Vehicles.Skip(1)).Select(vehicle => vehicle.Id).ToArray();
        Assert.All(buried, id => Assert.DoesNotContain(urgent.RankedCandidates, candidate => candidate.VehicleId == id));
    }

    [Fact]
    public async Task Equal_sequence_prefers_the_fuller_buffer()
    {
        var request = Shop.Request("balance", new[]
        {
            Shop.Lane("BUF-01", 10, Shop.Car("B-0001", 10)),
            Shop.Lane("BUF-02", 10, Enumerable.Range(0, 10).Select(i => Shop.Car("B-02" + i.ToString("00"), 10)).ToArray())
        });

        var decision = await new DispatchEngine().EvaluateAsync(request);

        Assert.Equal("BUF-02", decision.Selected!.QueueId);
        Assert.Equal(1m, decision.Selected.FillRatio);
        Assert.Equal(decision.Selected.SequenceNumber, decision.RankedCandidates.Single(candidate => candidate.Rank == 2).SequenceNumber);
    }

    [Fact]
    public async Task Equal_score_breaks_the_tie_by_queue_id()
    {
        var request = Shop.Request("tie", new[]
        {
            Shop.Lane("BUF-02", 10, Shop.Car("B-0002", 7)),
            Shop.Lane("BUF-01", 10, Shop.Car("B-0001", 7))
        });

        var decision = await new DispatchEngine().EvaluateAsync(request);

        Assert.Equal("BUF-01", decision.Selected!.QueueId);
        Assert.Equal(decision.RankedCandidates[0].Score, decision.RankedCandidates[1].Score);
    }

    [Fact]
    public async Task Blocked_queue_is_skipped_so_the_next_head_wins()
    {
        var request = Shop.Request("blocked-lane", new[]
        {
            Shop.BlockedLane("BUF-01", 10, Shop.Car("B-0001", 1)),
            Shop.Lane("BUF-02", 10, Shop.Car("B-0004", 4))
        });

        var decision = await new DispatchEngine().EvaluateAsync(request);

        Assert.Equal("B-0004", decision.Selected!.VehicleId);
        Assert.DoesNotContain(decision.RankedCandidates, candidate => candidate.QueueId == "BUF-01");
    }

    [Fact]
    public async Task Empty_shop_releases_nothing()
    {
        var decision = await new DispatchEngine().EvaluateAsync(Shop.Request("empty", Array.Empty<BufferQueue>()));

        Assert.False(decision.ShouldRelease);
        Assert.Equal(DispatchReason.NoEligibleCandidate, decision.Reason);
        Assert.Null(decision.Selected);
        Assert.Equal(AdvisorOutcome.NotConsulted, decision.AdvisorOutcome);
    }

    [Fact]
    public async Task Fast_lane_alone_can_release_when_every_buffer_is_empty()
    {
        var request = Shop.Request(
            "fast-only",
            new[] { Shop.Lane("BUF-01", 10), Shop.Lane("BUF-02", 10) },
            requestingExit: BoothExitId.A,
            exits: new[]
            {
                Shop.Exit(BoothExitId.A, Shop.Car("B-0003", 3, BoothExitId.A), fastLaneOpen: true),
                Shop.Exit(BoothExitId.B)
            });

        var decision = await new DispatchEngine().EvaluateAsync(request);

        Assert.Equal("B-0003", decision.Selected!.VehicleId);
        Assert.Equal(CandidateOrigin.FastLaneA, decision.Selected.Origin);
        Assert.Equal(2, decision.QueuesEvaluated);
    }

    [Fact]
    public async Task Duplicate_vehicle_id_is_rejected()
    {
        var request = Shop.Request("dup", new[]
        {
            Shop.Lane("BUF-01", 10, Shop.Car("B-0001", 1)),
            Shop.Lane("BUF-02", 10, Shop.Car("B-0001", 2))
        });

        var error = await Assert.ThrowsAsync<ArgumentException>(() => new DispatchEngine().EvaluateAsync(request));
        Assert.Contains("B-0001", error.Message);
    }

    [Fact]
    public async Task Null_request_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => new DispatchEngine().EvaluateAsync(null!));
    }
}
