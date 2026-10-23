using System.Globalization;
using System.Text;
using PaintQueueLocalAi;

namespace PaintQueueLocalAi.Local;

public static class AdvisorPrompt
{
    public const string SystemPrompt =
        "只输出一行 JSON，不要 Markdown，不要前后说明。键只有 order 和 explanation。order 是字符串数组，每项必须原样复制用户列出的候选编号，禁止新增，禁止只写车号。";

    public static string BuildUser(LlmAdviceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return BuildUser(request, request.TopCandidates);
    }

    public static string BuildUser(LlmAdviceRequest request, IReadOnlyList<CandidateScore> displayOrder)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(displayOrder);
        var builder = new StringBuilder();
        builder.Append(request.RuleSummary).Append('\n');
        builder.Append("候选编号:\n");
        foreach (var candidate in displayOrder)
        {
            builder.Append("- ").Append(candidate.CandidateId).Append(' ');
            builder.Append("序号=").Append(candidate.SequenceNumber.ToString(CultureInfo.InvariantCulture));
            builder.Append(" 得分=").Append(candidate.Score.ToString("0.##", CultureInfo.InvariantCulture));
            builder.Append(" 层级=").Append(candidate.PriorityTier.ToString(CultureInfo.InvariantCulture));
            builder.Append('\n');
        }

        if (request.TopCandidates.Count > 0)
        {
            var explanation = Explain(request.TopCandidates[0], request.Dispatch.IsUrgent);
            builder.Append("只输出一行 JSON。order 只能重排上面的编号。保持规则时原样输出：");
            builder.Append(ExampleJson(request.TopCandidates, explanation));
        }

        return builder.ToString().TrimEnd();
    }

    private static string ExampleJson(IReadOnlyList<CandidateScore> ranked, string explanation)
    {
        var builder = new StringBuilder();
        builder.Append("{\"order\":[");
        for (var i = 0; i < ranked.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append('"').Append(ranked[i].CandidateId).Append('"');
        }

        var safeExplanation = explanation.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        builder.Append("],\"explanation\":\"").Append(safeExplanation).Append("\"}");
        return builder.ToString();
    }

    public static string BuildRawPrompt(string system, string user)
    {
        return "<|im_start|>system\n"
            + system
            + "<|im_end|>\n<|im_start|>user\n"
            + user
            + "<|im_end|>\n<|im_start|>assistant\n";
    }

    public static string Explain(CandidateScore selected, bool urgent)
    {
        ArgumentNullException.ThrowIfNull(selected);
        if (selected.Origin == CandidateOrigin.FastLaneA)
        {
            return "A口快车道直放";
        }

        if (urgent && selected.FillRatio >= 0.8m)
        {
            return "紧急时先放满库";
        }

        if (selected.SequenceComponent >= selected.LoadBalanceComponent)
        {
            return "最小序号优先";
        }

        return "满库车道优先";
    }
}
