using System.Globalization;
using System.Text;
using System.Text.Json;
using PaintQueueLocalAi;
using PaintQueueLocalAi.Local;
using PaintQueueLocalAi.Simulation;

Console.OutputEncoding = Encoding.UTF8;

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
    var settings = DemoSettings.Load(args);
    if (settings.Help)
    {
        PrintHelp();
        return 0;
    }

    if (settings.ExportDatasetPath is not null)
    {
        AdvisorDatasetWriter.WriteFile(
            settings.ExportDatasetPath,
            await AdvisorDatasetWriter.SerializeAsync(settings.DatasetCount, settings.DatasetSeed).ConfigureAwait(false));
        Console.WriteLine("已写出训练集：" + settings.ExportDatasetPath);
    }

    if (settings.ExportOverridesPath is not null)
    {
        AdvisorDatasetWriter.WriteFile(
            settings.ExportOverridesPath,
            await AdvisorDatasetWriter.SerializePlantOverridesAsync().ConfigureAwait(false));
        Console.WriteLine("已写出现场覆盖样本：" + settings.ExportOverridesPath);
    }

    if (settings.ExportModelfilePath is not null)
    {
        AdvisorDatasetWriter.WriteFile(
            settings.ExportModelfilePath,
            AdvisorDatasetWriter.RenderModelfile(settings.ModelfileFrom));
        Console.WriteLine("已写出 Modelfile：" + settings.ExportModelfilePath);
    }

    if (settings.ExportOnly)
    {
        return 0;
    }

    var report = await new InMemoryPaintShopSimulator().RunAllAsync().ConfigureAwait(false);
    Console.WriteLine(report.ToChineseText());
    await RunAdvisorSampleAsync(settings).ConfigureAwait(false);
    return report.AllPassed ? 0 : 1;
}

static async Task RunAdvisorSampleAsync(DemoSettings settings)
{
    Console.WriteLine();
    Console.WriteLine("本地顾问（Ollama / Qwen）");
    if (settings.Provider == "null")
    {
        Console.WriteLine("已跳过。四段仿真仍使用规则和 NullLlmAdvisor。");
        return;
    }

    if (settings.Provider == "llamasharp")
    {
        Console.WriteLine("本演示走 Ollama。GGUF / LLamaSharp 是备选宿主，见 src/PaintQueueLocalAi.LlamaSharp。");
        return;
    }

    Console.WriteLine("模型名必须与 ollama list 一致，当前为 " + settings.Model + "。已安装其他标签时用 --model。");
    Console.WriteLine("放行节拍的 AdvisorTimeout 仍是 200 ms。下面这次演示用 " + settings.TimeoutMs.ToString(CultureInfo.InvariantCulture) + " ms，方便看到回复。");
    using var advisor = new OllamaLoopbackAdvisor(new OllamaAdvisorOptions
    {
        BaseUrl = settings.BaseUrl,
        Model = settings.Model
    });
    if (settings.Warmup)
    {
        Console.WriteLine("正在预热（不占用四段仿真）…");
        await advisor.WarmUpAsync(CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine("模型状态: " + advisor.Status);
    }

    var policy = DispatchPolicy.Default with { AdvisorTimeout = TimeSpan.FromMilliseconds(settings.TimeoutMs) };
    var engine = new DispatchEngine(policy, advisor);
    var decision = await engine.EvaluateAsync(SampleSnapshots.MinSequence()).ConfigureAwait(false);
    Console.WriteLine("顾问结果: " + decision.AdvisorOutcome);
    Console.WriteLine("放行车辆: " + (decision.Selected?.VehicleId ?? "-"));
    Console.WriteLine(decision.FellBackToRules
        ? "Ollama 不可用或输出无效，已保持规则顺序。"
        : "已采用本机顾问的回复。");
    Console.WriteLine(decision.Explanation);
}

static void PrintHelp()
{
    Console.WriteLine("""
        四段规则仿真始终使用 NullLlmAdvisor。随后按 appsettings 调用本机 Ollama。

          --provider ollama|null|llamasharp
          --model qwen2.5:0.5b      必须与 ollama list 一致；已安装 1.5b 时传 qwen2.5:1.5b
          --base-url http://127.0.0.1:11434
          --timeout-ms 8000         只影响演示里的顾问调用
          --no-warmup
          --export-dataset <path>
          --export-overrides <path>
          --export-modelfile <path> --modelfile-from <gguf>
        """);
}

internal sealed record DemoSettings(
    bool Help,
    string Provider,
    string Model,
    string BaseUrl,
    int TimeoutMs,
    bool Warmup,
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
        var provider = "ollama";
        var model = "qwen2.5:0.5b";
        var baseUrl = "http://127.0.0.1:11434";
        var timeoutMs = 8000;
        var warmup = true;
        var help = false;
        string? exportDataset = null;
        string? exportOverrides = null;
        string? exportModelfile = null;
        var modelfileFrom = "./paint-advisor-q4_k_m.gguf";
        var datasetCount = DatasetDefaults.Count;
        var datasetSeed = DatasetDefaults.Seed;
        var file = ResolveAppSettings();
        if (file is not null)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var root = document.RootElement;
            provider = ReadString(root, "provider") ?? provider;
            model = ReadString(root, "model") ?? model;
            baseUrl = ReadString(root, "baseUrl") ?? baseUrl;
            timeoutMs = ReadInt(root, "timeoutMs") ?? timeoutMs;
            warmup = ReadBool(root, "warmup") ?? warmup;
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
                case "--help":
                case "-h":
                    help = true;
                    break;
                case "--provider":
                    provider = Next();
                    break;
                case "--model":
                    model = Next();
                    break;
                case "--base-url":
                    baseUrl = Next();
                    break;
                case "--timeout-ms":
                    timeoutMs = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--warmup":
                    warmup = true;
                    break;
                case "--no-warmup":
                    warmup = false;
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
                    datasetCount = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--dataset-seed":
                    datasetSeed = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                default:
                    throw new ArgumentException("无法识别的参数：" + args[i]);
            }
        }

        if (provider is not ("ollama" or "null" or "llamasharp"))
        {
            throw new ArgumentException("provider 只能是 ollama、null 或 llamasharp。");
        }

        return new DemoSettings(
            help,
            provider,
            model,
            baseUrl,
            timeoutMs,
            warmup,
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
