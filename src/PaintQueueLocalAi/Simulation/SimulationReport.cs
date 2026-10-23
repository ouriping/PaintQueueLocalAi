using System.Text;
using PaintQueueLocalAi;

namespace PaintQueueLocalAi.Simulation;

/// <summary>One canned scenario after it has been executed.</summary>
public sealed record ScenarioRun
{
    /// <summary>Stable key: <c>min-sequence</c>, <c>fast-lane-a</c>, <c>urgent-weight</c>, or <c>contended-gate</c>.</summary>
    public required string Key { get; init; }

    /// <summary>Chinese title for the transcript.</summary>
    public required string Title { get; init; }

    /// <summary>True when the live engine matched the scenario contract.</summary>
    public required bool Passed { get; init; }

    /// <summary>Chinese narrative plus the engine explanation.</summary>
    public required string Detail { get; init; }

    /// <summary>Decisions produced by the scenario, in run order.</summary>
    public required IReadOnlyList<DispatchDecision> Decisions { get; init; }

    /// <summary>Peak number of physical-release callbacks inside the gate. Null when the scenario does not commit.</summary>
    public int? MaxReleaseConcurrency { get; init; }
}

/// <summary>Transcript of the canned scenarios.</summary>
public sealed record SimulationReport
{
    /// <summary>Scenarios in display order.</summary>
    public required IReadOnlyList<ScenarioRun> Scenarios { get; init; }

    /// <summary>True when every scenario passed.</summary>
    public bool AllPassed => Scenarios.Count > 0 && Scenarios.All(scenario => scenario.Passed);

    /// <summary>Multi-line Chinese transcript for a console or a log.</summary>
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
