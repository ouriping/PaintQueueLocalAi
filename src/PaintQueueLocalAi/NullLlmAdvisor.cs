namespace PaintQueueLocalAi;

/// <summary>
/// 未安装本地模型时使用的顾问。它返回 null，使引擎保持规则顺序。
/// 线程安全，分配很少。这是离线默认实现。
/// </summary>
public sealed class NullLlmAdvisor : ILlmAdvisor
{
    /// <summary>共享实例。该类型没有状态。</summary>
    public static NullLlmAdvisor Instance { get; } = new();

    /// <inheritdoc />
    public Task<LlmAdvice?> AdviseAsync(LlmAdviceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<LlmAdvice?>(null);
    }
}
