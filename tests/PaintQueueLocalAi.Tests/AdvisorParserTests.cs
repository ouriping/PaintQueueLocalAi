using PaintQueueLocalAi;

namespace PaintQueueLocalAi.Tests;

public class AdvisorParserTests
{
    [Theory]
    [InlineData("{\"order\":[\"W-URG\",\"K-LATE\"],\"explanation\":\"同色紧急件优先\"}", new[] { "W-URG", "K-LATE" }, "同色紧急件优先")]
    [InlineData("```json\n{\"order\":[\"A\"],\"explanation\":\"ok\"}\n```", new[] { "A" }, "ok")]
    [InlineData("说明如下：{\"order\":[\"A\",\"B\"],\"explanation\":\"保持\"} 完毕", new[] { "A", "B" }, "保持")]
    public void Parse_AcceptsJsonAndFences(string text, string[] order, string explanation)
    {
        var parsed = AdvisorResponseParser.Parse(text);
        Assert.True(parsed.Success);
        Assert.Equal(order, parsed.Order);
        Assert.Equal(explanation, parsed.Explanation);
    }

    [Theory]
    [InlineData("")]
    [InlineData("没有 JSON")]
    [InlineData("{\"order\":[]}")]
    [InlineData("{\"order\":[1]}")]
    [InlineData("{\"explanation\":\"只有说明\"}")]
    [InlineData("{\"order\":\"W-URG\"}")]
    public void Parse_RejectsInvalidPayloads(string text)
    {
        var parsed = AdvisorResponseParser.Parse(text);
        Assert.False(parsed.Success);
        Assert.Empty(parsed.Order);
    }

    [Fact]
    public void TryApply_DropsUnknownAndDuplicates()
    {
        var sample = SampleQueues.MorningShift();
        var top = new DispatchEngine().Preview(sample.Booth, sample.Jobs).Ranked;
        Assert.True(AdvisorOrder.TryApply(new[] { "R-HIGH", "R-HIGH", "NO", "W-URG" }, top, out var applied));
        Assert.Equal(new[] { "R-HIGH", "W-URG", "K-LATE", "W-NORM" }, applied.Select(item => item.CandidateId).ToArray());
        Assert.False(AdvisorOrder.TryApply(new[] { "NO" }, top, out _));
    }

    [Fact]
    public void Prompt_ContainsOnlyProvidedCandidates()
    {
        var sample = SampleQueues.MorningShift();
        var top = new DispatchEngine().Preview(sample.Booth, sample.Jobs).Ranked.Take(2).ToArray();
        var user = AdvisorPrompt.BuildUser(sample.Booth, top);
        Assert.Contains("- W-URG ", user);
        Assert.Contains("- K-LATE ", user);
        Assert.DoesNotContain("W-HOLD", user);
        Assert.DoesNotContain("S-CHEM", user);
        Assert.Contains(AdvisorPrompt.SystemPrompt, AdvisorPrompt.BuildRawPrompt(AdvisorPrompt.SystemPrompt, user));
        Assert.Contains("<|im_start|>assistant", AdvisorPrompt.BuildRawPrompt(AdvisorPrompt.SystemPrompt, user));
    }

    [Fact]
    public void ExplainTemplates_StayWithinFortyCharacters()
    {
        var now = SampleQueues.MorningNow;
        var job = new PaintJob("J", "RAL9016", "白", PaintChemistry.Waterborne, JobPriority.Normal, now, "门板", 1, false, true, 160);
        string[][] reasonSets =
        {
            new[] { RuleReason.SameColor, RuleReason.Urgent },
            new[] { RuleReason.SameColor, RuleReason.Overdue },
            new[] { RuleReason.Urgent, RuleReason.Overdue },
            new[] { RuleReason.SameColor, RuleReason.DueSoon },
            new[] { RuleReason.SameColor },
            new[] { RuleReason.Urgent },
            new[] { RuleReason.Overdue },
            new[] { RuleReason.DueSoon },
            new[] { RuleReason.High },
            new[] { RuleReason.SameFamily },
            new[] { RuleReason.DueWithin120 },
            Array.Empty<string>()
        };
        foreach (var reasons in reasonSets)
        {
            var text = AdvisorPrompt.Explain(new RankedCandidate("J", job, 1, 1, reasons));
            Assert.InRange(text.Length, 1, AdvisorLimits.PromptExplanationChars);
        }
    }
}
