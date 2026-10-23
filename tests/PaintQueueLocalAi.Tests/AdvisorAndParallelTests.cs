using PaintQueueLocalAi;
using PaintQueueLocalAi.Simulation;

namespace PaintQueueLocalAi.Tests;

public class AdvisorAndParallelTests
{
    [Fact]
    public async Task Null_advisor_declines_and_keeps_the_rule_winner()
    {
        var engine = new DispatchEngine(advisor: NullLlmAdvisor.Instance);
        var decision = await engine.EvaluateAsync(SampleSnapshots.MinSequence());

        Assert.Equal(AdvisorOutcome.Declined, decision.AdvisorOutcome);
        Assert.True(decision.FellBackToRules);
        Assert.False(decision.AdvisorApplied);
        Assert.Equal("B-0012", decision.Selected!.VehicleId);
        Assert.Contains("规则", decision.Explanation);
    }

    [Fact]
    public async Task Timeout_falls_back_to_the_rule_result()
    {
        var policy = DispatchPolicy.Default with { AdvisorTimeout = TimeSpan.FromMilliseconds(40) };
        var engine = new DispatchEngine(policy, new SlowAdvisor(TimeSpan.FromSeconds(3)));
        var decision = await engine.EvaluateAsync(SampleSnapshots.FastLane());

        Assert.Equal(AdvisorOutcome.TimedOut, decision.AdvisorOutcome);
        Assert.True(decision.FellBackToRules);
        Assert.Equal("B-0020", decision.Selected!.VehicleId);
        Assert.Equal(DispatchReason.PolicySelection, decision.Reason);
        Assert.Contains("超时", decision.Explanation);
    }

    [Fact]
    public async Task Fault_and_invalid_reorder_fall_back()
    {
        var faulted = await new DispatchEngine(advisor: new ThrowingAdvisor()).EvaluateAsync(SampleSnapshots.MinSequence());
        Assert.Equal(AdvisorOutcome.Faulted, faulted.AdvisorOutcome);
        Assert.Equal("B-0012", faulted.Selected!.VehicleId);
        Assert.Contains("model file missing", faulted.AdvisorExplanation);
        Assert.Contains("本地顾问故障，已回退到规则排序。", faulted.Explanation);
        Assert.DoesNotContain("ollama list", faulted.Explanation, StringComparison.Ordinal);

        var invalid = new FlagAdvisor
        {
            Reply = _ => new LlmAdvice { ReorderedCandidateIds = new[] { "not-a-candidate" }, Explanation = "坏建议" }
        };
        var rejected = await new DispatchEngine(advisor: invalid).EvaluateAsync(SampleSnapshots.MinSequence());
        Assert.Equal(AdvisorOutcome.InvalidReorder, rejected.AdvisorOutcome);
        Assert.True(rejected.FellBackToRules);
        Assert.Equal("B-0012", rejected.Selected!.VehicleId);
        Assert.Contains("坏建议", rejected.AdvisorExplanation);
    }

    [Fact]
    public async Task Valid_reorder_can_change_the_winner_inside_top_k()
    {
        var advisor = new FlagAdvisor
        {
            Reply = request => new LlmAdvice
            {
                ReorderedCandidateIds = request.TopCandidates.Select(candidate => candidate.CandidateId).Reverse().ToArray(),
                Explanation = "先疏解满库"
            }
        };
        var decision = await new DispatchEngine(advisor: advisor).EvaluateAsync(SampleSnapshots.Urgent(urgent: false));

        Assert.Equal(AdvisorOutcome.Applied, decision.AdvisorOutcome);
        Assert.True(decision.AdvisorReordered);
        Assert.Equal(DispatchReason.AdvisorRerank, decision.Reason);
        Assert.False(decision.FellBackToRules);
        Assert.Equal("B-0110", decision.Selected!.VehicleId);
        Assert.Contains("先疏解满库", decision.Explanation);
        Assert.Equal(1, advisor.Calls);
    }

    [Fact]
    public async Task Partial_reorder_keeps_the_rest_of_the_window()
    {
        var advisor = new FlagAdvisor
        {
            Reply = request => new LlmAdvice
            {
                ReorderedCandidateIds = new[] { request.TopCandidates[1].CandidateId }
            }
        };
        var decision = await new DispatchEngine(advisor: advisor).EvaluateAsync(SampleSnapshots.Urgent(urgent: false));

        Assert.Equal("B-0110", decision.RankedCandidates[0].VehicleId);
        Assert.Equal("B-0100", decision.RankedCandidates[1].VehicleId);
        Assert.Equal(1, decision.RankedCandidates[0].Rank);
        Assert.Equal(2, decision.RankedCandidates[1].Rank);
    }

    [Fact]
    public async Task Explanation_without_reorder_is_applied_and_does_not_change_the_winner()
    {
        var advisor = new FlagAdvisor
        {
            Reply = request => new LlmAdvice
            {
                Explanation = "同意规则"
            }
        };
        var decision = await new DispatchEngine(advisor: advisor).EvaluateAsync(SampleSnapshots.MinSequence());

        Assert.Equal(AdvisorOutcome.Applied, decision.AdvisorOutcome);
        Assert.False(decision.AdvisorReordered);
        Assert.Equal(DispatchReason.PolicySelection, decision.Reason);
        Assert.Equal("B-0012", decision.Selected!.VehicleId);
        Assert.Equal("同意规则", decision.AdvisorExplanation);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_swallowed_as_a_timeout()
    {
        var engine = new DispatchEngine(advisor: new SlowAdvisor(TimeSpan.FromSeconds(30)));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(40));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.EvaluateAsync(SampleSnapshots.MinSequence(), cts.Token));
    }

    [Fact]
    public async Task Engine_asks_the_scheduler_for_one_action_per_queue()
    {
        var scheduler = new RecordingScheduler();
        var engine = new DispatchEngine(queueScheduler: scheduler);
        var decision = await engine.EvaluateAsync(SampleSnapshots.MinSequence());

        Assert.Equal(8, scheduler.LastCount);
        Assert.Equal("B-0012", decision.Selected!.VehicleId);
    }

    [Fact]
    public async Task Scheduler_that_skips_a_queue_fails_the_decision()
    {
        var engine = new DispatchEngine(queueScheduler: new SkippingScheduler());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.EvaluateAsync(SampleSnapshots.FastLane()));
        Assert.Contains("BUF-01", error.Message);
    }

    [Fact]
    public async Task Default_scheduler_overlaps_queue_evaluation()
    {
        ThreadPool.GetMinThreads(out var workers, out var io);
        ThreadPool.GetMaxThreads(out var maxWorkers, out _);
        ThreadPool.SetMinThreads(Math.Min(Math.Max(workers, 8), maxWorkers), io);

        var scheduler = new ParallelQueueScheduler(maxDegreeOfParallelism: 4);
        using var barrier = new Barrier(4);
        var failures = new List<string>();
        var gate = new object();
        var work = Enumerable.Range(0, 4).Select<int, Func<CancellationToken, Task>>(_ => ct =>
        {
            if (!barrier.SignalAndWait(TimeSpan.FromSeconds(5)))
            {
                lock (gate)
                {
                    failures.Add("timeout");
                }
            }

            return Task.CompletedTask;
        }).ToArray();

        await scheduler.RunAsync(work, CancellationToken.None);
        Assert.Empty(failures);
    }

    [Fact]
    public async Task Concurrent_evaluations_agree_on_the_winner()
    {
        var engine = new DispatchEngine();
        var request = SampleSnapshots.MinSequence();
        var decisions = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => engine.EvaluateAsync(request)));

        Assert.All(decisions, decision =>
        {
            Assert.Equal("B-0012", decision.Selected!.VehicleId);
            Assert.Equal(decisions[0].RankedCandidates.Select(candidate => candidate.CandidateId), decision.RankedCandidates.Select(candidate => candidate.CandidateId));
        });
    }
}
