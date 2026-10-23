namespace PaintQueueLocalAi;

public static class AdvisorLimits
{
    public const int DefaultTimeoutMilliseconds = 200;
    public const int DefaultTopK = 5;
    public const int MaxTopK = 8;
    public const int MaxExplanationChars = 80;
    public const int PromptExplanationChars = 40;
}

public static class AdvisorFallbackReason
{
    public const string Timeout = "timeout";
    public const string NoSuggestion = "no-suggestion";
    public const string Invalid = "invalid";
    public const string ExceptionPrefix = "exception:";
}

/// <summary>
/// 本地顾问。只能针对已经给出的候选返回重排和一句说明。
/// 实现必须遵守取消标记，并且不得访问云端。
/// 返回 null 表示没有建议，调度保持规则顺序。
/// </summary>
public interface ILlmAdvisor
{
    ValueTask<AdvisorSuggestion?> AdviseAsync(AdvisorContext context, CancellationToken cancellationToken);
}

/// <summary>开班预热。不得占用节拍内的顾问超时。</summary>
public interface IAdvisorWarmup
{
    Task WarmUpAsync(CancellationToken cancellationToken);
}

public interface ILocalAdvisorStatus
{
    string Status { get; }
}

public sealed class NullLlmAdvisor : ILlmAdvisor
{
    public static NullLlmAdvisor Instance { get; } = new();

    public ValueTask<AdvisorSuggestion?> AdviseAsync(AdvisorContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ValueTask.FromResult<AdvisorSuggestion?>(null);
    }
}

public sealed record AdvisorContext(
    BoothState Booth,
    IReadOnlyList<RankedCandidate> TopCandidates,
    TimeSpan Timeout);

public sealed record AdvisorSuggestion(IReadOnlyList<string> OrderedCandidateIds, string? Explanation);

public sealed record DispatchOptions
{
    public int TopK { get; init; } = AdvisorLimits.DefaultTopK;

    public TimeSpan AdvisorTimeout { get; init; } = TimeSpan.FromMilliseconds(AdvisorLimits.DefaultTimeoutMilliseconds);

    public void EnsureValid()
    {
        if (TopK < 1 || TopK > AdvisorLimits.MaxTopK)
        {
            throw new ArgumentOutOfRangeException(nameof(TopK), "TopK 必须在 1 到 8 之间，以控制提示长度。");
        }

        if (AdvisorTimeout < TimeSpan.FromMilliseconds(1) || AdvisorTimeout > TimeSpan.FromSeconds(30))
        {
            throw new ArgumentOutOfRangeException(nameof(AdvisorTimeout), "顾问超时必须在 1 毫秒到 30 秒之间。");
        }
    }
}
