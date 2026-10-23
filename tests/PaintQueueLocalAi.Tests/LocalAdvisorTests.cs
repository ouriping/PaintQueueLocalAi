using System.Net;
using System.Text;
using PaintQueueLocalAi.LlamaSharp;
using PaintQueueLocalAi.Local;
using PaintQueueLocalAi.Simulation;

namespace PaintQueueLocalAi.Tests;

public class LocalAdvisorTests
{
    [Theory]
    [InlineData("http://127.0.0.1:11434")]
    [InlineData("http://localhost:11434")]
    [InlineData("http://[::1]:11434")]
    public void LoopbackGuard_AllowsLoopback(string url)
    {
        Assert.True(LoopbackAddressGuard.IsLoopbackHost(LoopbackAddressGuard.Ensure(url).Host));
    }

    [Theory]
    [InlineData("https://api.openai.com/v1")]
    [InlineData("http://192.168.1.10:11434")]
    [InlineData("http://user:pass@127.0.0.1:11434")]
    [InlineData("http://127.0.0.1:11434/api")]
    public void LoopbackGuard_RejectsAnythingElse(string url)
    {
        Assert.Throws<ArgumentException>(() => LoopbackAddressGuard.Ensure(url));
    }

    [Fact]
    public void OllamaOptions_DefaultToQwen05B()
    {
        var options = new OllamaAdvisorOptions();
        Assert.Equal("qwen2.5:0.5b", options.Model);
        Assert.Equal("http://127.0.0.1:11434", options.BaseUrl);
    }

    [Fact]
    public async Task OllamaAdvisor_PostsLoopbackChatForRealCandidates()
    {
        var context = await AdviceContextAsync();
        var orderJson = AdvisorDatasetWriter.SerializeAssistant(new[] { context.TopCandidates[0].CandidateId }, "最小序号优先");
        var handler = new RecordingHandler("{\"message\":{\"content\":" + JsonEscape(orderJson) + "}}");
        using var advisor = new OllamaLoopbackAdvisor(new OllamaAdvisorOptions(), handler);
        var advice = await advisor.AdviseAsync(context, CancellationToken.None);
        Assert.NotNull(advice);
        Assert.Equal(context.TopCandidates[0].CandidateId, advice!.ReorderedCandidateIds![0]);
        Assert.Equal(new Uri("http://127.0.0.1:11434/api/chat"), handler.LastUri);
        Assert.Contains("\"model\":\"qwen2.5:0.5b\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains(context.TopCandidates[0].CandidateId, handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("B-0001", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OllamaDown_InsideEngine_KeepsRuleWinner()
    {
        var handler = new RecordingHandler("not-json", HttpStatusCode.OK);
        using var advisor = new OllamaLoopbackAdvisor(handler: handler);
        var decision = await new DispatchEngine(advisor: advisor).EvaluateAsync(SampleSnapshots.MinSequence());
        Assert.Equal(AdvisorOutcome.Faulted, decision.AdvisorOutcome);
        Assert.True(decision.FellBackToRules);
        Assert.Equal("B-0012", decision.Selected!.VehicleId);
        Assert.Equal(DispatchReason.PolicySelection, decision.Reason);
    }

    [Fact]
    public async Task OllamaMissingModel_Http404_KeepsRuleWinnerAndNamesTheModel()
    {
        var handler = new RecordingHandler("{\"error\":\"model not found\"}", HttpStatusCode.NotFound);
        using var advisor = new OllamaLoopbackAdvisor(handler: handler);
        await advisor.WarmUpAsync(CancellationToken.None);
        Assert.Contains("404", advisor.Status, StringComparison.Ordinal);
        Assert.Contains("模型未找到", advisor.Status, StringComparison.Ordinal);

        var decision = await new DispatchEngine(advisor: advisor).EvaluateAsync(SampleSnapshots.MinSequence());
        Assert.Equal(AdvisorOutcome.Faulted, decision.AdvisorOutcome);
        Assert.True(decision.FellBackToRules);
        Assert.Equal("B-0012", decision.Selected!.VehicleId);
        Assert.Equal(DispatchReason.PolicySelection, decision.Reason);
        Assert.Contains("HTTP 404", decision.Explanation, StringComparison.Ordinal);
        Assert.Contains("ollama list", decision.Explanation, StringComparison.Ordinal);
        Assert.Contains("--model", decision.Explanation, StringComparison.Ordinal);
        Assert.Contains("已回退到规则排序", decision.Explanation, StringComparison.Ordinal);
        Assert.Contains("qwen2.5:0.5b", decision.AdvisorExplanation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OllamaTimeout_KeepsRuleWinner()
    {
        var handler = new RecordingHandler("{}", delay: TimeSpan.FromSeconds(5));
        using var advisor = new OllamaLoopbackAdvisor(handler: handler);
        var engine = new DispatchEngine(DispatchPolicy.Default with { AdvisorTimeout = TimeSpan.FromMilliseconds(40) }, advisor);
        var started = System.Diagnostics.Stopwatch.StartNew();
        var decision = await engine.EvaluateAsync(SampleSnapshots.FastLane());
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(2));
        Assert.Equal(AdvisorOutcome.TimedOut, decision.AdvisorOutcome);
        Assert.Equal("B-0020", decision.Selected!.VehicleId);
    }

    [Fact]
    public void CloudBaseUrl_IsRejectedBeforeTheHandlerRuns()
    {
        var handler = new RecordingHandler("{}");
        Assert.Throws<ArgumentException>(() => new OllamaLoopbackAdvisor(new OllamaAdvisorOptions
        {
            BaseUrl = "https://api.openai.com"
        }, handler));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("{\"order\":[\"buffer:BUF-02:B-0012\"],\"explanation\":\"最小序号优先\"}")]
    [InlineData("```json\n{\"order\":[\"A\"],\"explanation\":\"ok\"}\n```")]
    public void Parser_AcceptsJson(string text)
    {
        var parsed = AdvisorResponseParser.Parse(text);
        Assert.True(parsed.Success);
        Assert.NotEmpty(parsed.Order);
    }

    [Theory]
    [InlineData("")]
    [InlineData("没有 JSON")]
    [InlineData("{\"order\":[]}")]
    [InlineData("{\"order\":[1]}")]
    public void Parser_RejectsGarbage(string text)
    {
        Assert.False(AdvisorResponseParser.Parse(text).Success);
    }

    [Fact]
    public async Task LlamaSharp_MissingModel_ReturnsNull()
    {
        var advisor = new LlamaSharpAdvisor(new LlamaSharpAdvisorOptions
        {
            ModelPath = Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".gguf"),
            GpuLayerCount = 4
        });
        Assert.Equal(0, advisor.AppliedGpuLayerCount);
        Assert.Contains("GpuLayerCount=0", advisor.GpuLayerWarning);
        var advice = await advisor.AdviseAsync(await AdviceContextAsync(), CancellationToken.None);
        Assert.Null(advice);
        Assert.Equal("模型文件不存在", advisor.Status);
    }

    [Fact]
    public async Task Dataset_MatchesRulesAndCommittedFiles()
    {
        var records = await AdvisorDatasetWriter.BuildAsync();
        Assert.Equal(DatasetDefaults.Count, records.Count);
        var explanations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            var parsed = AdvisorResponseParser.Parse(record.Assistant);
            Assert.True(parsed.Success);
            Assert.Equal(record.RuleOrder, parsed.Order);
            Assert.InRange(record.Explanation.Length, 1, 40);
            explanations.Add(record.Explanation);
            var positions = record.DisplayOrder.Select(id => record.User.IndexOf("- " + id + " ", StringComparison.Ordinal)).ToArray();
            Assert.All(positions, index => Assert.True(index >= 0));
            Assert.True(positions.Zip(positions.Skip(1), (left, right) => left < right).All(ok => ok));
        }

        Assert.DoesNotContain("B-0001", records[0].User, StringComparison.Ordinal);
        Assert.True(explanations.Count >= 3);

        var root = RepoRoot();
        await AssertFileAsync(Path.Combine(root, "finetune", "data", "paint_queue_advisor.jsonl"), AdvisorDatasetWriter.SerializeAsync());
        await AssertFileAsync(Path.Combine(root, "finetune", "data", "plant_overrides.sample.jsonl"), AdvisorDatasetWriter.SerializePlantOverridesAsync());
        AssertText(
            Path.Combine(root, "finetune", "Modelfile"),
            AdvisorDatasetWriter.RenderModelfile("./paint-advisor-q4_k_m.gguf"));
        AssertText(
            Path.Combine(root, "scripts", "ollama", "Modelfile"),
            AdvisorDatasetWriter.RenderModelfile("../../models/qwen2.5-1.5b-instruct-q4_k_m.gguf"));
    }

    [Fact]
    public async Task PlantOverride_ReordersInsideTheRealTopWindow()
    {
        var records = await AdvisorDatasetWriter.BuildPlantOverridesAsync();
        var swapped = AdvisorResponseParser.Parse(records[0].Assistant);
        var kept = AdvisorResponseParser.Parse(records[1].Assistant);
        Assert.False(records[0].DisplayOrder.SequenceEqual(swapped.Order));
        Assert.True(records[1].DisplayOrder.SequenceEqual(kept.Order));
        Assert.Equal(records[0].DisplayOrder.Last(), swapped.Order[0]);
    }

    [Fact]
    public void CoreLibrary_DoesNotReferenceLlamaSharpOrHttpClient()
    {
        var root = RepoRoot();
        var core = Path.Combine(root, "src", "PaintQueueLocalAi");
        var csproj = File.ReadAllText(Path.Combine(core, "PaintQueueLocalAi.csproj"));
        Assert.DoesNotContain("LLamaSharp", csproj, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PackageReference", csproj, StringComparison.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(core, "*.cs", SearchOption.AllDirectories))
        {
            Assert.DoesNotContain("HttpClient", File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    private static async Task<LlmAdviceRequest> AdviceContextAsync()
    {
        var decision = await new DispatchEngine().EvaluateAsync(SampleSnapshots.MinSequence());
        var top = decision.RankedCandidates.Take(DispatchPolicy.Default.AdvisorTopK).ToArray();
        return new LlmAdviceRequest
        {
            Dispatch = SampleSnapshots.MinSequence(),
            TopCandidates = top,
            Policy = DispatchPolicy.Default,
            AppliedMinSequenceWeight = decision.AppliedMinSequenceWeight,
            RuleSummary = "非紧急。"
        };
    }

    private static async Task AssertFileAsync(string path, Task<string> expectedTask)
    {
        var expected = await expectedTask;
        AssertText(path, expected);
    }

    private static void AssertText(string path, string expected)
    {
        var actual = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(expected, actual);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PaintQueueLocalAi.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("找不到仓库根目录。");
    }

    private static string JsonEscape(string value) => System.Text.Json.JsonSerializer.Serialize(value);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _response;
        private readonly TimeSpan _delay;
        private readonly HttpStatusCode _status;

        public RecordingHandler(string response, HttpStatusCode status = HttpStatusCode.OK, TimeSpan delay = default)
        {
            _response = response;
            _status = status;
            _delay = delay;
        }

        public int Calls { get; private set; }

        public Uri? LastUri { get; private set; }

        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastUri = request.RequestUri;
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            if (_delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, cancellationToken);
            }

            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_response, Encoding.UTF8, "application/json")
            };
        }
    }
}
