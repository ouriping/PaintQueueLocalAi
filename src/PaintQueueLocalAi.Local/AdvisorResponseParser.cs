using System.Text.Json;

namespace PaintQueueLocalAi.Local;

public readonly record struct AdvisorParseResult(bool Success, IReadOnlyList<string> Order, string? Explanation, string? Error)
{
    public static AdvisorParseResult Ok(IReadOnlyList<string> order, string? explanation)
        => new(true, order, explanation, null);

    public static AdvisorParseResult Fail(string error)
        => new(false, Array.Empty<string>(), null, error);
}

public static class AdvisorResponseParser
{
    public static AdvisorParseResult Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return AdvisorParseResult.Fail("empty");
        }

        var json = ExtractObject(text);
        if (json is null)
        {
            return AdvisorParseResult.Fail("no-object");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("order", out var orderElement)
                || orderElement.ValueKind != JsonValueKind.Array)
            {
                return AdvisorParseResult.Fail("order");
            }

            var order = new List<string>();
            foreach (var item in orderElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    return AdvisorParseResult.Fail("order-item");
                }

                var id = item.GetString();
                if (string.IsNullOrWhiteSpace(id))
                {
                    return AdvisorParseResult.Fail("order-item");
                }

                order.Add(id);
            }

            if (order.Count == 0)
            {
                return AdvisorParseResult.Fail("order-empty");
            }

            string? explanation = null;
            if (document.RootElement.TryGetProperty("explanation", out var explanationElement)
                && explanationElement.ValueKind == JsonValueKind.String)
            {
                explanation = explanationElement.GetString();
            }

            return AdvisorParseResult.Ok(order, explanation);
        }
        catch (JsonException)
        {
            return AdvisorParseResult.Fail("json");
        }
    }

    private static string? ExtractObject(string text)
    {
        var trimmed = text.Trim().TrimStart('\uFEFF');
        var fence = trimmed.IndexOf("```", StringComparison.Ordinal);
        if (fence >= 0)
        {
            var startContent = trimmed.IndexOf('\n', fence);
            var endFence = trimmed.IndexOf("```", fence + 3, StringComparison.Ordinal);
            if (startContent >= 0 && endFence > startContent)
            {
                trimmed = trimmed.Substring(startContent + 1, endFence - startContent - 1);
            }
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        return trimmed.Substring(start, end - start + 1);
    }
}
