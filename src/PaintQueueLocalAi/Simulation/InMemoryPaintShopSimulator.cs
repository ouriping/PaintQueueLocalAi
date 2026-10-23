using System.Text;
using PaintQueueLocalAi;

namespace PaintQueueLocalAi.Simulation;

/// <summary>
/// 四段车间故事的内存验收：最小序号、A 口快车道、
/// 紧急降权，以及两条车道经同一个放行闸放行。
/// 仿真器始终用 <see cref="DispatchPolicy.Default"/> 自建引擎，且不加载本地模型。
/// </summary>
public sealed class InMemoryPaintShopSimulator
{
    private readonly DispatchEngine _engine = new();

    /// <summary>按顺序运行这四个场景。</summary>
    public async Task<SimulationReport> RunAllAsync(CancellationToken cancellationToken = default)
    {
        var scenarios = new[]
        {
            await RunMinSequenceAsync(cancellationToken).ConfigureAwait(false),
            await RunFastLaneAsync(cancellationToken).ConfigureAwait(false),
            await RunUrgentAsync(cancellationToken).ConfigureAwait(false),
            await RunContendedGateAsync(cancellationToken).ConfigureAwait(false)
        };
        return new SimulationReport { Scenarios = scenarios };
    }

    private async Task<ScenarioRun> RunMinSequenceAsync(CancellationToken cancellationToken)
    {
        var request = SampleSnapshots.MinSequence();
        var decision = await _engine.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
        var heads = EligibleHeads(request);
        var smallest = heads.Min(vehicle => vehicle.SequenceNumber);
        var buried = request.Queues.SelectMany(queue => queue.Vehicles.Skip(1)).Select(vehicle => vehicle.Id).ToArray();
        var exitVehicleId = request.Exits.Single(exit => exit.Id == BoothExitId.B).PresentVehicle?.Id;
        var passed = decision.ShouldRelease
            && decision.Reason == DispatchReason.PolicySelection
            && decision.Selected is not null
            && decision.Selected.Origin == CandidateOrigin.BufferHead
            && decision.Selected.SequenceNumber == smallest
            && decision.Selected.VehicleId == heads.Single(vehicle => vehicle.SequenceNumber == smallest).Id
            && decision.QueuesEvaluated == 8
            && !decision.IsUrgent
            && decision.FellBackToRules
            && buried.All(id => decision.RankedCandidates.All(candidate => candidate.VehicleId != id))
            && (exitVehicleId is null || decision.RankedCandidates.All(candidate => candidate.VehicleId != exitVehicleId));

        var detail = new StringBuilder();
        detail.AppendLine("期望：8 条缓存里只比较车头，放行序号最小的车头。埋在队列里的更小序号，以及 B 口在位车辆，都不能直放。");
        detail.AppendLine(passed ? "结果符合期望。" : "结果不符合期望。");
        detail.Append(decision.Explanation);
        return new ScenarioRun
        {
            Key = "min-sequence",
            Title = "正常放行：最小序号优先",
            Passed = passed,
            Detail = detail.ToString(),
            Decisions = new[] { decision }
        };
    }

    private async Task<ScenarioRun> RunFastLaneAsync(CancellationToken cancellationToken)
    {
        var request = SampleSnapshots.FastLane();
        var decision = await _engine.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
        var buffers = decision.RankedCandidates.Where(candidate => candidate.Origin == CandidateOrigin.BufferHead).ToArray();
        var bestBuffer = buffers.OrderBy(candidate => candidate.SequenceNumber).FirstOrDefault();
        var passed = decision.ShouldRelease
            && decision.Selected is { Origin: CandidateOrigin.FastLaneA, VehicleId: "B-0020" }
            && bestBuffer is not null
            && bestBuffer.SequenceNumber < decision.Selected.SequenceNumber
            && bestBuffer.Score > decision.Selected.Score
            && decision.Selected.Rank < bestBuffer.Rank
            && decision.Reason == DispatchReason.PolicySelection;

        var detail = new StringBuilder();
        detail.AppendLine("期望：A 口快车道开着时，直放车辆优先于缓存里序号更小的车头。");
        detail.AppendLine(passed ? "结果符合期望。" : "结果不符合期望。");
        detail.Append(decision.Explanation);
        return new ScenarioRun
        {
            Key = "fast-lane-a",
            Title = "A 口快车道：直放优先于更小序号",
            Passed = passed,
            Detail = detail.ToString(),
            Decisions = new[] { decision }
        };
    }

    private async Task<ScenarioRun> RunUrgentAsync(CancellationToken cancellationToken)
    {
        var calmRequest = SampleSnapshots.Urgent(urgent: false);
        var urgentRequest = SampleSnapshots.Urgent(urgent: true);
        var calm = await _engine.EvaluateAsync(calmRequest, cancellationToken).ConfigureAwait(false);
        var urgent = await _engine.EvaluateAsync(urgentRequest, cancellationToken).ConfigureAwait(false);
        var policy = _engine.Policy;
        var passed = calm.ShouldRelease && urgent.ShouldRelease
            && calm.Selected is not null && urgent.Selected is not null
            && calm.AppliedMinSequenceWeight == policy.MinSequenceWeight
            && urgent.AppliedMinSequenceWeight == policy.MinSequenceWeight * policy.UrgentMinSequenceWeightFactor
            && calm.Selected.SequenceNumber < urgent.Selected.SequenceNumber
            && urgent.Selected.FillRatio > calm.Selected.FillRatio
            && urgent.IsUrgent && !calm.IsUrgent
            && calm.Selected.VehicleId == "B-0100"
            && urgent.Selected.VehicleId == "B-0110"
            && calm.QueuesEvaluated == 8
            && urgent.QueuesEvaluated == 8;

        var detail = new StringBuilder();
        detail.AppendLine("期望：非紧急时放行序号 100；紧急要车把最小序号权重降下来后，改放满库的序号 110。");
        detail.AppendLine(passed ? "结果符合期望。" : "结果不符合期望。");
        detail.AppendLine("非紧急：");
        detail.AppendLine(calm.Explanation.TrimEnd());
        detail.AppendLine("紧急：");
        detail.Append(urgent.Explanation);
        return new ScenarioRun
        {
            Key = "urgent-weight",
            Title = "紧急要车：降低最小序号权重",
            Passed = passed,
            Detail = detail.ToString(),
            Decisions = new[] { calm, urgent }
        };
    }

    private async Task<ScenarioRun> RunContendedGateAsync(CancellationToken cancellationToken)
    {
        var pair = SampleSnapshots.ContendedPair();
        var current = 0;
        var max = 0;

        async Task<ReleaseResult> ReleaseAsync(DispatchRequest request)
        {
            return await _engine.CommitReleaseAsync(request, async (_, _, ct) =>
            {
                var now = Interlocked.Increment(ref current);
                ObserveMax(ref max, now);
                try
                {
                    await Task.Delay(100, ct).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Decrement(ref current);
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        var results = await Task.WhenAll(
            ReleaseAsync(pair.Left),
            ReleaseAsync(pair.Right)).ConfigureAwait(false);
        var left = results[0];
        var right = results[1];
        var passed = left.Committed && right.Committed
            && max == 1
            && left.Decision.Selected?.VehicleId == "B-0001"
            && right.Decision.Selected?.VehicleId == "B-0002"
            && left.LockedResourceIds.Contains(SampleSnapshots.Merge)
            && right.LockedResourceIds.Contains(SampleSnapshots.Merge)
            && left.LockedResourceIds.Contains(ReleaseResources.Queue("BUF-01"))
            && right.LockedResourceIds.Contains(ReleaseResources.Queue("BUF-02"));

        var detail = new StringBuilder();
        detail.AppendLine("期望：两条队列同时评估并提交到 MERGE-01，物理放行回调的最大并发为 1，且各自锁住自己的车道。");
        detail.Append("观测到的最大并发：").Append(max).AppendLine(passed ? "。结果符合期望。" : "。结果不符合期望。");
        detail.Append("左侧：").AppendLine(left.Decision.Explanation.TrimEnd());
        detail.Append("右侧：").Append(right.Decision.Explanation);
        return new ScenarioRun
        {
            Key = "contended-gate",
            Title = "双队列争用同一放行闸",
            Passed = passed,
            Detail = detail.ToString(),
            Decisions = new[] { left.Decision, right.Decision },
            MaxReleaseConcurrency = max
        };
    }

    private static IEnumerable<Vehicle> EligibleHeads(DispatchRequest request)
    {
        foreach (var queue in request.Queues)
        {
            if (!queue.ReleaseBlocked && queue.Vehicles.Count > 0)
            {
                yield return queue.Vehicles[0];
            }
        }
    }

    private static void ObserveMax(ref int location, int value)
    {
        int initial;
        do
        {
            initial = Volatile.Read(ref location);
            if (value <= initial)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref location, value, initial) != initial);
    }
}
