namespace PaintQueueLocalAi;

/// <summary>
/// 确定性评分的可调参数。默认值符合涂装线约定：
/// 快车道档位更高，最小序号权重大于负载均衡，紧急请求
/// 把序号权重乘以 0.2。
/// </summary>
public sealed record DispatchPolicy
{
    /// <summary>构造引擎时未传入策略则使用本策略。</summary>
    public static DispatchPolicy Default { get; } = new();

    /// <summary>
    /// 为 true 时，已打开的出口 A 快车道压过所有缓存车头，不论序号。
    /// 为 false 时，出口 A 上的车身不是直放候选。
    /// </summary>
    public bool EnableFastLaneA { get; init; } = true;

    /// <summary>非紧急评分中序号接近程度的权重。必须为零或正数。</summary>
    public decimal MinSequenceWeight { get; init; } = 400m;

    /// <summary>
    /// 请求紧急时乘到 <see cref="MinSequenceWeight"/> 上的系数。
    /// 必须在 0 到 1 之间，以免紧急请求加大序号偏向。
    /// </summary>
    public decimal UrgentMinSequenceWeightFactor { get; init; } = 0.2m;

    /// <summary>缓存充满率的权重。必须为零或正数。快车道不计此项。</summary>
    public decimal LoadBalanceWeight { get; init; } = 150m;

    /// <summary>展示给 <see cref="ILlmAdvisor"/> 的规则排序候选数量。</summary>
    public int AdvisorTopK { get; init; } = 3;

    /// <summary>
    /// 单次顾问调用的时间预算。默认很短是有意的：目标工控机没有独立显卡。
    /// 超出预算时保持规则顺序。
    /// </summary>
    public TimeSpan AdvisorTimeout { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>某个参数超出约定时抛出 <see cref="ArgumentOutOfRangeException"/>。</summary>
    public void EnsureValid()
    {
        if (MinSequenceWeight < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(MinSequenceWeight), MinSequenceWeight, "Min-sequence weight cannot be negative.");
        }

        if (LoadBalanceWeight < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(LoadBalanceWeight), LoadBalanceWeight, "Load-balance weight cannot be negative.");
        }

        if (UrgentMinSequenceWeightFactor < 0m || UrgentMinSequenceWeightFactor > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(UrgentMinSequenceWeightFactor),
                UrgentMinSequenceWeightFactor,
                "Urgent min-sequence factor must be from 0 to 1 so urgency cannot raise the sequence weight.");
        }

        if (AdvisorTopK < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(AdvisorTopK), AdvisorTopK, "Advisor top-K must be at least 1.");
        }

        if (AdvisorTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(AdvisorTimeout), AdvisorTimeout, "Advisor timeout must be positive.");
        }
    }
}
