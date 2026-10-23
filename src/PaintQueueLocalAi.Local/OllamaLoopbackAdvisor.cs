using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PaintQueueLocalAi;

namespace PaintQueueLocalAi.Local;

public static class LoopbackAddressGuard
{
    public static Uri Ensure(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || baseUrl.Length > 200)
        {
            throw new ArgumentException("顾问地址无效。只允许本机回环，例如 http://127.0.0.1:11434。", nameof(baseUrl));
        }

        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("顾问地址必须是绝对 URI，且只能指向本机回环。", nameof(baseUrl));
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("顾问地址只允许 http 或 https。", nameof(baseUrl));
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException("顾问地址不能包含用户信息。", nameof(baseUrl));
        }

        if (!IsLoopbackHost(uri.Host))
        {
            throw new ArgumentException("顾问地址必须是本机回环（127.0.0.1、localhost 或 ::1），拒绝云端和局域网。", nameof(baseUrl));
        }

        if (uri.AbsolutePath is not "/")
        {
            throw new ArgumentException("顾问地址不能包含路径，只填写源站，例如 http://127.0.0.1:11434。", nameof(baseUrl));
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException("顾问地址不能包含查询或片段。", nameof(baseUrl));
        }

        return uri;
    }

    public static bool IsLoopbackHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var ipText = host.Trim('[', ']');
        return IPAddress.TryParse(ipText, out var ip) && IPAddress.IsLoopback(ip);
    }
}

public sealed record OllamaAdvisorOptions
{
    public string BaseUrl { get; init; } = "http://127.0.0.1:11434";

    public string Model { get; init; } = "qwen2.5:1.5b";

    public int MaxTokens { get; init; } = 80;

    public int NumCtx { get; init; } = 2048;

    public double Temperature { get; init; } = 0.1;

    public string KeepAlive { get; init; } = "30m";
}

/// <summary>
/// 只向本机 Ollama 发请求。关闭代理和自动重定向，避免回环地址被送到外网。
/// </summary>
public sealed class OllamaLoopbackAdvisor : ILlmAdvisor, IAdvisorWarmup, ILocalAdvisorStatus, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly Uri _chatUri;
    private readonly OllamaAdvisorOptions _options;
    private readonly bool _disposeClient;

    public OllamaLoopbackAdvisor(OllamaAdvisorOptions? options = null, HttpMessageHandler? handler = null)
    {
        _options = options ?? new OllamaAdvisorOptions();
        if (string.IsNullOrWhiteSpace(_options.Model))
        {
            throw new ArgumentException("Ollama 模型名不能为空。", nameof(options));
        }

        var origin = LoopbackAddressGuard.Ensure(_options.BaseUrl);
        _chatUri = new Uri(origin.GetLeftPart(UriPartial.Authority) + "/api/chat");
        if (handler is null)
        {
            _http = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false
            });
            _disposeClient = true;
        }
        else
        {
            _http = new HttpClient(handler, disposeHandler: false);
            _disposeClient = true;
        }

        _http.Timeout = Timeout.InfiniteTimeSpan;
        Status = "未预热";
    }

    public string Status { get; private set; }

    public async Task WarmUpAsync(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            _ = await ChatAsync("回复一个字：好", 1, cts.Token).ConfigureAwait(false);
            Status = "已预热";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Status = "预热失败: " + ex.GetType().Name;
        }
    }

    public async ValueTask<AdvisorSuggestion?> AdviseAsync(AdvisorContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var user = AdvisorPrompt.BuildUser(context.Booth, context.TopCandidates);
        var content = await ChatAsync(user, _options.MaxTokens, cancellationToken).ConfigureAwait(false);
        var parsed = AdvisorResponseParser.Parse(content);
        if (!parsed.Success)
        {
            throw new FormatException(parsed.Error);
        }

        Status = "就绪";
        return new AdvisorSuggestion(parsed.Order, parsed.Explanation);
    }

    public void Dispose()
    {
        if (_disposeClient)
        {
            _http.Dispose();
        }
    }

    private async Task<string> ChatAsync(string user, int maxTokens, CancellationToken cancellationToken)
    {
        var payload = new ChatRequest
        {
            Model = _options.Model,
            Stream = false,
            Format = "json",
            KeepAlive = _options.KeepAlive,
            Messages = new[]
            {
                new ChatMessage("system", AdvisorPrompt.SystemPrompt),
                new ChatMessage("user", user)
            },
            Options = new ChatGenOptions
            {
                Temperature = _options.Temperature,
                NumPredict = maxTokens,
                NumCtx = _options.NumCtx
            }
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, _chatUri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var status = (int)response.StatusCode;
        if (status is >= 300 and < 400)
        {
            throw new HttpRequestException("拒绝跟随 Ollama 重定向。");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException("Ollama HTTP " + status);
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.String)
        {
            throw new FormatException("ollama-content");
        }

        return content.GetString() ?? "";
    }

    private sealed record ChatRequest
    {
        public required string Model { get; init; }

        public bool Stream { get; init; }

        public required string Format { get; init; }

        [JsonPropertyName("keep_alive")]
        public required string KeepAlive { get; init; }

        public required IReadOnlyList<ChatMessage> Messages { get; init; }

        public required ChatGenOptions Options { get; init; }
    }

    private sealed record ChatMessage(string Role, string Content);

    private sealed record ChatGenOptions
    {
        public double Temperature { get; init; }

        [JsonPropertyName("num_predict")]
        public int NumPredict { get; init; }

        [JsonPropertyName("num_ctx")]
        public int NumCtx { get; init; }
    }
}
