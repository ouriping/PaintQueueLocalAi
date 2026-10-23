namespace PaintQueueLocalAi;

/// <summary>
/// Deterministic dispatch engine for the paint-shop buffer bank.
/// Priority, highest first: safety interlocks, urgent exit request, fast lane A,
/// minimum sequence (weight reduced while urgent), then buffer load balance.
/// A single instance may evaluate many snapshots at once. Commit takes
/// <see cref="IReleaseGate"/> so two lanes cannot move the same conveyor together.
/// </summary>
public sealed class DispatchEngine : IDispatchEngine
{
    private readonly ILlmAdvisor _advisor;
    private readonly IReleaseGate _gate;
    private readonly IQueueEvaluationScheduler _scheduler;

    /// <summary>
    /// Create an engine. Null dependencies use the offline defaults:
    /// <see cref="DispatchPolicy.Default"/>, <see cref="NullLlmAdvisor"/>,
    /// a private <see cref="ResourceTokenReleaseGate"/>, and <see cref="ParallelQueueScheduler.Instance"/>.
    /// Share one gate across every engine that can move the same hardware.
    /// </summary>
    public DispatchEngine(
        DispatchPolicy? policy = null,
        ILlmAdvisor? advisor = null,
        IReleaseGate? releaseGate = null,
        IQueueEvaluationScheduler? queueScheduler = null)
    {
        Policy = policy ?? DispatchPolicy.Default;
        Policy.EnsureValid();
        _advisor = advisor ?? NullLlmAdvisor.Instance;
        _gate = releaseGate ?? new ResourceTokenReleaseGate();
        _scheduler = queueScheduler ?? ParallelQueueScheduler.Instance;
    }

    /// <inheritdoc />
    public DispatchPolicy Policy { get; }

    /// <inheritdoc />
    public async Task<DispatchDecision> EvaluateAsync(DispatchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        RequestValidator.Validate(request);

        var appliedWeight = request.IsUrgent
            ? Policy.MinSequenceWeight * Policy.UrgentMinSequenceWeightFactor
            : Policy.MinSequenceWeight;

        var hold = request.Safety.GetHold(request.SharedResourceId);
        if (hold != SafetyHoldKind.None)
        {
            return new DispatchDecision
            {
                RequestId = request.RequestId,
                ShouldRelease = false,
                Reason = DispatchReason.SafetyInterlock,
                Selected = null,
                RankedCandidates = Array.Empty<CandidateScore>(),
                Explanation = DecisionText.Safety(request, hold),
                IsUrgent = request.IsUrgent,
                AppliedMinSequenceWeight = appliedWeight,
                SafetyHold = hold,
                QueuesEvaluated = 0,
                AdvisorOutcome = AdvisorOutcome.NotConsulted,
                AdvisorReordered = false
            };
        }

        var observations = await ReadQueuesAsync(request, cancellationToken).ConfigureAwait(false);
        var emptyQueues = 0;
        var blockedQueues = 0;
        var eligible = new List<CandidateDraft>(observations.Length + 1);
        foreach (var observation in observations)
        {
            if (observation.Blocked)
            {
                blockedQueues++;
            }
            else if (observation.Empty)
            {
                emptyQueues++;
            }

            if (observation.Candidate is not null)
            {
                eligible.Add(observation.Candidate);
            }
        }

        var fastLane = TryFastLane(request);
        if (fastLane is not null)
        {
            eligible.Add(fastLane);
        }

        if (eligible.Count == 0)
        {
            return new DispatchDecision
            {
                RequestId = request.RequestId,
                ShouldRelease = false,
                Reason = DispatchReason.NoEligibleCandidate,
                Selected = null,
                RankedCandidates = Array.Empty<CandidateScore>(),
                Explanation = DecisionText.NoCandidate(request, emptyQueues, blockedQueues, appliedWeight),
                IsUrgent = request.IsUrgent,
                AppliedMinSequenceWeight = appliedWeight,
                SafetyHold = SafetyHoldKind.None,
                QueuesEvaluated = request.Queues.Count,
                AdvisorOutcome = AdvisorOutcome.NotConsulted,
                AdvisorReordered = false
            };
        }

        var ranked = Rank(eligible, appliedWeight);
        var ruleWinnerId = ranked[0].CandidateId;
        var consultation = await ConsultAsync(request, ranked, appliedWeight, cancellationToken).ConfigureAwait(false);
        var selected = consultation.Ranked[0];
        var winnerChanged = !string.Equals(ruleWinnerId, selected.CandidateId, StringComparison.Ordinal);
        var reason = consultation.Outcome == AdvisorOutcome.Applied && consultation.Reordered && winnerChanged
            ? DispatchReason.AdvisorRerank
            : DispatchReason.PolicySelection;

        return new DispatchDecision
        {
            RequestId = request.RequestId,
            ShouldRelease = true,
            Reason = reason,
            Selected = selected,
            RankedCandidates = consultation.Ranked,
            Explanation = DecisionText.Release(
                request,
                appliedWeight,
                consultation.Ranked,
                selected,
                ruleWinnerId,
                emptyQueues,
                blockedQueues,
                consultation.Outcome,
                consultation.Reordered,
                consultation.Explanation),
            IsUrgent = request.IsUrgent,
            AppliedMinSequenceWeight = appliedWeight,
            SafetyHold = SafetyHoldKind.None,
            QueuesEvaluated = request.Queues.Count,
            AdvisorOutcome = consultation.Outcome,
            AdvisorReordered = consultation.Reordered,
            AdvisorExplanation = consultation.Explanation
        };
    }

    /// <inheritdoc />
    public async Task<ReleaseResult> CommitReleaseAsync(
        DispatchRequest request,
        Func<DispatchDecision, IReleaseToken, CancellationToken, Task>? physicalRelease = null,
        CancellationToken cancellationToken = default)
    {
        var decision = await EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!decision.ShouldRelease || decision.Selected is null)
        {
            return new ReleaseResult
            {
                Decision = decision,
                Committed = false,
                ResourceId = request.SharedResourceId,
                LockedResourceIds = Array.Empty<string>(),
                TokenId = null
            };
        }

        var lockIds = LockIds(request, decision.Selected);
        await using var token = await _gate.AcquireManyAsync(lockIds, cancellationToken).ConfigureAwait(false);
        if (physicalRelease is not null)
        {
            await physicalRelease(decision, token, cancellationToken).ConfigureAwait(false);
        }

        return new ReleaseResult
        {
            Decision = decision,
            Committed = true,
            ResourceId = request.SharedResourceId,
            LockedResourceIds = token.ResourceIds,
            TokenId = token.TokenId
        };
    }

    private async Task<QueueObservation[]> ReadQueuesAsync(DispatchRequest request, CancellationToken cancellationToken)
    {
        var queues = request.Queues;
        var observations = new QueueObservation[queues.Count];
        var seen = new bool[queues.Count];
        var work = new Func<CancellationToken, Task>[queues.Count];
        for (var i = 0; i < queues.Count; i++)
        {
            var index = i;
            var queue = queues[i];
            work[i] = ct =>
            {
                ct.ThrowIfCancellationRequested();
                observations[index] = ReadQueue(queue, request.Safety);
                seen[index] = true;
                return Task.CompletedTask;
            };
        }

        await _scheduler.RunAsync(work, cancellationToken).ConfigureAwait(false);

        for (var i = 0; i < seen.Length; i++)
        {
            if (!seen[i])
            {
                throw new InvalidOperationException($"Queue scheduler did not evaluate '{queues[i].Id}'.");
            }
        }

        return observations;
    }

    private static QueueObservation ReadQueue(BufferQueue queue, SafetyInterlockState safety)
    {
        if (queue.ReleaseBlocked || safety.Blocks(queue.Id) || safety.Blocks(ReleaseResources.Queue(queue.Id)))
        {
            return new QueueObservation(null, empty: false, blocked: true);
        }

        if (queue.Vehicles.Count == 0)
        {
            return new QueueObservation(null, empty: true, blocked: false);
        }

        return new QueueObservation(CandidateDraft.FromBuffer(queue, queue.Vehicles[0]), empty: false, blocked: false);
    }

    private CandidateDraft? TryFastLane(DispatchRequest request)
    {
        if (!Policy.EnableFastLaneA)
        {
            return null;
        }

        BoothExit? exitA = null;
        foreach (var exit in request.Exits)
        {
            if (exit.Id == BoothExitId.A)
            {
                exitA = exit;
                break;
            }
        }

        if (exitA is null || !exitA.FastLaneOpen || exitA.ReleaseBlocked || exitA.PresentVehicle is null)
        {
            return null;
        }

        if (request.Safety.Blocks(ReleaseResources.ExitA))
        {
            return null;
        }

        return CandidateDraft.FromFastLane(exitA.PresentVehicle);
    }

    private CandidateScore[] Rank(IReadOnlyList<CandidateDraft> eligible, decimal appliedWeight)
    {
        var hasBuffer = false;
        var min = 0;
        var max = 0;
        foreach (var candidate in eligible)
        {
            if (candidate.Origin != CandidateOrigin.BufferHead)
            {
                continue;
            }

            if (!hasBuffer)
            {
                hasBuffer = true;
                min = candidate.SequenceNumber;
                max = candidate.SequenceNumber;
            }
            else
            {
                if (candidate.SequenceNumber < min)
                {
                    min = candidate.SequenceNumber;
                }

                if (candidate.SequenceNumber > max)
                {
                    max = candidate.SequenceNumber;
                }
            }
        }

        var scored = new List<CandidateScore>(eligible.Count);
        foreach (var candidate in eligible)
        {
            var closeness = hasBuffer ? Closeness(candidate.SequenceNumber, min, max) : 1m;
            var fill = candidate.Origin == CandidateOrigin.BufferHead ? Fill(candidate.Count, candidate.Capacity) : 0m;
            var sequenceComponent = appliedWeight * closeness;
            var loadComponent = Policy.LoadBalanceWeight * fill;
            scored.Add(new CandidateScore
            {
                CandidateId = candidate.CandidateId,
                VehicleId = candidate.VehicleId,
                SequenceNumber = candidate.SequenceNumber,
                Origin = candidate.Origin,
                QueueId = candidate.QueueId,
                SourceExit = candidate.SourceExit,
                PriorityTier = candidate.Origin == CandidateOrigin.FastLaneA ? 1 : 0,
                Score = sequenceComponent + loadComponent,
                SequenceComponent = sequenceComponent,
                LoadBalanceComponent = loadComponent,
                FillRatio = fill,
                Rank = 0,
                ColorCode = candidate.ColorCode
            });
        }

        scored.Sort(Compare);
        return AssignRanks(scored);
    }

    private async Task<AdvisorConsultation> ConsultAsync(
        DispatchRequest request,
        CandidateScore[] ranked,
        decimal appliedWeight,
        CancellationToken cancellationToken)
    {
        var window = Math.Min(Policy.AdvisorTopK, ranked.Length);
        var top = new CandidateScore[window];
        Array.Copy(ranked, top, window);
        var adviceRequest = new LlmAdviceRequest
        {
            Dispatch = request,
            TopCandidates = top,
            Policy = Policy,
            AppliedMinSequenceWeight = appliedWeight,
            RuleSummary = DecisionText.RuleSummary(request, top, appliedWeight)
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Policy.AdvisorTimeout);
        try
        {
            var advice = await _advisor.AdviseAsync(adviceRequest, timeout.Token).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (timeout.IsCancellationRequested)
            {
                return AdvisorConsultation.Fallback(ranked, AdvisorOutcome.TimedOut, null);
            }

            if (advice is null)
            {
                return AdvisorConsultation.Fallback(ranked, AdvisorOutcome.Declined, null);
            }

            var explanation = DecisionText.Cap(advice.Explanation);
            if (advice.ReorderedCandidateIds is null)
            {
                return AdvisorConsultation.Accept(ranked, reordered: false, explanation);
            }

            var rebuilt = TryReorder(ranked, advice.ReorderedCandidateIds, window);
            if (rebuilt is null)
            {
                return AdvisorConsultation.Fallback(ranked, AdvisorOutcome.InvalidReorder, explanation);
            }

            return AdvisorConsultation.Accept(rebuilt, reordered: !SameOrder(ranked, rebuilt), explanation);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AdvisorConsultation.Fallback(ranked, AdvisorOutcome.TimedOut, null);
        }
        catch (TimeoutException)
        {
            return AdvisorConsultation.Fallback(ranked, AdvisorOutcome.TimedOut, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return AdvisorConsultation.Fallback(ranked, AdvisorOutcome.Faulted, DecisionText.Cap(ex.Message));
        }
    }

    private static CandidateScore[]? TryReorder(CandidateScore[] ranked, IReadOnlyList<string> requestedOrder, int window)
    {
        if (requestedOrder is null || requestedOrder.Count == 0)
        {
            return null;
        }

        var byId = new Dictionary<string, CandidateScore>(window, StringComparer.Ordinal);
        for (var i = 0; i < window; i++)
        {
            byId.Add(ranked[i].CandidateId, ranked[i]);
        }

        var used = new HashSet<string>(StringComparer.Ordinal);
        var rebuilt = new List<CandidateScore>(ranked.Length);
        foreach (var id in requestedOrder)
        {
            if (string.IsNullOrWhiteSpace(id) || !byId.TryGetValue(id, out var candidate) || !used.Add(id))
            {
                return null;
            }

            rebuilt.Add(candidate);
        }

        for (var i = 0; i < window; i++)
        {
            if (!used.Contains(ranked[i].CandidateId))
            {
                rebuilt.Add(ranked[i]);
            }
        }

        for (var i = window; i < ranked.Length; i++)
        {
            rebuilt.Add(ranked[i]);
        }

        return AssignRanks(rebuilt);
    }

    private static bool SameOrder(CandidateScore[] left, CandidateScore[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (!string.Equals(left[i].CandidateId, right[i].CandidateId, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static CandidateScore[] AssignRanks(IReadOnlyList<CandidateScore> ordered)
    {
        var result = new CandidateScore[ordered.Count];
        for (var i = 0; i < ordered.Count; i++)
        {
            result[i] = ordered[i] with { Rank = i + 1 };
        }

        return result;
    }

    private static int Compare(CandidateScore left, CandidateScore right)
    {
        var tier = right.PriorityTier.CompareTo(left.PriorityTier);
        if (tier != 0)
        {
            return tier;
        }

        var score = right.Score.CompareTo(left.Score);
        if (score != 0)
        {
            return score;
        }

        var sequence = left.SequenceNumber.CompareTo(right.SequenceNumber);
        if (sequence != 0)
        {
            return sequence;
        }

        var queue = string.CompareOrdinal(left.QueueId, right.QueueId);
        if (queue != 0)
        {
            return queue;
        }

        return string.CompareOrdinal(left.VehicleId, right.VehicleId);
    }

    private static decimal Closeness(int sequence, int min, int max)
    {
        if (max == min)
        {
            return sequence == min ? 1m : 0m;
        }

        var value = (decimal)((long)max - sequence) / (decimal)((long)max - min);
        if (value < 0m)
        {
            return 0m;
        }

        if (value > 1m)
        {
            return 1m;
        }

        return value;
    }

    private static decimal Fill(int count, int capacity)
    {
        if (count <= 0)
        {
            return 0m;
        }

        if (capacity <= 0)
        {
            return 1m;
        }

        var ratio = (decimal)count / capacity;
        return ratio > 1m ? 1m : ratio;
    }

    private static string[] LockIds(DispatchRequest request, CandidateScore selected)
    {
        if (selected.Origin == CandidateOrigin.FastLaneA)
        {
            return new[] { request.SharedResourceId, ReleaseResources.ExitA };
        }

        if (string.IsNullOrEmpty(selected.QueueId))
        {
            return new[] { request.SharedResourceId };
        }

        return new[] { request.SharedResourceId, ReleaseResources.Queue(selected.QueueId) };
    }

    private readonly struct QueueObservation
    {
        public QueueObservation(CandidateDraft? candidate, bool empty, bool blocked)
        {
            Candidate = candidate;
            Empty = empty;
            Blocked = blocked;
        }

        public CandidateDraft? Candidate { get; }

        public bool Empty { get; }

        public bool Blocked { get; }
    }

    private sealed class CandidateDraft
    {
        private CandidateDraft()
        {
        }

        public required string CandidateId { get; init; }

        public required string VehicleId { get; init; }

        public required int SequenceNumber { get; init; }

        public required CandidateOrigin Origin { get; init; }

        public string? QueueId { get; init; }

        public required BoothExitId SourceExit { get; init; }

        public required int Count { get; init; }

        public required int Capacity { get; init; }

        public string? ColorCode { get; init; }

        public static CandidateDraft FromBuffer(BufferQueue queue, Vehicle head) => new()
        {
            CandidateId = "buffer:" + queue.Id + ":" + head.Id,
            VehicleId = head.Id,
            SequenceNumber = head.SequenceNumber,
            Origin = CandidateOrigin.BufferHead,
            QueueId = queue.Id,
            SourceExit = head.SourceExit,
            Count = queue.Vehicles.Count,
            Capacity = queue.Capacity,
            ColorCode = head.ColorCode
        };

        public static CandidateDraft FromFastLane(Vehicle vehicle) => new()
        {
            CandidateId = "fast-lane-a:" + vehicle.Id,
            VehicleId = vehicle.Id,
            SequenceNumber = vehicle.SequenceNumber,
            Origin = CandidateOrigin.FastLaneA,
            QueueId = null,
            SourceExit = BoothExitId.A,
            Count = 0,
            Capacity = 0,
            ColorCode = vehicle.ColorCode
        };
    }

    private readonly struct AdvisorConsultation
    {
        public required CandidateScore[] Ranked { get; init; }

        public required AdvisorOutcome Outcome { get; init; }

        public required bool Reordered { get; init; }

        public string? Explanation { get; init; }

        public static AdvisorConsultation Fallback(CandidateScore[] ranked, AdvisorOutcome outcome, string? explanation)
            => new()
            {
                Ranked = ranked,
                Outcome = outcome,
                Reordered = false,
                Explanation = explanation
            };

        public static AdvisorConsultation Accept(CandidateScore[] ranked, bool reordered, string? explanation)
            => new()
            {
                Ranked = ranked,
                Outcome = AdvisorOutcome.Applied,
                Reordered = reordered,
                Explanation = explanation
            };
    }
}
