namespace PaintQueueLocalAi;

/// <summary>可选的本地模型回复。顾问返回 null 表示“没有建议”。</summary>
public sealed record LlmAdvice
{
    /// <summary>
    /// 前 K 名窗口内 <see cref="CandidateScore.CandidateId"/> 的期望顺序。
    /// null 保持规则顺序。窗口外的编号、重复项或空列表会被拒绝。
    /// 不完整的列表会按原顺序补上其余前 K 名编号。
    /// </summary>
    public IReadOnlyList<string>? ReorderedCandidateIds { get; init; }

    /// <summary>可选说明。引擎最多保存 2000 个字符。说明本身不会改变排序。</summary>
    public string? Explanation { get; init; }
}
