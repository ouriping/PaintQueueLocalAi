namespace PaintQueueLocalAi;

public sealed record QueuePreview(
    InterlockKind? BlockingInterlock,
    IReadOnlyList<ExcludedJob> Excluded,
    IReadOnlyList<RankedCandidate> Ranked);

public sealed class DispatchDecision
{
    private DispatchDecision(
        InterlockKind? blockingInterlock,
        string? selectedJobId,
        IReadOnlyList<RankedCandidate> ruleOrder,
        IReadOnlyList<RankedCandidate> finalOrder,
        IReadOnlyList<ExcludedJob> excluded,
        bool advisorInvoked,
        bool advisorApplied,
        string? advisorExplanation,
        string? fallbackReason,
        string hmiText)
    {
        BlockingInterlock = blockingInterlock;
        SelectedJobId = selectedJobId;
        RuleOrder = ruleOrder;
        FinalOrder = finalOrder;
        Excluded = excluded;
        AdvisorInvoked = advisorInvoked;
        AdvisorApplied = advisorApplied;
        AdvisorExplanation = advisorExplanation;
        FallbackReason = fallbackReason;
        HmiText = hmiText;
    }

    public InterlockKind? BlockingInterlock { get; }

    public bool Blocked => BlockingInterlock is not null;

    public string? SelectedJobId { get; }

    public IReadOnlyList<RankedCandidate> RuleOrder { get; }

    public IReadOnlyList<RankedCandidate> FinalOrder { get; }

    public IReadOnlyList<ExcludedJob> Excluded { get; }

    public bool AdvisorInvoked { get; }

    public bool AdvisorApplied { get; }

    public string? AdvisorExplanation { get; }

    public string? FallbackReason { get; }

    public string HmiText { get; }

    public static DispatchDecision FromInterlock(InterlockKind kind, IReadOnlyList<ExcludedJob> excluded)
    {
        var text = $"联锁：{InterlockText.ToChinese(kind)}。本轮不调度，未询问模型。";
        return new DispatchDecision(kind, null, Array.Empty<RankedCandidate>(), Array.Empty<RankedCandidate>(), excluded, false, false, null, null, text);
    }

    public static DispatchDecision NoWork(IReadOnlyList<ExcludedJob> excluded)
    {
        return new DispatchDecision(null, null, Array.Empty<RankedCandidate>(), Array.Empty<RankedCandidate>(), excluded, false, false, null, null, "没有可调度工单。");
    }

    public static DispatchDecision Ready(
        IReadOnlyList<RankedCandidate> ruleOrder,
        IReadOnlyList<RankedCandidate> finalOrder,
        IReadOnlyList<ExcludedJob> excluded,
        bool advisorInvoked,
        bool advisorApplied,
        string? advisorExplanation,
        string? fallbackReason)
    {
        var selected = finalOrder.Count == 0 ? null : finalOrder[0].CandidateId;
        var text = HmiFormatter.Format(finalOrder, advisorApplied, advisorExplanation, fallbackReason);
        return new DispatchDecision(null, selected, ruleOrder, finalOrder, excluded, advisorInvoked, advisorApplied, advisorExplanation, fallbackReason, text);
    }
}

public static class HmiFormatter
{
    public static string Format(
        IReadOnlyList<RankedCandidate> finalOrder,
        bool advisorApplied,
        string? advisorExplanation,
        string? fallbackReason)
    {
        if (finalOrder.Count == 0)
        {
            return "没有可调度工单。";
        }

        var top = finalOrder[0];
        var head = advisorApplied
            ? $"顾问建议 {top.CandidateId}（{top.Job.ColorName}）"
            : $"规则建议 {top.CandidateId}（{top.Job.ColorName}）";
        var detail = advisorApplied ? advisorExplanation : AdvisorPrompt.Explain(top);
        var text = string.IsNullOrEmpty(detail) ? head + "。" : head + "。" + detail;
        if (!advisorApplied
            && fallbackReason is not null
            && !string.Equals(fallbackReason, AdvisorFallbackReason.NoSuggestion, StringComparison.Ordinal))
        {
            text += "（顾问未采用）";
        }

        return text;
    }
}

/// <summary>
/// 规则优先调度。模型只允许重排 TopCandidates 或提供界面说明。
/// 联锁、空队列、超时、异常和非法输出都不会改变规则给出的候选集合。
/// </summary>
public sealed class DispatchEngine
{
    private readonly DispatchOptions _options;
    private readonly ILlmAdvisor _advisor;

    public DispatchEngine(DispatchOptions? options = null, ILlmAdvisor? advisor = null)
    {
        _options = options ?? new DispatchOptions();
        _options.EnsureValid();
        _advisor = advisor ?? NullLlmAdvisor.Instance;
    }

    public DispatchOptions Options => _options;

    public QueuePreview Preview(BoothState booth, IReadOnlyList<PaintJob> queue)
    {
        ArgumentNullException.ThrowIfNull(booth);
        ArgumentNullException.ThrowIfNull(queue);
        EnsureIds(queue);

        var excluded = new List<ExcludedJob>();
        var eligible = new List<PaintJob>(queue.Count);
        foreach (var job in queue)
        {
            if (!SafetyInterlocks.TryAccept(job, booth, out var reason))
            {
                excluded.Add(new ExcludedJob(job.JobId, reason));
                continue;
            }

            eligible.Add(job);
        }

        var blocking = SafetyInterlocks.BoothBlock(booth);
        var ranked = blocking is null ? RuleScorer.Rank(booth, eligible) : Array.Empty<RankedCandidate>();
        return new QueuePreview(blocking, excluded, ranked);
    }

    public async ValueTask<DispatchDecision> DispatchAsync(
        BoothState booth,
        IReadOnlyList<PaintJob> queue,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var preview = Preview(booth, queue);
        if (preview.BlockingInterlock is { } kind)
        {
            return DispatchDecision.FromInterlock(kind, preview.Excluded);
        }

        var top = preview.Ranked.Take(_options.TopK).ToArray();
        if (top.Length == 0)
        {
            return DispatchDecision.NoWork(preview.Excluded);
        }

        AdvisorSuggestion? suggestion = null;
        string? fallback = null;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.AdvisorTimeout);
        try
        {
            var context = new AdvisorContext(booth, top, _options.AdvisorTimeout);
            suggestion = await _advisor.AdviseAsync(context, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            fallback = AdvisorFallbackReason.Timeout;
        }
        catch (FormatException)
        {
            fallback = AdvisorFallbackReason.Invalid;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            fallback = AdvisorFallbackReason.ExceptionPrefix + ex.GetType().Name;
        }

        if (fallback is null && suggestion is null)
        {
            fallback = AdvisorFallbackReason.NoSuggestion;
        }

        if (fallback is null && suggestion is not null)
        {
            if (!AdvisorOrder.TryApply(suggestion.OrderedCandidateIds, top, out var reordered))
            {
                return DispatchDecision.Ready(top, top, preview.Excluded, true, false, null, AdvisorFallbackReason.Invalid);
            }

            var explanation = AdvisorText.SanitizeExplanation(suggestion.Explanation);
            return DispatchDecision.Ready(top, reordered, preview.Excluded, true, true, explanation, null);
        }

        return DispatchDecision.Ready(top, top, preview.Excluded, true, false, null, fallback);
    }

    private static void EnsureIds(IReadOnlyList<PaintJob> queue)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var job in queue)
        {
            if (job is null)
            {
                throw new ArgumentException("队列含空工单。", nameof(queue));
            }

            if (string.IsNullOrWhiteSpace(job.JobId))
            {
                throw new ArgumentException("工单号不能为空。", nameof(queue));
            }

            if (!seen.Add(job.JobId))
            {
                throw new ArgumentException("工单号重复：" + job.JobId, nameof(queue));
            }
        }
    }
}
