using PaintQueueLocalAi.Simulation;

namespace PaintQueueLocalAi.Tests;

public class SimulatorTests
{
    [Fact]
    public async Task Simulator_runs_the_four_shop_stories()
    {
        var report = await new InMemoryPaintShopSimulator().RunAllAsync();

        Assert.True(report.AllPassed, report.ToChineseText());
        Assert.Equal(
            new[] { "min-sequence", "fast-lane-a", "urgent-weight", "contended-gate" },
            report.Scenarios.Select(scenario => scenario.Key).ToArray());
        Assert.Equal(1, report.Scenarios.Single(scenario => scenario.Key == "contended-gate").MaxReleaseConcurrency);
        Assert.Contains("全部 4 个场景通过", report.ToChineseText());
    }
}
