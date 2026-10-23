namespace PaintQueueLocalAi;

/// <summary>
/// Advisor used when no local model is installed. It returns null so the engine keeps the rule order.
/// Thread-safe and allocation-light. This is the offline default.
/// </summary>
public sealed class NullLlmAdvisor : ILlmAdvisor
{
    /// <summary>Shared instance. The type has no state.</summary>
    public static NullLlmAdvisor Instance { get; } = new();

    /// <inheritdoc />
    public Task<LlmAdvice?> AdviseAsync(LlmAdviceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<LlmAdvice?>(null);
    }
}
