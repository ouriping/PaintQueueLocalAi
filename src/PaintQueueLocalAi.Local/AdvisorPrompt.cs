using System.Globalization;
using System.Text;
using PaintQueueLocalAi;

namespace PaintQueueLocalAi.Local;

public static class AdvisorPrompt
{
    public const string SystemPrompt =
        "你是涂装缓存放行顾问。只能重排用户给出的候选编号，不能新增车辆，也不能建议解除联锁。只输出一行 JSON：{\"order\":[\"候选编号\"],\"explanation\":\"不超过40字\"}。若理由不足，保持输入顺序。";

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
        builder.Append("候选(只能重排这些编号):\n");
        foreach (var candidate in displayOrder)
        {
            builder.Append("- ").Append(candidate.CandidateId).Append(' ');
            builder.Append("车号=").Append(candidate.VehicleId);
            builder.Append(" 序号=").Append(candidate.SequenceNumber.ToString(CultureInfo.InvariantCulture));
            builder.Append(" 来源=").Append(candidate.Origin == CandidateOrigin.FastLaneA
                ? "A口快车道"
                : "缓存" + candidate.QueueId);
            builder.Append(" 颜色=").Append(string.IsNullOrEmpty(candidate.ColorCode) ? "-" : candidate.ColorCode);
            builder.Append(" 层级=").Append(candidate.PriorityTier.ToString(CultureInfo.InvariantCulture));
            builder.Append(" 填充=").Append(candidate.FillRatio.ToString("0.##", CultureInfo.InvariantCulture));
            builder.Append(" 得分=").Append(candidate.Score.ToString("0.##", CultureInfo.InvariantCulture));
            builder.Append('\n');
        }

        return builder.ToString().TrimEnd();
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
