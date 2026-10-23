namespace PaintQueueLocalAi;

/// <summary>规则分权重。数值是现场可解释的相对分，不是货币或分钟。</summary>
public static class RuleWeights
{
    public const int Urgent = 400;
    public const int High = 200;
    public const int OverdueBase = 300;
    public const int OverduePerMinute = 2;
    public const int OverdueCapMinutes = 120;
    public const int DueWithin30 = 180;
    public const int DueWithin120 = 80;
    public const int SameColor = 220;
    public const int SamePartFamily = 40;
}

public static class RuleReason
{
    public const string Urgent = "紧急";
    public const string High = "高优先";
    public const string Overdue = "已逾期";
    public const string DueSoon = "交期临近";
    public const string DueWithin120 = "两小时内交期";
    public const string SameColor = "同色";
    public const string SameFamily = "同零件族";
}

public static class RuleScorer
{
    public static IReadOnlyList<RankedCandidate> Rank(BoothState booth, IReadOnlyList<PaintJob> eligible)
    {
        ArgumentNullException.ThrowIfNull(booth);
        ArgumentNullException.ThrowIfNull(eligible);

        var scored = new List<RankedCandidate>(eligible.Count);
        foreach (var job in eligible)
        {
            var reasons = new List<string>(4);
            var score = 0;

            switch (job.Priority)
            {
                case JobPriority.Urgent:
                    score += RuleWeights.Urgent;
                    reasons.Add(RuleReason.Urgent);
                    break;
                case JobPriority.High:
                    score += RuleWeights.High;
                    reasons.Add(RuleReason.High);
                    break;
            }

            var delta = job.DueAt - booth.Now;
            if (delta <= TimeSpan.Zero)
            {
                var late = (int)Math.Floor((-delta).TotalMinutes);
                if (late < 0)
                {
                    late = 0;
                }

                var capped = Math.Min(RuleWeights.OverdueCapMinutes, late);
                score += RuleWeights.OverdueBase + (capped * RuleWeights.OverduePerMinute);
                reasons.Add(RuleReason.Overdue);
            }
            else
            {
                var minutes = (int)Math.Floor(delta.TotalMinutes);
                if (minutes <= 30)
                {
                    score += RuleWeights.DueWithin30;
                    reasons.Add(RuleReason.DueSoon);
                }
                else if (minutes <= 120)
                {
                    score += RuleWeights.DueWithin120;
                    reasons.Add(RuleReason.DueWithin120);
                }
            }

            if (!string.IsNullOrEmpty(booth.CurrentColorCode)
                && string.Equals(job.ColorCode, booth.CurrentColorCode, StringComparison.OrdinalIgnoreCase))
            {
                score += RuleWeights.SameColor;
                reasons.Add(RuleReason.SameColor);
            }

            if (!string.IsNullOrEmpty(booth.CurrentPartFamily)
                && string.Equals(job.PartFamily, booth.CurrentPartFamily, StringComparison.OrdinalIgnoreCase))
            {
                score += RuleWeights.SamePartFamily;
                reasons.Add(RuleReason.SameFamily);
            }

            scored.Add(new RankedCandidate(job.JobId, job, score, 0, reasons));
        }

        var ordered = scored
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Job.DueAt)
            .ThenBy(item => item.Job.JobId, StringComparer.Ordinal)
            .ToArray();

        var ranked = new RankedCandidate[ordered.Length];
        for (var i = 0; i < ordered.Length; i++)
        {
            ranked[i] = ordered[i] with { Rank = i + 1 };
        }

        return ranked;
    }
}
