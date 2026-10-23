using System.Text.Json;
using PaintQueueLocalAi;

namespace PaintQueueLocalAi.Local;

public readonly record struct AdvisorParseResult(bool Success, IReadOnlyList<string> Order, string? Explanation, string? Error)
{
    public static AdvisorParseResult Ok(IReadOnlyList<string> order, string? explanation)
        => new(true, order, explanation, null);

    public static AdvisorParseResult Fail(string error, IReadOnlyList<string>? order = null, string? explanation = null)
        => new(false, order ?? Array.Empty<string>(), explanation, error);
}

public static class AdvisorResponseParser
{
    private static readonly string[] OrderNames =
    {
        "order", "orders", "ranking", "reorder", "ids", "candidates", "candidateIds", "candidate_ids", "排序", "顺序"
    };

    private static readonly string[] ExplanationNames =
    {
        "explanation", "reason", "why", "explain", "说明", "理由"
    };

    private static readonly string[] IdNames = { "candidateId", "candidate_id", "id", "candidate", "编号" };

    private static readonly char[] Separators = { ',', '，', '、', ';', '；', '|', '\n', '\r', '\t', ' ' };

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    public static AdvisorParseResult Parse(string? text, IReadOnlyCollection<string>? allowedCandidateIds = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return AdvisorParseResult.Fail("empty");
        }

        var allowed = NormalizeAllowed(allowedCandidateIds);
        AdvisorParseResult? failure = null;
        foreach (var slice in JsonSlices(text))
        {
            var document = TryParse(slice);
            if (document is null)
            {
                continue;
            }

            using (document)
            {
                var result = Read(document.RootElement, allowed);
                if (result.Success)
                {
                    return result;
                }

                failure ??= result;
            }
        }

        return failure ?? AdvisorParseResult.Fail("no-object");
    }

    /// <summary>
    /// Structural failures throw. Ids that are not real candidates are returned so the engine records InvalidReorder.
    /// </summary>
    public static LlmAdvice ToAdvice(AdvisorParseResult parsed)
    {
        if (parsed.Success || (parsed.Order.Count > 0 && parsed.Error is "unknown-id" or "ambiguous-id"))
        {
            return new LlmAdvice
            {
                ReorderedCandidateIds = parsed.Order,
                Explanation = parsed.Explanation
            };
        }

        throw new FormatException(parsed.Error ?? "invalid");
    }

    private static string[]? NormalizeAllowed(IReadOnlyCollection<string>? allowedCandidateIds)
    {
        if (allowedCandidateIds is null || allowedCandidateIds.Count == 0)
        {
            return null;
        }

        var allowed = allowedCandidateIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return allowed.Length == 0 ? null : allowed;
    }

    private static AdvisorParseResult Read(JsonElement root, string[]? allowed)
    {
        if (!TryLocate(root, out var orderElement, out var owner))
        {
            return AdvisorParseResult.Fail("order");
        }

        if (!TryReadTokens(orderElement, out var tokens, out var error))
        {
            return AdvisorParseResult.Fail(error);
        }

        return Bind(tokens, ReadExplanation(root, owner), allowed);
    }

    private static AdvisorParseResult Bind(List<string> raw, string? explanation, string[]? allowed)
    {
        if (raw.Count == 0)
        {
            return AdvisorParseResult.Fail("order-empty");
        }

        if (allowed is null)
        {
            var plain = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in raw)
            {
                if (token.Length == 0 || !seen.Add(token))
                {
                    continue;
                }

                plain.Add(token);
            }

            return plain.Count == 0
                ? AdvisorParseResult.Fail("order-empty")
                : AdvisorParseResult.Ok(plain, explanation);
        }

        var canonical = new List<string>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in raw)
        {
            var resolved = Resolve(token, allowed, used);
            if (resolved.Kind == ResolveKind.Duplicate)
            {
                continue;
            }

            if (resolved.Kind != ResolveKind.Found || resolved.Id is null)
            {
                var error = resolved.Kind == ResolveKind.Ambiguous ? "ambiguous-id" : "unknown-id";
                return AdvisorParseResult.Fail(error, raw, explanation);
            }

            used.Add(resolved.Id);
            canonical.Add(resolved.Id);
        }

        return canonical.Count == 0
            ? AdvisorParseResult.Fail("order-empty")
            : AdvisorParseResult.Ok(canonical, explanation);
    }

    private static ResolveResult Resolve(string token, IReadOnlyList<string> allowed, HashSet<string> used)
    {
        var exact = Match(allowed, used, id => string.Equals(id, token, StringComparison.Ordinal));
        if (exact.Kind != ResolveKind.Unknown)
        {
            return exact;
        }

        var ignoreCase = Match(allowed, used, id => string.Equals(id, token, StringComparison.OrdinalIgnoreCase));
        if (ignoreCase.Kind != ResolveKind.Unknown)
        {
            return ignoreCase;
        }

        var vehicle = Match(allowed, used, id =>
        {
            var vehicleId = VehicleIdOf(id);
            return vehicleId is not null && string.Equals(vehicleId, token, StringComparison.Ordinal);
        });
        if (vehicle.Kind != ResolveKind.Unknown)
        {
            return vehicle;
        }

        if (token.Contains(':') && token.Length >= 8)
        {
            var prefix = Match(allowed, used, id => IsBoundedPrefix(id, token));
            if (prefix.Kind != ResolveKind.Unknown)
            {
                return prefix;
            }
        }

        return ResolveResult.Unknown();
    }

    private static ResolveResult Match(IReadOnlyList<string> allowed, HashSet<string> used, Func<string, bool> predicate)
    {
        string? found = null;
        var usedHit = false;
        foreach (var id in allowed)
        {
            if (!predicate(id))
            {
                continue;
            }

            if (used.Contains(id))
            {
                usedHit = true;
                continue;
            }

            if (found is not null)
            {
                return ResolveResult.Ambiguous();
            }

            found = id;
        }

        if (found is not null)
        {
            return ResolveResult.Found(found);
        }

        return usedHit ? ResolveResult.Duplicate() : ResolveResult.Unknown();
    }

    private static bool IsBoundedPrefix(string candidateId, string token)
        => candidateId.StartsWith(token, StringComparison.Ordinal)
            && (candidateId.Length == token.Length || candidateId[token.Length] == ':');

    private static string? VehicleIdOf(string candidateId)
    {
        var colon = candidateId.LastIndexOf(':');
        if (colon < 0 || colon >= candidateId.Length - 1)
        {
            return null;
        }

        return candidateId[(colon + 1)..];
    }

    private static bool TryLocate(JsonElement element, out JsonElement orderElement, out JsonElement owner)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            orderElement = element;
            owner = default;
            return true;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            if (TryNamed(element, OrderNames, out orderElement))
            {
                owner = element;
                return true;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Object
                    && TryNamed(property.Value, OrderNames, out orderElement))
                {
                    owner = property.Value;
                    return true;
                }
            }
        }

        orderElement = default;
        owner = default;
        return false;
    }

    private static bool TryReadTokens(JsonElement orderElement, out List<string> tokens, out string error)
    {
        tokens = new List<string>();
        if (orderElement.ValueKind == JsonValueKind.String)
        {
            AddSplit(orderElement.GetString(), tokens);
            error = tokens.Count == 0 ? "order-empty" : "";
            return tokens.Count > 0;
        }

        if (orderElement.ValueKind != JsonValueKind.Array)
        {
            error = "order";
            return false;
        }

        foreach (var item in orderElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var before = tokens.Count;
                AddSplit(item.GetString(), tokens);
                if (tokens.Count == before)
                {
                    error = "order-item";
                    return false;
                }
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                if (!TryObjectId(item, out var id))
                {
                    error = "order-item";
                    return false;
                }

                tokens.Add(NormalizeToken(id));
            }
            else
            {
                error = "order-item";
                return false;
            }
        }

        error = tokens.Count == 0 ? "order-empty" : "";
        return tokens.Count > 0;
    }

    private static bool TryObjectId(JsonElement item, out string id)
    {
        if (TryNamed(item, IdNames, out var value) && value.ValueKind == JsonValueKind.String)
        {
            id = value.GetString() ?? "";
            return !string.IsNullOrWhiteSpace(id);
        }

        id = "";
        return false;
    }

    private static void AddSplit(string? text, List<string> tokens)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        foreach (var part in text.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var token = NormalizeToken(part);
            if (token.Length > 0)
            {
                tokens.Add(token);
            }
        }
    }

    private static string NormalizeToken(string raw)
    {
        var token = raw.Trim().Trim('`', '"', '\'', '「', '」', '。', '，', ',', ' ', '\t');
        return token.Replace('：', ':').Trim();
    }

    private static string? ReadExplanation(JsonElement root, JsonElement owner)
    {
        if (TryString(owner, out var local))
        {
            return local;
        }

        return TryString(root, out var top) ? top : null;
    }

    private static bool TryString(JsonElement element, out string? text)
    {
        text = null;
        if (element.ValueKind != JsonValueKind.Object || !TryNamed(element, ExplanationNames, out var value))
        {
            return false;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        text = value.GetString();
        return true;
    }

    private static bool TryNamed(JsonElement element, string[] names, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            foreach (var name in names)
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static IEnumerable<string> JsonSlices(string text)
    {
        var trimmed = text.Trim().TrimStart('\uFEFF');
        for (var i = 0; i < trimmed.Length; i++)
        {
            var ch = trimmed[i];
            if (ch != '{' && ch != '[')
            {
                continue;
            }

            var end = FindBalanced(trimmed, i);
            if (end <= i)
            {
                continue;
            }

            yield return trimmed.Substring(i, end - i + 1);
        }
    }

    private static int FindBalanced(string text, int start)
    {
        var opener = text[start];
        var closer = opener == '{' ? '}' : ']';
        var depth = 0;
        var inString = false;
        var escape = false;
        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];
            if (inString)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escape = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == opener)
            {
                depth++;
            }
            else if (ch == closer)
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static JsonDocument? TryParse(string slice)
    {
        if (TryParseCore(slice, out var document))
        {
            return document;
        }

        if (!slice.Contains('"') && slice.Contains('\''))
        {
            if (TryParseCore(slice.Replace('\'', '"'), out document))
            {
                return document;
            }
        }

        return null;
    }

    private static bool TryParseCore(string slice, out JsonDocument? document)
    {
        try
        {
            document = JsonDocument.Parse(slice, DocumentOptions);
            return true;
        }
        catch (JsonException)
        {
            document = null;
            return false;
        }
    }

    private enum ResolveKind
    {
        Found,
        Duplicate,
        Unknown,
        Ambiguous
    }

    private readonly record struct ResolveResult(ResolveKind Kind, string? Id)
    {
        public static ResolveResult Found(string id) => new(ResolveKind.Found, id);

        public static ResolveResult Duplicate() => new(ResolveKind.Duplicate, null);

        public static ResolveResult Unknown() => new(ResolveKind.Unknown, null);

        public static ResolveResult Ambiguous() => new(ResolveKind.Ambiguous, null);
    }
}
