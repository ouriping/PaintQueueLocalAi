using System.Text;
using LLama;
using LLama.Common;
using LLama.Sampling;
using PaintQueueLocalAi;

namespace PaintQueueLocalAi.LlamaSharp;

public sealed record LlamaSharpAdvisorOptions
{
    public string ModelPath { get; init; } = "models/qwen2.5-1.5b-instruct-q4_k_m.gguf";

    public int Threads { get; init; } = 6;

    public int ContextSize { get; init; } = 2048;

    public int MaxTokens { get; init; } = 80;

    /// <summary>目标机只有核显。非 0 会被忽略，实际始终按 CPU 加载。</summary>
    public int GpuLayerCount { get; init; }
}

/// <summary>
/// GGUF 顾问。权重只在 <see cref="WarmUpAsync"/> 时加载，节拍内调用不会临时读盘。
/// 未预热、文件缺失或模型忙时返回 null，由调度器保持规则顺序。
/// </summary>
public sealed class LlamaSharpAdvisor : ILlmAdvisor, IAdvisorWarmup, ILocalAdvisorStatus, IDisposable
{
    private readonly LlamaSharpAdvisorOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LlamaSharpSession? _session;

    public LlamaSharpAdvisor(LlamaSharpAdvisorOptions? options = null)
    {
        _options = options ?? new LlamaSharpAdvisorOptions();
        if (string.IsNullOrWhiteSpace(_options.ModelPath))
        {
            throw new ArgumentException("GGUF 路径不能为空。", nameof(options));
        }

        if (_options.GpuLayerCount != 0)
        {
            GpuLayerWarning = "已忽略 GpuLayerCount，强制 CPU（GpuLayerCount=0）。目标机不使用 CUDA。";
        }

        Status = "未预热";
    }

    public int AppliedGpuLayerCount => 0;

    public string? GpuLayerWarning { get; }

    public string Status { get; private set; }

    public async Task WarmUpAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_options.ModelPath))
        {
            Status = "模型文件不存在";
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_session is not null)
            {
                Status = "已预热";
                return;
            }

            _session = LlamaSharpSession.Load(_options);
            Status = "已预热";
        }
        catch (Exception ex)
        {
            _session?.Dispose();
            _session = null;
            Status = "预热失败: " + ex.GetType().Name;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<AdvisorSuggestion?> AdviseAsync(AdvisorContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!File.Exists(_options.ModelPath))
        {
            Status = "模型文件不存在";
            return null;
        }

        if (_session is null)
        {
            Status = "模型尚未预热";
            return null;
        }

        var entered = await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false);
        if (!entered)
        {
            Status = "模型忙";
            return null;
        }

        try
        {
            var user = AdvisorPrompt.BuildUser(context.Booth, context.TopCandidates);
            var prompt = AdvisorPrompt.BuildRawPrompt(AdvisorPrompt.SystemPrompt, user);
            var text = await _session.CompleteAsync(prompt, cancellationToken).ConfigureAwait(false);
            var parsed = AdvisorResponseParser.Parse(text);
            if (!parsed.Success)
            {
                throw new FormatException(parsed.Error);
            }

            Status = "就绪";
            return new AdvisorSuggestion(parsed.Order, parsed.Explanation);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
        _gate.Dispose();
    }
}

internal sealed class LlamaSharpSession : IDisposable
{
    private readonly LLamaWeights _weights;
    private readonly StatelessExecutor _executor;
    private readonly InferenceParams _inference;

    private LlamaSharpSession(LLamaWeights weights, StatelessExecutor executor, InferenceParams inference)
    {
        _weights = weights;
        _executor = executor;
        _inference = inference;
    }

    public static LlamaSharpSession Load(LlamaSharpAdvisorOptions options)
    {
        var threads = Math.Clamp(options.Threads, 1, 12);
        var parameters = new ModelParams(options.ModelPath)
        {
            ContextSize = (uint)Math.Clamp(options.ContextSize, 512, 4096),
            GpuLayerCount = 0,
            Threads = threads,
            BatchThreads = threads,
            BatchSize = 128,
            UBatchSize = 128,
            UseMemorymap = true,
            UseMemoryLock = false,
            FlashAttention = false
        };
        var weights = LLamaWeights.LoadFromFile(parameters);
        try
        {
            var executor = new StatelessExecutor(weights, parameters);
            var sampling = new DefaultSamplingPipeline
            {
                Temperature = 0.1f,
                TopK = 40,
                TopP = 0.9f,
                MinP = 0f,
                Seed = 1
            };
            var inference = new InferenceParams
            {
                MaxTokens = Math.Clamp(options.MaxTokens, 16, 128),
                AntiPrompts = new[] { "<|im_end|>" },
                SamplingPipeline = sampling
            };
            return new LlamaSharpSession(weights, executor, inference);
        }
        catch
        {
            weights.Dispose();
            throw;
        }
    }

    public async Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        await foreach (var chunk in _executor.InferAsync(prompt, _inference, cancellationToken).ConfigureAwait(false))
        {
            sb.Append(chunk);
            if (sb.Length > 600)
            {
                break;
            }
        }

        return sb.ToString();
    }

    public void Dispose() => _weights.Dispose();
}
