namespace PaintQueueLocalAi;

/// <summary>
/// 可选的本地模型。它可以重排前列候选，或解释规则已经做出的决策。
/// 引擎才是事实来源：返回 null、超时、抛出异常或编号列表无效时，
/// 规则顺序保持不变。实现不得调用云端 API。
/// </summary>
/// <remarks>
/// <para>
/// 涂装工控机装上模型文件之前，先交付 <see cref="NullLlmAdvisor"/>。
/// 后续宿主可以封装 LLamaSharp（llama.cpp，CPU）或本机回环上的 Ollama，地址为
/// <c>http://127.0.0.1:11434</c>。本库不引用这两个包。
/// 不要把实现指向公网端点。
/// </para>
/// <para>
/// 目标机是离线的 Intel i5-14500，16 GB 内存，核显为 Intel UHD 770。
/// 没有独立显卡。实用的本地权重是 Qwen2.5-1.5B-Instruct Q4、
/// Qwen2.5-3B-Instruct Q4 或 Phi-3-mini Q4。生成必须落在
/// <see cref="DispatchPolicy.AdvisorTimeout"/> 之内。失败时返回 null 或抛出异常；
/// 确定性排序此时已经完成。
/// </para>
/// <para>
/// 顾问只能排列 <see cref="LlmAdviceRequest.TopCandidates"/> 中已有的编号。
/// 不得编造车身、解除联锁或写 PLC。
/// </para>
/// </remarks>
public interface ILlmAdvisor
{
    /// <summary>
    /// 查看前列候选。返回 null 表示保持规则顺序。
    /// 必须遵守 <paramref name="cancellationToken"/>；超时后引擎会取消它。
    /// </summary>
    Task<LlmAdvice?> AdviseAsync(LlmAdviceRequest request, CancellationToken cancellationToken);
}
