using System.Text;
using PaintQueueLocalAi;

namespace PaintQueueLocalAi.Simulation;

/// <summary>一个预设场景执行之后的结果。</summary>
public sealed record ScenarioRun
{
    /// <summary>稳定键：<c>min-sequence</c>、<c>fast-lane-a</c>、<c>urgent-weight</c> 或 <c>contended-gate</c>。</summary>
    public required string Key { get; init; }

    /// <summary>记录用的中文标题。</summary>
    public required string Title { get; init; }

    /// <summary>现场引擎符合该场景约定时为 true。</summary>
    public required bool Passed { get; init; }

    /// <summary>中文叙述，加上引擎说明。</summary>
    public required string Detail { get; init; }

    /// <summary>该场景产生的决策，按运行顺序排列。</summary>
    public required IReadOnlyList<DispatchDecision> Decisions { get; init; }

    /// <summary>放行闸内物理放行回调的峰值个数。场景不提交时为 null。</summary>
    public int? MaxReleaseConcurrency { get; init; }
}

/// <summary>预设场景的记录。</summary>
public sealed record SimulationReport
{
    /// <summary>按显示顺序排列的场景。</summary>
    public required IReadOnlyList<ScenarioRun> Scenarios { get; init; }

    /// <summary>每个场景都通过时为 true。</summary>
    public bool AllPassed => Scenarios.Count > 0 && Scenarios.All(scenario => scenario.Passed);

    /// <summary>供控制台或日志使用的多行中文记录。</summary>
    public string ToChineseText()
    {
        var builder = new StringBuilder();
        builder.AppendLine("涂装缓存队列调度仿真");
        builder.AppendLine("决策源：确定性规则引擎。默认不加载本地模型。");
        builder.AppendLine();
        foreach (var scenario in Scenarios)
        {
            builder.AppendLine(scenario.Passed ? "[通过] " + scenario.Title : "[失败] " + scenario.Title);
            builder.AppendLine(scenario.Detail.TrimEnd());
            builder.AppendLine();
        }

        var failed = 0;
        foreach (var scenario in Scenarios)
        {
            if (!scenario.Passed)
            {
                failed++;
            }
        }

        builder.Append(failed == 0
            ? $"全部 {Scenarios.Count} 个场景通过。"
            : $"{failed} 个场景失败。");
        return builder.ToString();
    }
}
