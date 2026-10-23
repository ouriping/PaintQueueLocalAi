using System.Globalization;
using System.Text;

namespace PaintQueueLocalAi;

internal static class DecisionText
{
    private const int MaxAdvisorChars = 2000;

    public static string? Cap(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        return trimmed.Length <= MaxAdvisorChars ? trimmed : trimmed[..MaxAdvisorChars] + "…";
    }

    public static string Safety(DispatchRequest request, SafetyHoldKind hold)
    {
        var reason = hold switch
        {
            SafetyHoldKind.EStop => "急停已动作",
            SafetyHoldKind.ConveyorNotReady => "输送链未就绪",
            SafetyHoldKind.SharedResourceBlocked => $"共享资源 {request.SharedResourceId} 被联锁封锁",
            _ => "安全联锁不允许放行"
        };

        var message = string.IsNullOrWhiteSpace(request.Safety.Message)
            ? string.Empty
            : " " + request.Safety.Message.Trim();
        return $"禁止放行：{reason}。本次未评估队列，也未询问本地顾问。{message}".Trim();
    }

    public static string NoCandidate(DispatchRequest request, int emptyQueues, int blockedQueues, decimal appliedWeight)
    {
        var urgency = UrgencySentence(request, appliedWeight);
        return $"未放行：没有可放行的缓存车头，A 口快车道也没有可直放车辆。{urgency}已查看 {request.Queues.Count} 条缓存队列（空 {emptyQueues}，封锁 {blockedQueues}）。";
    }

    public static string Release(
        DispatchRequest request,
        decimal appliedWeight,
        IReadOnlyList<CandidateScore> ranked,
        CandidateScore selected,
        string ruleWinnerId,
        int emptyQueues,
        int blockedQueues,
        AdvisorOutcome advisorOutcome,
        bool advisorReordered,
        string? advisorExplanation)
    {
        var builder = new StringBuilder();
        builder.Append("放行 ").Append(selected.VehicleId).Append("（序号 ").Append(selected.SequenceNumber);
        if (!string.IsNullOrWhiteSpace(selected.ColorCode))
        {
            builder.Append('，').Append(selected.ColorCode);
        }

        builder.Append('，').Append(OriginText(selected)).Append("）。");
        builder.Append("缓存只放车头，靠后的车不能越位。");
        builder.Append(UrgencySentence(request, appliedWeight));

        if (selected.Origin == CandidateOrigin.FastLaneA && !advisorReordered)
        {
            var overruled = FirstHigherScore(ranked, selected);
            builder.Append("A 口快车道属于更高优先级，排在所有缓存车头之前。");
            if (overruled is not null)
            {
                builder.Append("缓存候选 ")
                    .Append(overruled.VehicleId)
                    .Append(" 序号 ")
                    .Append(overruled.SequenceNumber)
                    .Append(" 的加权得分 ")
                    .Append(N(overruled.Score))
                    .Append(" 更高，但仍让位于快车道。");
            }
        }

        builder.Append("已并行查看 ")
            .Append(request.Queues.Count)
            .Append(" 条缓存队列（空 ")
            .Append(emptyQueues)
            .Append("，封锁 ")
            .Append(blockedQueues)
            .Append("），可放行候选 ")
            .Append(ranked.Count)
            .Append(" 个。");
        builder.Append(AdvisorSentence(advisorOutcome, advisorReordered, ruleWinnerId, selected, advisorExplanation));
        if (!string.IsNullOrWhiteSpace(advisorExplanation))
        {
            builder.Append("顾问说明：").Append(advisorExplanation);
        }

        builder.AppendLine();
        builder.Append("排序：");
        var shown = Math.Min(ranked.Count, 8);
        for (var i = 0; i < shown; i++)
        {
            var candidate = ranked[i];
            builder.AppendLine();
            builder.Append(candidate.Rank)
                .Append(". ")
                .Append(candidate.VehicleId)
                .Append(' ')
                .Append(OriginText(candidate))
                .Append(" 序号 ")
                .Append(candidate.SequenceNumber)
                .Append(" 层级 ")
                .Append(candidate.PriorityTier)
                .Append(" 得分 ")
                .Append(N(candidate.Score))
                .Append("（序号 ")
                .Append(N(candidate.SequenceComponent))
                .Append(" + 均衡 ")
                .Append(N(candidate.LoadBalanceComponent))
                .Append("）");
        }

        if (ranked.Count > shown)
        {
            builder.AppendLine();
            builder.Append("其余 ").Append(ranked.Count - shown).Append(" 个候选略。");
        }

        return builder.ToString();
    }

    public static string RuleSummary(DispatchRequest request, IReadOnlyList<CandidateScore> top, decimal appliedWeight)
    {
        var builder = new StringBuilder();
        builder.Append(request.IsUrgent ? "紧急要车。" : "非紧急。");
        builder.Append("最小序号权重 ").Append(N(appliedWeight)).Append("。规则排序：");
        for (var i = 0; i < top.Count; i++)
        {
            if (i > 0)
            {
                builder.Append("；");
            }

            var candidate = top[i];
            builder.Append(candidate.CandidateId)
                .Append(" 序号 ")
                .Append(candidate.SequenceNumber)
                .Append(" 得分 ")
                .Append(N(candidate.Score));
        }

        return builder.ToString();
    }

    private static string UrgencySentence(DispatchRequest request, decimal appliedWeight)
    {
        if (!request.IsUrgent)
        {
            return $"本次非紧急，最小序号权重为 {N(appliedWeight)}。";
        }

        return $"本次为紧急要车，最小序号权重已降到 {N(appliedWeight)}，按当前可放行车辆立即决策。";
    }

    private static string OriginText(CandidateScore candidate)
    {
        return candidate.Origin == CandidateOrigin.FastLaneA
            ? "A 口快车道直放"
            : "缓存队列 " + candidate.QueueId;
    }

    private static CandidateScore? FirstHigherScore(IReadOnlyList<CandidateScore> ranked, CandidateScore selected)
    {
        foreach (var candidate in ranked)
        {
            if (candidate.Rank > selected.Rank && candidate.Score > selected.Score)
            {
                return candidate;
            }
        }

        return null;
    }

    private static string AdvisorSentence(
        AdvisorOutcome outcome,
        bool reordered,
        string ruleWinnerId,
        CandidateScore selected,
        string? advisorExplanation)
    {
        return outcome switch
        {
            AdvisorOutcome.NotConsulted => "未询问顾问。",
            AdvisorOutcome.Declined => "未接入本地顾问，规则结果即为放行决定。",
            AdvisorOutcome.TimedOut => "本地顾问超时，已回退到规则排序。",
            AdvisorOutcome.Faulted when IsModelNotFound(advisorExplanation)
                => "本地顾问故障：模型未找到（HTTP 404）。请运行 ollama list，用 --model 传入列表里的名字。已回退到规则排序。",
            AdvisorOutcome.Faulted => "本地顾问故障，已回退到规则排序。",
            AdvisorOutcome.InvalidReorder => "本地顾问重排无效，已回退到规则排序。",
            AdvisorOutcome.Applied when reordered && !string.Equals(ruleWinnerId, selected.CandidateId, StringComparison.Ordinal)
                => $"本地顾问改写了前排候选的顺序，规则首选是 {ruleWinnerId}。",
            AdvisorOutcome.Applied when reordered => "本地顾问调整了名次，放行对象未变。",
            _ => "本地顾问未改写规则排序。"
        };
    }

    private static bool IsModelNotFound(string? explanation)
    {
        if (string.IsNullOrWhiteSpace(explanation))
        {
            return false;
        }

        return explanation.Contains("404", StringComparison.Ordinal)
            || explanation.Contains("model not found", StringComparison.OrdinalIgnoreCase)
            || explanation.Contains("模型未找到", StringComparison.Ordinal);
    }

    private static string N(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
