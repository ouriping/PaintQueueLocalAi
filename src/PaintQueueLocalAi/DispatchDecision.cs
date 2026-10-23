namespace PaintQueueLocalAi;

/// <summary>引擎放行或未放行的原因。</summary>
public enum DispatchReason
{
    /// <summary>全局联锁禁止动作。未对任何候选评分。</summary>
    SafetyInterlock = 0,

    /// <summary>联锁已解除，但没有缓存车头或快车道车身可以移动。</summary>
    NoEligibleCandidate = 1,

    /// <summary>由确定性排序选出车身。顾问缺席、同意，或被拒绝。</summary>
    PolicySelection = 2,

    /// <summary>已接受的顾问重排改变了名次 1 的车身。</summary>
    AdvisorRerank = 3
}

/// <summary>可选的本地顾问查看前列候选后的结果。</summary>
public enum AdvisorOutcome
{
    /// <summary>未调用顾问。安全保持和空候选集会跳过顾问。</summary>
    NotConsulted = 0,

    /// <summary>顾问返回了引擎接受的建议。</summary>
    Applied = 1,

    /// <summary>顾问返回 null。规则顺序保持不变。这是未安装模型时的正常路径。</summary>
    Declined = 2,

    /// <summary>顾问给出了未知编号或重复编号。规则顺序保持不变。</summary>
    InvalidReorder = 3,

    /// <summary>顾问超过 <see cref="DispatchPolicy.AdvisorTimeout"/>，且调用方取消令牌当时仍未取消。</summary>
    TimedOut = 4,

    /// <summary>顾问抛出异常。规则顺序保持不变。</summary>
    Faulted = 5
}

/// <summary>一次评估的确定性结果。物理动作尚未发生。</summary>
public sealed record DispatchDecision
{
    /// <summary><see cref="DispatchRequest.RequestId"/> 的副本。</summary>
    public required string RequestId { get; init; }

    /// <summary>为 true 时应放行车身，且 <see cref="Selected"/> 已填写。</summary>
    public required bool ShouldRelease { get; init; }

    /// <summary>粗略原因。顾问路径需与 <see cref="AdvisorOutcome"/> 一起看。</summary>
    public required DispatchReason Reason { get; init; }

    /// <summary>名次 1 的候选；<see cref="ShouldRelease"/> 为 false 时为 null。</summary>
    public CandidateScore? Selected { get; init; }

    /// <summary>完整排序。未评分时为空。名次 1 即 <see cref="Selected"/>。</summary>
    public required IReadOnlyList<CandidateScore> RankedCandidates { get; init; }

    /// <summary>供现场人机界面或演示记录使用的中文说明。</summary>
    public required string Explanation { get; init; }

    /// <summary>请求紧急标志的副本。</summary>
    public required bool IsUrgent { get; init; }

    /// <summary>实际使用的最小序号权重。<see cref="IsUrgent"/> 为 true 时已降低。</summary>
    public required decimal AppliedMinSequenceWeight { get; init; }

    /// <summary>全局联锁结果。允许评分放行时为 <see cref="SafetyHoldKind.None"/>。</summary>
    public required SafetyHoldKind SafetyHold { get; init; }

    /// <summary>调度器被要求读取的缓存车道数量。</summary>
    public required int QueuesEvaluated { get; init; }

    /// <summary>本次决策所走的顾问路径。</summary>
    public required AdvisorOutcome AdvisorOutcome { get; init; }

    /// <summary>已接受的重排改变了候选顺序时为 true。</summary>
    public required bool AdvisorReordered { get; init; }

    /// <summary>模型文本，或简短故障信息。顾问未发言时为 null。</summary>
    public string? AdvisorExplanation { get; init; }

    /// <summary>已调用顾问。</summary>
    public bool AdvisorConsulted => AdvisorOutcome != AdvisorOutcome.NotConsulted;

    /// <summary>顾问回复已被接受。</summary>
    public bool AdvisorApplied => AdvisorOutcome == AdvisorOutcome.Applied;

    /// <summary>
    /// 因顾问缺失、过慢、无效或抛出异常而保持规则顺序。
    /// 安全保持不是回退；只是根本不会询问顾问。
    /// </summary>
    public bool FellBackToRules => AdvisorOutcome is AdvisorOutcome.Declined
        or AdvisorOutcome.InvalidReorder
        or AdvisorOutcome.TimedOut
        or AdvisorOutcome.Faulted;
}
