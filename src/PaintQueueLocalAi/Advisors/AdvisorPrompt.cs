using System.Globalization;
using System.Text;

namespace PaintQueueLocalAi;

public static class AdvisorPrompt
{
    public const string SystemPrompt =
        "你是涂装队列顾问。只能重排用户给出的候选工单号，不能新增、删除或改写工单。安全联锁已由规则处理，不要建议解除联锁。只输出一行 JSON：{\"order\":[\"工单号\"],\"explanation\":\"不超过40字\"}。若理由不足，按分数从高到低排列。";

    public static string Explain(RankedCandidate top)
    {
        ArgumentNullException.ThrowIfNull(top);
        var reasons = top.Reasons;
        bool Has(string reason) => reasons.Contains(reason, StringComparer.Ordinal);

        if (Has(RuleReason.SameColor) && Has(RuleReason.Urgent))
        {
            return "同色紧急件优先";
        }

        if (Has(RuleReason.SameColor) && Has(RuleReason.Overdue))
        {
            return "同色且已逾期，优先";
        }

        if (Has(RuleReason.Urgent) && Has(RuleReason.Overdue))
        {
            return "逾期紧急件优先";
        }

        if (Has(RuleReason.SameColor) && Has(RuleReason.DueSoon))
        {
            return "同色且交期临近";
        }

        if (Has(RuleReason.SameColor))
        {
            return "同色减少换色";
        }

        if (Has(RuleReason.Urgent))
        {
            return "紧急件优先";
        }

        if (Has(RuleReason.Overdue))
        {
            return "逾期工单优先";
        }

        if (Has(RuleReason.DueSoon))
        {
            return "交期临近优先";
        }

        if (Has(RuleReason.High))
        {
            return "高优先级优先";
        }

        if (Has(RuleReason.SameFamily))
        {
            return "同零件族优先";
        }

        if (Has(RuleReason.DueWithin120))
        {
            return "两小时内交期优先";
        }

        return "按规则分排序";
    }

    public static string BuildUser(BoothState booth, IReadOnlyList<RankedCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(booth);
        ArgumentNullException.ThrowIfNull(candidates);
        var sb = new StringBuilder();
        sb.Append("工位:").Append(booth.BoothId);
        sb.Append(" 当前色:").Append(string.IsNullOrEmpty(booth.CurrentColorCode) ? "-" : booth.CurrentColorCode);
        sb.Append(" 体系:").Append(ChemistryText(booth.LoadedChemistry));
        sb.Append(" 烘房:").Append(booth.OvenTempC.ToString("0.##", CultureInfo.InvariantCulture)).Append("C");
        sb.Append('\n');
        sb.Append("候选(只能重排这些工单号):\n");
        foreach (var candidate in candidates)
        {
            var due = candidate.Job.DueAt - booth.Now;
            var dueText = due <= TimeSpan.Zero
                ? "逾期" + ((int)Math.Floor((-due).TotalMinutes)).ToString(CultureInfo.InvariantCulture) + "分钟"
                : ((int)Math.Floor(due.TotalMinutes)).ToString(CultureInfo.InvariantCulture) + "分钟后";
            sb.Append("- ").Append(candidate.CandidateId).Append(' ');
            sb.Append("色=").Append(candidate.Job.ColorCode).Append(' ').Append(candidate.Job.ColorName);
            sb.Append(" 优先级=").Append(PriorityText(candidate.Job.Priority));
            sb.Append(" 交期=").Append(dueText);
            sb.Append(" 件=").Append(candidate.Job.Quantity.ToString(CultureInfo.InvariantCulture));
            sb.Append(" 分=").Append(candidate.Score.ToString(CultureInfo.InvariantCulture));
            sb.Append(" 要点=").Append(candidate.Reasons.Count == 0 ? "无" : string.Join('、', candidate.Reasons));
            sb.Append('\n');
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>Qwen2.5 Instruct 的原始对话模板，供不走聊天接口的 GGUF 路径使用。</summary>
    public static string BuildRawPrompt(string system, string user)
    {
        return "<|im_start|>system\n"
            + system
            + "<|im_end|>\n<|im_start|>user\n"
            + user
            + "<|im_end|>\n<|im_start|>assistant\n";
    }

    public static string ChemistryText(PaintChemistry chemistry) => chemistry switch
    {
        PaintChemistry.Waterborne => "水性",
        PaintChemistry.Solvent => "溶剂型",
        PaintChemistry.Powder => "粉末",
        _ => "未知"
    };

    public static string PriorityText(JobPriority priority) => priority switch
    {
        JobPriority.Urgent => "紧急",
        JobPriority.High => "高",
        _ => "普通"
    };
}
