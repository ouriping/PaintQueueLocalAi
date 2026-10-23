using PaintQueueLocalAi;

namespace PaintQueueLocalAi.Tests;

public class RuleAndDispatchTests
{
    [Fact]
    public void DefaultTimeout_IsTwoHundredMilliseconds()
    {
        var options = new DispatchOptions();
        Assert.Equal(TimeSpan.FromMilliseconds(200), options.AdvisorTimeout);
        Assert.Equal(5, options.TopK);
    }

    [Fact]
    public void MorningShift_RanksSameColorUrgentFirst()
    {
        var sample = SampleQueues.MorningShift();
        var preview = new DispatchEngine().Preview(sample.Booth, sample.Jobs);
        var ids = preview.Ranked.Select(item => item.CandidateId).ToArray();
        Assert.Equal(new[] { "W-URG", "K-LATE", "R-HIGH", "W-NORM" }, ids);
        Assert.Equal(840, preview.Ranked[0].Score);
        Assert.Equal(390, preview.Ranked[1].Score);
        Assert.Equal(380, preview.Ranked[2].Score);
        Assert.Equal(220, preview.Ranked[3].Score);
        Assert.Contains(preview.Excluded, item => item.JobId == "W-HOLD" && item.Reason == "质量保留");
        Assert.Contains(preview.Excluded, item => item.JobId == "S-CHEM" && item.Reason == "涂料体系不一致");
        Assert.Contains(preview.Excluded, item => item.JobId == "W-FLASH" && item.Reason == "闪干未完成");
        Assert.Contains(preview.Excluded, item => item.JobId == "W-HOT" && item.Reason == "烘房温度不足");
    }

    [Fact]
    public void Scorer_BreaksTiesByDueThenJobId()
    {
        var now = SampleQueues.MorningNow;
        var booth = new BoothState("B1", "RAL9016", null, PaintChemistry.Waterborne, false, true, true, true, 180, now);
        var later = new PaintJob("J2", "RAL9016", "白", PaintChemistry.Waterborne, JobPriority.Normal, now.AddHours(5), "门板", 1, false, true, 160);
        var earlier = new PaintJob("J1", "RAL9016", "白", PaintChemistry.Waterborne, JobPriority.Normal, now.AddHours(4), "门板", 1, false, true, 160);
        var sameDueB = new PaintJob("Jb", "RAL9005", "黑", PaintChemistry.Waterborne, JobPriority.Normal, now.AddHours(8), "翼子板", 1, false, true, 160);
        var sameDueA = new PaintJob("Ja", "RAL9005", "黑", PaintChemistry.Waterborne, JobPriority.Normal, now.AddHours(8), "翼子板", 1, false, true, 160);
        var ranked = RuleScorer.Rank(booth, new[] { later, sameDueB, earlier, sameDueA });
        Assert.Equal(new[] { "J1", "J2", "Ja", "Jb" }, ranked.Select(item => item.CandidateId).ToArray());
        Assert.True(ranked[0].Score > ranked[2].Score);
    }

    [Theory]
    [InlineData(nameof(InterlockKind.EmergencyStop))]
    [InlineData(nameof(InterlockKind.ConveyorStopped))]
    [InlineData(nameof(InterlockKind.BoothFault))]
    [InlineData(nameof(InterlockKind.OvenNotReady))]
    public async Task BoothInterlock_DoesNotCallAdvisor(string kindName)
    {
        var sample = SampleQueues.MorningShift();
        var booth = kindName switch
        {
            nameof(InterlockKind.EmergencyStop) => sample.Booth with { EmergencyStop = true },
            nameof(InterlockKind.ConveyorStopped) => sample.Booth with { ConveyorRunning = false },
            nameof(InterlockKind.BoothFault) => sample.Booth with { BoothHealthy = false },
            _ => sample.Booth with { OvenReady = false }
        };
        var spy = new SpyAdvisor();
        var decision = await new DispatchEngine(advisor: spy).DispatchAsync(booth, sample.Jobs);
        Assert.Equal(0, spy.Calls);
        Assert.True(decision.Blocked);
        Assert.False(decision.AdvisorInvoked);
        Assert.Null(decision.SelectedJobId);
        Assert.Contains("未询问模型", decision.HmiText);
    }

    [Fact]
    public async Task NullAdvisor_KeepsRuleOrder()
    {
        var sample = SampleQueues.MorningShift();
        var decision = await new DispatchEngine(advisor: null).DispatchAsync(sample.Booth, sample.Jobs);
        Assert.Equal(SampleQueues.MorningWinnerId, decision.SelectedJobId);
        Assert.True(decision.AdvisorInvoked);
        Assert.False(decision.AdvisorApplied);
        Assert.Equal(AdvisorFallbackReason.NoSuggestion, decision.FallbackReason);
        Assert.Equal(decision.RuleOrder.Select(item => item.CandidateId), decision.FinalOrder.Select(item => item.CandidateId));
        Assert.StartsWith("规则建议 W-URG", decision.HmiText);
        Assert.DoesNotContain("顾问未采用", decision.HmiText);
    }

    [Fact]
    public async Task AdvisorReorder_IsLimitedToExistingCandidates()
    {
        var sample = SampleQueues.MorningShift();
        var spy = new SpyAdvisor((context, _) =>
        {
            Assert.DoesNotContain(context.TopCandidates, item => item.CandidateId is "W-HOLD" or "S-CHEM" or "W-FLASH" or "W-HOT");
            return ValueTask.FromResult<AdvisorSuggestion?>(new AdvisorSuggestion(
                new[] { "R-HIGH", "W-URG", "K-LATE", "W-NORM" },
                "高优先红件提前\n第二行" + new string('啊', 100)));
        });
        var decision = await new DispatchEngine(advisor: spy).DispatchAsync(sample.Booth, sample.Jobs);
        Assert.Equal(1, spy.Calls);
        Assert.True(decision.AdvisorApplied);
        Assert.Equal(new[] { "R-HIGH", "W-URG", "K-LATE", "W-NORM" }, decision.FinalOrder.Select(item => item.CandidateId).ToArray());
        Assert.Equal("R-HIGH", decision.SelectedJobId);
        Assert.DoesNotContain("\n", decision.AdvisorExplanation);
        Assert.True(decision.AdvisorExplanation!.Length <= AdvisorLimits.MaxExplanationChars);
        Assert.Contains("顾问建议 R-HIGH", decision.HmiText);
    }

    [Fact]
    public async Task PartialOrder_AppendsMissingIdsInRuleOrder()
    {
        var sample = SampleQueues.MorningShift();
        var spy = new SpyAdvisor((_, _) => ValueTask.FromResult<AdvisorSuggestion?>(
            new AdvisorSuggestion(new[] { "NOPE", "W-NORM", "W-NORM" }, "只点了后段")));
        var decision = await new DispatchEngine(advisor: spy).DispatchAsync(sample.Booth, sample.Jobs);
        Assert.True(decision.AdvisorApplied);
        Assert.Equal(new[] { "W-NORM", "W-URG", "K-LATE", "R-HIGH" }, decision.FinalOrder.Select(item => item.CandidateId).ToArray());
    }

    [Fact]
    public async Task UnknownIds_KeepRuleOrder()
    {
        var sample = SampleQueues.MorningShift();
        var spy = new SpyAdvisor((_, _) => ValueTask.FromResult<AdvisorSuggestion?>(
            new AdvisorSuggestion(new[] { "CLOUD-1" }, "发明工单")));
        var decision = await new DispatchEngine(advisor: spy).DispatchAsync(sample.Booth, sample.Jobs);
        Assert.False(decision.AdvisorApplied);
        Assert.Equal(AdvisorFallbackReason.Invalid, decision.FallbackReason);
        Assert.Equal("W-URG", decision.SelectedJobId);
        Assert.Contains("顾问未采用", decision.HmiText);
    }

    [Fact]
    public async Task Timeout_KeepsRuleOrderQuickly()
    {
        var sample = SampleQueues.MorningShift();
        var spy = new SpyAdvisor(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            return new AdvisorSuggestion(new[] { "R-HIGH" }, "太慢");
        });
        var engine = new DispatchEngine(new DispatchOptions { AdvisorTimeout = TimeSpan.FromMilliseconds(50) }, spy);
        var started = System.Diagnostics.Stopwatch.StartNew();
        var decision = await engine.DispatchAsync(sample.Booth, sample.Jobs);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(2));
        Assert.Equal(AdvisorFallbackReason.Timeout, decision.FallbackReason);
        Assert.Equal("W-URG", decision.SelectedJobId);
        Assert.False(decision.AdvisorApplied);
    }

    [Fact]
    public async Task InvalidJson_FromAdvisorException_KeepsRuleOrder()
    {
        var sample = SampleQueues.MorningShift();
        var spy = new SpyAdvisor((_, _) => throw new FormatException("order"));
        var decision = await new DispatchEngine(advisor: spy).DispatchAsync(sample.Booth, sample.Jobs);
        Assert.Equal(AdvisorFallbackReason.Invalid, decision.FallbackReason);
        Assert.Equal("W-URG", decision.SelectedJobId);
    }

    [Fact]
    public async Task AdvisorException_DoesNotLeakMessageIntoSelection()
    {
        var sample = SampleQueues.MorningShift();
        var spy = new SpyAdvisor((_, _) => throw new InvalidOperationException("boom-secret"));
        var decision = await new DispatchEngine(advisor: spy).DispatchAsync(sample.Booth, sample.Jobs);
        Assert.Equal("exception:InvalidOperationException", decision.FallbackReason);
        Assert.Equal("W-URG", decision.SelectedJobId);
        Assert.DoesNotContain("boom-secret", decision.HmiText);
    }

    [Fact]
    public async Task EmptyEligible_DoesNotCallAdvisor()
    {
        var sample = SampleQueues.MorningShift();
        var held = sample.Jobs.Select(job => job with { QualityHold = true }).ToArray();
        var spy = new SpyAdvisor();
        var decision = await new DispatchEngine(advisor: spy).DispatchAsync(sample.Booth, held);
        Assert.Equal(0, spy.Calls);
        Assert.Null(decision.SelectedJobId);
        Assert.Equal("没有可调度工单。", decision.HmiText);
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        var sample = SampleQueues.MorningShift();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var spy = new SpyAdvisor();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DispatchEngine(advisor: spy).DispatchAsync(sample.Booth, sample.Jobs, cts.Token).AsTask());
        Assert.Equal(0, spy.Calls);
    }

    [Fact]
    public void DuplicateJobId_IsRejected()
    {
        var sample = SampleQueues.MorningShift();
        var jobs = sample.Jobs.ToArray();
        jobs[1] = jobs[1] with { JobId = jobs[0].JobId };
        Assert.Throws<ArgumentException>(() => new DispatchEngine().Preview(sample.Booth, jobs));
    }

    [Fact]
    public void TopK_DropsLowerRanksFromAdvisorSet()
    {
        var sample = SampleQueues.MorningShift();
        var preview = new DispatchEngine(new DispatchOptions { TopK = 1 }).Preview(sample.Booth, sample.Jobs);
        Assert.Equal(4, preview.Ranked.Count);
    }

    [Fact]
    public async Task TopKOne_OnlyExposesTheWinner()
    {
        var sample = SampleQueues.MorningShift();
        AdvisorContext? seen = null;
        var spy = new SpyAdvisor((context, _) =>
        {
            seen = context;
            return ValueTask.FromResult<AdvisorSuggestion?>(null);
        });
        await new DispatchEngine(new DispatchOptions { TopK = 1 }, spy).DispatchAsync(sample.Booth, sample.Jobs);
        Assert.NotNull(seen);
        Assert.Equal(new[] { "W-URG" }, seen!.TopCandidates.Select(item => item.CandidateId).ToArray());
    }

    private sealed class SpyAdvisor : ILlmAdvisor
    {
        private readonly Func<AdvisorContext, CancellationToken, ValueTask<AdvisorSuggestion?>>? _next;

        public SpyAdvisor(Func<AdvisorContext, CancellationToken, ValueTask<AdvisorSuggestion?>>? next = null)
        {
            _next = next;
        }

        public int Calls { get; private set; }

        public ValueTask<AdvisorSuggestion?> AdviseAsync(AdvisorContext context, CancellationToken cancellationToken)
        {
            Calls++;
            if (_next is null)
            {
                return ValueTask.FromResult<AdvisorSuggestion?>(null);
            }

            return _next(context, cancellationToken);
        }
    }
}
