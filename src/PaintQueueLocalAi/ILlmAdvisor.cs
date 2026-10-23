namespace PaintQueueLocalAi;

/// <summary>
/// Optional local model that may re-rank the top candidates or explain a decision the rules already made.
/// The engine is the source of truth: a null reply, a timeout, a thrown exception, or an invalid id list
/// leaves the rule order unchanged. Implementations must not call a cloud API.
/// </summary>
/// <remarks>
/// <para>
/// Ship <see cref="NullLlmAdvisor"/> until a model file is installed on the paint-shop PC.
/// A later host can wrap LLamaSharp (llama.cpp, CPU) or a loopback Ollama server at
/// <c>http://127.0.0.1:11434</c>. This library does not reference either package.
/// Do not point an implementation at a public endpoint.
/// </para>
/// <para>
/// The target machine is an offline Intel i5-14500 with 16 GB of RAM and Intel UHD 770.
/// There is no discrete GPU. Practical local weights are Qwen2.5-1.5B-Instruct Q4,
/// Qwen2.5-3B-Instruct Q4, or Phi-3-mini Q4. Keep generations inside
/// <see cref="DispatchPolicy.AdvisorTimeout"/>. On a miss, return null or throw;
/// the deterministic ranking is already complete.
/// </para>
/// <para>
/// An advisor may only permute ids present in <see cref="LlmAdviceRequest.TopCandidates"/>.
/// It must not invent a body, clear an interlock, or write to the PLC.
/// </para>
/// </remarks>
public interface ILlmAdvisor
{
    /// <summary>
    /// Inspect the top candidates. Return null to keep the rule order.
    /// Honor <paramref name="cancellationToken"/>; the engine cancels it when the timeout expires.
    /// </summary>
    Task<LlmAdvice?> AdviseAsync(LlmAdviceRequest request, CancellationToken cancellationToken);
}
