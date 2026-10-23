using System.Text;
using System.Text.Json;

namespace PaintQueueLocalAi;

public readonly record struct AdvisorParseResult(
    bool Success,
    IReadOnlyList<string> Order,
    string? Explanation,
    string? Error)
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
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return AdvisorParseResult.Fail("not-object");
            }

            if (!doc.RootElement.TryGetProperty("order", out var orderElement)
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
            if (doc.RootElement.TryGetProperty("explanation", out var explanationElement)
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

public static class AdvisorText
{
    public static string? SanitizeExplanation(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var sb = new StringBuilder(Math.Min(text.Length, AdvisorLimits.MaxExplanationChars));
        var pendingSpace = false;
        foreach (var ch in text)
        {
            if (char.IsControl(ch) || ch == '\u00A0')
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                if (sb.Length + 1 > AdvisorLimits.MaxExplanationChars)
                {
                    break;
                }

                sb.Append(' ');
                pendingSpace = false;
            }

            if (sb.Length + 1 > AdvisorLimits.MaxExplanationChars)
            {
                break;
            }

            sb.Append(ch);
        }

        var value = sb.ToString().Trim();
        return value.Length == 0 ? null : value;
    }
}

public static class AdvisorOrder
{
    public static bool TryApply(
        IReadOnlyList<string>? proposed,
        IReadOnlyList<RankedCandidate> ruleOrder,
        out IReadOnlyList<RankedCandidate> applied)
    {
        applied = ruleOrder;
        if (proposed is null || proposed.Count == 0 || ruleOrder.Count == 0)
        {
            return false;
        }

        var byId = new Dictionary<string, RankedCandidate>(ruleOrder.Count, StringComparer.Ordinal);
        foreach (var candidate in ruleOrder)
        {
            byId[candidate.CandidateId] = candidate;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<RankedCandidate>(ruleOrder.Count);
        foreach (var id in proposed)
        {
            if (string.IsNullOrWhiteSpace(id) || !byId.TryGetValue(id, out var candidate))
            {
                continue;
            }

            if (seen.Add(candidate.CandidateId))
            {
                result.Add(candidate);
            }
        }

        if (result.Count == 0)
        {
            return false;
        }

        foreach (var candidate in ruleOrder)
        {
            if (seen.Add(candidate.CandidateId))
            {
                result.Add(candidate);
            }
        }

        applied = result;
        return true;
    }
}
