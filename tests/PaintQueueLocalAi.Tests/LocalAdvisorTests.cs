using System.Net;
using System.Text;
using PaintQueueLocalAi;
using PaintQueueLocalAi.LlamaSharp;
using PaintQueueLocalAi.Local;

namespace PaintQueueLocalAi.Tests;

public class LocalAdvisorTests
{
    [Theory]
    [InlineData("http://127.0.0.1:11434")]
    [InlineData("http://127.0.0.1:11434/")]
    [InlineData("http://localhost:11434")]
    [InlineData("http://[::1]:11434")]
    [InlineData("https://127.0.0.1:9")]
    public void LoopbackGuard_AllowsLoopbackOrigin(string url)
    {
        var uri = LoopbackAddressGuard.Ensure(url);
        Assert.True(LoopbackAddressGuard.IsLoopbackHost(uri.Host));
    }

    [Theory]
    [InlineData("http://192.168.1.10:11434")]
    [InlineData("https://api.openai.com/v1")]
    [InlineData("http://10.0.0.5")]
    [InlineData("http://example.com")]
    [InlineData("file:///models/a.gguf")]
    [InlineData("http://user:pass@127.0.0.1:11434")]
    [InlineData("http://127.0.0.1:11434/api")]
    [InlineData("http://127.0.0.1:11434?x=1")]
    public void LoopbackGuard_RejectsNonLoopback(string url)
    {
        Assert.Throws<ArgumentException>(() => LoopbackAddressGuard.Ensure(url));
    }

    [Fact]
    public async Task OllamaAdvisor_PostsOnlyToLoopbackChat()
    {
        var handler = new RecordingHandler("""{"message":{"role":"assistant","content":"{\"order\":[\"W-URG\",\"K-LATE\"],\"explanation\":\"同色紧急件优先\"}"}}""");
        using var advisor = new OllamaLoopbackAdvisor(new OllamaAdvisorOptions
        {
            BaseUrl = "http://127.0.0.1:11434",
            Model = "qwen2.5:1.5b"
        }, handler);
        var suggestion = await advisor.AdviseAsync(Context(), CancellationToken.None);
        Assert.NotNull(suggestion);
        Assert.Equal(new[] { "W-URG", "K-LATE" }, suggestion!.OrderedCandidateIds);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(new Uri("http://127.0.0.1:11434/api/chat"), handler.LastUri);
        Assert.Contains("\"model\":\"qwen2.5:1.5b\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"num_predict\":80", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"keep_alive\":\"30m\"", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("openai", handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("W-HOLD", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OllamaAdvisor_InvalidModelJson_ThrowsFormatException()
    {
        var handler = new RecordingHandler("""{"message":{"content":"今天喷白色"}}""");
        using var advisor = new OllamaLoopbackAdvisor(handler: handler);
        await Assert.ThrowsAsync<FormatException>(() => advisor.AdviseAsync(Context(), CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task OllamaAdvisor_RedirectIsNotFollowed()
    {
        var handler = new RecordingHandler("", HttpStatusCode.Redirect);
        using var advisor = new OllamaLoopbackAdvisor(handler: handler);
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => advisor.AdviseAsync(Context(), CancellationToken.None).AsTask());
        Assert.Contains("重定向", ex.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void OllamaAdvisor_RejectsCloudBaseUrlBeforeAnyRequest()
    {
        var handler = new RecordingHandler("{}");
        Assert.Throws<ArgumentException>(() => new OllamaLoopbackAdvisor(new OllamaAdvisorOptions
        {
            BaseUrl = "https://api.openai.com"
        }, handler));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task OllamaFallback_InsideEngine_KeepsRules()
    {
        var handler = new RecordingHandler("""{"message":{"content":"坏的"}}""");
        using var advisor = new OllamaLoopbackAdvisor(handler: handler);
        var sample = SampleQueues.MorningShift();
        var decision = await new DispatchEngine(advisor: advisor).DispatchAsync(sample.Booth, sample.Jobs);
        Assert.Equal(AdvisorFallbackReason.Invalid, decision.FallbackReason);
        Assert.Equal("W-URG", decision.SelectedJobId);
    }

    [Fact]
    public async Task LlamaSharp_MissingOrColdModel_ReturnsNullWithoutLoading()
    {
        var missing = new LlamaSharpAdvisor(new LlamaSharpAdvisorOptions
        {
            ModelPath = Path.Combine(Path.GetTempPath(), "paint-queue-missing-" + Guid.NewGuid().ToString("N") + ".gguf"),
            GpuLayerCount = 12
        });
        Assert.Equal(0, missing.AppliedGpuLayerCount);
        Assert.Contains("GpuLayerCount=0", missing.GpuLayerWarning);
        var suggestion = await missing.AdviseAsync(Context(), CancellationToken.None);
        Assert.Null(suggestion);
        Assert.Equal("模型文件不存在", missing.Status);

        var coldPath = Path.Combine(Path.GetTempPath(), "paint-queue-empty-" + Guid.NewGuid().ToString("N") + ".gguf");
        await File.WriteAllBytesAsync(coldPath, new byte[] { 1, 2, 3 });
        try
        {
            var cold = new LlamaSharpAdvisor(new LlamaSharpAdvisorOptions { ModelPath = coldPath });
            var coldSuggestion = await cold.AdviseAsync(Context(), CancellationToken.None);
            Assert.Null(coldSuggestion);
            Assert.Equal("模型尚未预热", cold.Status);
        }
        finally
        {
            File.Delete(coldPath);
        }
    }

    private static AdvisorContext Context()
    {
        var sample = SampleQueues.MorningShift();
        var top = new DispatchEngine().Preview(sample.Booth, sample.Jobs).Ranked.Take(4).ToArray();
        return new AdvisorContext(sample.Booth, top, TimeSpan.FromMilliseconds(200));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _response;
        private readonly HttpStatusCode _status;

        public RecordingHandler(string response, HttpStatusCode status = HttpStatusCode.OK)
        {
            _response = response;
            _status = status;
        }

        public int Calls { get; private set; }

        public Uri? LastUri { get; private set; }

        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastUri = request.RequestUri;
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_response, Encoding.UTF8, "application/json")
            };
        }
    }
}
