namespace PaintQueueLocalAi;

/// <summary>引擎展示给顾问的内容。安全保持永远不会提交给顾问。</summary>
public sealed record LlmAdviceRequest
{
    /// <summary>规则已经评过分的车间快照。</summary>
    public required DispatchRequest Dispatch { get; init; }

    /// <summary>规则顺序，已裁剪到 <see cref="DispatchPolicy.AdvisorTopK"/>。</summary>
    public required IReadOnlyList<CandidateScore> TopCandidates { get; init; }

    /// <summary>产生 <see cref="TopCandidates"/> 的策略。</summary>
    public required DispatchPolicy Policy { get; init; }

    /// <summary>本次请求使用的序号权重，已计入紧急时的降低。</summary>
    public required decimal AppliedMinSequenceWeight { get; init; }

    /// <summary>规则排序的简短中文摘要，适合作为提示词上下文。</summary>
    public required string RuleSummary { get; init; }
}
