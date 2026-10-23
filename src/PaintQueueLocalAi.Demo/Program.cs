using System.Text;
using System.Text.Json;
using PaintQueueLocalAi;
using PaintQueueLocalAi.LlamaSharp;
using PaintQueueLocalAi.Local;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

try
{
    return await RunAsync(args);
}
catch (Exception ex) when (ex is ArgumentException or FormatException)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

static async Task<int> RunAsync(string[] args)
{
    if (args.Any(arg => arg is "--help" or "-h"))
    {
        PrintHelp();
        return 0;
    }

    var options = DemoSettings.Load(args);
    if (options.ExportDatasetPath is not null)
    {
        AdvisorDatasetWriter.WriteFile(
            options.ExportDatasetPath,
            AdvisorDatasetWriter.Serialize(options.DatasetCount, options.DatasetSeed));
        Console.WriteLine($"已写出训练集 {options.DatasetCount} 条：{options.ExportDatasetPath}");
    }

    if (options.ExportOverridesPath is not null)
    {
        AdvisorDatasetWriter.WriteFile(options.ExportOverridesPath, AdvisorDatasetWriter.SerializePlantOverrides());
        Console.WriteLine("已写出现场覆盖样本：" + options.ExportOverridesPath);
    }

    if (options.ExportModelfilePath is not null)
    {
        AdvisorDatasetWriter.WriteFile(
            options.ExportModelfilePath,
            AdvisorDatasetWriter.RenderModelfile(options.ModelfileFrom));
        Console.WriteLine("已写出 Modelfile：" + options.ExportModelfilePath);
    }

    if (options.ExportOnly)
    {
        return 0;
    }

    if (options.SelfCheck)
    {
        return await SelfCheckAsync();
    }

    ILlmAdvisor advisor = CreateAdvisor(options);
    try
    {
        if (options.Warmup && advisor is IAdvisorWarmup warmup)
        {
            Console.WriteLine("正在预热本地模型（不占用节拍超时）…");
            await warmup.WarmUpAsync(CancellationToken.None);
        }

        if (advisor is ILocalAdvisorStatus status)
        {
            Console.WriteLine("模型状态: " + status.Status);
        }

        var engine = new DispatchEngine(
            new DispatchOptions
            {
                TopK = options.TopK,
                AdvisorTimeout = TimeSpan.FromMilliseconds(options.TimeoutMs)
            },
            advisor);
        var sample = options.Scenario == "interlock" ? SampleQueues.EmergencyStop() : SampleQueues.MorningShift();
        var decision = await engine.DispatchAsync(sample.Booth, sample.Jobs);
        PrintDecision(decision);
        if (string.Equals(decision.FallbackReason, AdvisorFallbackReason.Timeout, StringComparison.Ordinal))
        {
            Console.WriteLine("模型未在节拍内返回。人工查看说明时可加 --timeout-ms 5000，并确认已预热。");
        }

        return 0;
    }
    finally
    {
        if (advisor is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

static ILlmAdvisor CreateAdvisor(DemoSettings options) => options.Provider switch
{
    "null" => NullLlmAdvisor.Instance,
    "ollama" => new OllamaLoopbackAdvisor(new OllamaAdvisorOptions
    {
        BaseUrl = options.BaseUrl,
        Model = options.OllamaModel
    }),
    "llamasharp" => new LlamaSharpAdvisor(new LlamaSharpAdvisorOptions
    {
        ModelPath = options.ModelPath,
        Threads = options.Threads,
        GpuLayerCount = options.GpuLayerCount
    }),
    _ => throw new ArgumentException("未知 provider。可用值：null、ollama、llamasharp。")
};

static async Task<int> SelfCheckAsync()
{
    var engine = new DispatchEngine(new DispatchOptions(), NullLlmAdvisor.Instance);
    var sample = SampleQueues.MorningShift();
    var decision = await engine.DispatchAsync(sample.Booth, sample.Jobs);
    var blocked = await engine.DispatchAsync(SampleQueues.EmergencyStop().Booth, sample.Jobs);
    if (decision.SelectedJobId != SampleQueues.MorningWinnerId || decision.AdvisorApplied)
    {
        Console.Error.WriteLine("自检失败：无模型时规则结果不符合预期。");
        return 1;
    }

    if (!blocked.Blocked || blocked.AdvisorInvoked || blocked.BlockingInterlock != InterlockKind.EmergencyStop)
    {
        Console.Error.WriteLine("自检失败：急停仍询问了模型或没有阻断。");
        return 1;
    }

    Console.WriteLine("自检通过：无模型时规则选中 W-URG，急停不询问模型。");
    return 0;
}

static void PrintDecision(DispatchDecision decision)
{
    Console.WriteLine(decision.Blocked
        ? "联锁: " + InterlockText.ToChinese(decision.BlockingInterlock!.Value)
        : "选中: " + (decision.SelectedJobId ?? "-"));
    Console.WriteLine("规则顺序: " + FormatOrder(decision.RuleOrder));
    Console.WriteLine("最终顺序: " + FormatOrder(decision.FinalOrder));
    Console.WriteLine("顾问调用: " + (decision.AdvisorInvoked ? "是" : "否"));
    Console.WriteLine("顾问采用: " + (decision.AdvisorApplied ? "是" : "否"));
    Console.WriteLine("回退: " + (decision.FallbackReason ?? "-"));
    Console.WriteLine("界面: " + decision.HmiText);
    if (decision.Excluded.Count > 0)
    {
        Console.WriteLine("排除: " + string.Join("，", decision.Excluded.Select(item => item.JobId + " " + item.Reason)));
    }
}

static string FormatOrder(IReadOnlyList<RankedCandidate> candidates)
{
    if (candidates.Count == 0)
    {
        return "-";
    }

    return string.Join(" > ", candidates.Select(item => item.CandidateId + "(" + item.Score + ")"));
}

static void PrintHelp()
{
    Console.WriteLine("""
        PaintQueueLocalAi 演示

          --provider null|ollama|llamasharp
          --timeout-ms 200          节拍内重排预算，默认 200
          --top-k 5
          --base-url http://127.0.0.1:11434
          --model qwen2.5:1.5b      Ollama 模型名
          --model-path models/qwen2.5-1.5b-instruct-q4_k_m.gguf
          --threads 6
          --scenario sample|interlock
          --warmup / --no-warmup
          --self-check               无模型自检
          --export-dataset <path>
          --export-overrides <path>
          --export-modelfile <path> --modelfile-from <gguf>
          --dataset-count 48 --dataset-seed 20261023

        没有模型文件时调度仍然按规则完成。顾问地址只允许本机回环。
        """);
}

internal sealed record DemoSettings(
    string Provider,
    int TimeoutMs,
    int TopK,
    bool Warmup,
    string BaseUrl,
    string OllamaModel,
    string ModelPath,
    int Threads,
    int GpuLayerCount,
    string Scenario,
    bool SelfCheck,
    string? ExportDatasetPath,
    string? ExportOverridesPath,
    string? ExportModelfilePath,
    string ModelfileFrom,
    int DatasetCount,
    int DatasetSeed)
{
    public bool ExportOnly => ExportDatasetPath is not null || ExportOverridesPath is not null || ExportModelfilePath is not null;

    public static DemoSettings Load(string[] args)
    {
        var provider = "null";
        var timeoutMs = AdvisorLimits.DefaultTimeoutMilliseconds;
        var topK = AdvisorLimits.DefaultTopK;
        var warmup = true;
        var baseUrl = "http://127.0.0.1:11434";
        var model = "qwen2.5:1.5b";
        var modelPath = "models/qwen2.5-1.5b-instruct-q4_k_m.gguf";
        var threads = 6;
        var gpu = 0;
        var scenario = "sample";
        var selfCheck = false;
        string? exportDataset = null;
        string? exportOverrides = null;
        string? exportModelfile = null;
        var modelfileFrom = "paint-advisor-q4_k_m.gguf";
        var datasetCount = DatasetDefaults.Count;
        var datasetSeed = DatasetDefaults.Seed;
        var file = ResolveAppSettings();
        if (file is not null)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            provider = ReadString(root, "provider") ?? provider;
            timeoutMs = ReadInt(root, "timeoutMs") ?? timeoutMs;
            topK = ReadInt(root, "topK") ?? topK;
            warmup = ReadBool(root, "warmup") ?? warmup;
            threads = ReadInt(root, "threads") ?? threads;
            if (root.TryGetProperty("ollama", out var ollama))
            {
                baseUrl = ReadString(ollama, "baseUrl") ?? baseUrl;
                model = ReadString(ollama, "model") ?? model;
            }

            if (root.TryGetProperty("llamaSharp", out var llama))
            {
                modelPath = ReadString(llama, "modelPath") ?? modelPath;
                gpu = ReadInt(llama, "gpuLayerCount") ?? gpu;
            }
        }

        for (var i = 0; i < args.Length; i++)
        {
            string Next()
            {
                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException("缺少参数值：" + args[i]);
                }

                return args[++i];
            }

            switch (args[i])
            {
                case "--provider":
                    provider = Next();
                    break;
                case "--timeout-ms":
                    timeoutMs = int.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--top-k":
                    topK = int.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--base-url":
                    baseUrl = Next();
                    break;
                case "--model":
                    model = Next();
                    break;
                case "--model-path":
                    modelPath = Next();
                    break;
                case "--threads":
                    threads = int.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--scenario":
                    scenario = Next();
                    break;
                case "--warmup":
                    warmup = true;
                    break;
                case "--no-warmup":
                    warmup = false;
                    break;
                case "--self-check":
                    selfCheck = true;
                    break;
                case "--export-dataset":
                    exportDataset = Next();
                    break;
                case "--export-overrides":
                    exportOverrides = Next();
                    break;
                case "--export-modelfile":
                    exportModelfile = Next();
                    break;
                case "--modelfile-from":
                    modelfileFrom = Next();
                    break;
                case "--dataset-count":
                    datasetCount = int.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--dataset-seed":
                    datasetSeed = int.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture);
                    break;
                default:
                    throw new ArgumentException("无法识别的参数：" + args[i]);
            }
        }

        if (scenario is not ("sample" or "interlock"))
        {
            throw new ArgumentException("scenario 只能是 sample 或 interlock。");
        }

        if (provider is not ("null" or "ollama" or "llamasharp"))
        {
            throw new ArgumentException("provider 只能是 null、ollama 或 llamasharp。");
        }

        return new DemoSettings(
            provider,
            timeoutMs,
            topK,
            warmup,
            baseUrl,
            model,
            modelPath,
            threads,
            gpu,
            scenario,
            selfCheck,
            exportDataset,
            exportOverrides,
            exportModelfile,
            modelfileFrom,
            datasetCount,
            datasetSeed);
    }

    private static string? ResolveAppSettings()
    {
        var cwd = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
        if (File.Exists(cwd))
        {
            return cwd;
        }

        var beside = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        return File.Exists(beside) ? beside : null;
    }

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? ReadInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;

    private static bool? ReadBool(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
