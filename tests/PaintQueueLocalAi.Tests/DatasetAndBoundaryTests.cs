using System.Text.Json;
using PaintQueueLocalAi;

namespace PaintQueueLocalAi.Tests;

public class DatasetAndBoundaryTests
{
    [Fact]
    public void Dataset_FollowsRulesAndOmitsExcludedJobs()
    {
        var records = AdvisorDatasetWriter.Build();
        Assert.Equal(DatasetDefaults.Count, records.Count);
        var explanations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            Assert.Equal(AdvisorPrompt.SystemPrompt, record.System);
            var parsed = AdvisorResponseParser.Parse(record.Assistant);
            Assert.True(parsed.Success);
            Assert.Equal(record.RuleOrder, parsed.Order);
            Assert.Equal(record.Explanation, parsed.Explanation);
            Assert.InRange(record.Explanation.Length, 1, AdvisorLimits.PromptExplanationChars);
            explanations.Add(record.Explanation);
            var positions = record.DisplayOrder.Select(id => record.User.IndexOf("- " + id + " ", StringComparison.Ordinal)).ToArray();
            Assert.All(positions, index => Assert.True(index >= 0));
            Assert.True(positions.Zip(positions.Skip(1), (left, right) => left < right).All(ok => ok));
            foreach (var excluded in record.ExcludedIds)
            {
                Assert.DoesNotContain("- " + excluded + " ", record.User);
            }
        }

        Assert.True(explanations.Count >= 3);
    }

    [Fact]
    public void CommittedDataset_MatchesGenerator()
    {
        var root = RepoPaths.Root();
        AssertText(Path.Combine(root, "finetune", "data", "paint_queue_advisor.jsonl"), AdvisorDatasetWriter.Serialize());
        AssertText(Path.Combine(root, "finetune", "data", "plant_overrides.sample.jsonl"), AdvisorDatasetWriter.SerializePlantOverrides());
        AssertText(
            Path.Combine(root, "scripts", "ollama", "Modelfile"),
            AdvisorDatasetWriter.RenderModelfile("../../models/qwen2.5-1.5b-instruct-q4_k_m.gguf"));
        AssertText(
            Path.Combine(root, "finetune", "Modelfile"),
            AdvisorDatasetWriter.RenderModelfile("./paint-advisor-q4_k_m.gguf"));
    }

    [Fact]
    public void PlantOverrides_TeachAReorderAndAKeep()
    {
        var records = AdvisorDatasetWriter.BuildPlantOverrides();
        Assert.Equal(2, records.Count);
        var swapped = AdvisorResponseParser.Parse(records[0].Assistant);
        Assert.Equal(new[] { "W1", "K1" }, swapped.Order);
        Assert.Equal(new[] { "K1", "W1" }, records[0].DisplayOrder);
        var kept = AdvisorResponseParser.Parse(records[1].Assistant);
        Assert.Equal(new[] { "U1", "N1" }, kept.Order);
        Assert.Throws<ArgumentException>(() => AdvisorDatasetWriter.RenderModelfile("https://example.com/model.gguf"));
    }

    [Fact]
    public void CoreProject_DoesNotReferenceRuntimeOrNetwork()
    {
        var root = RepoPaths.Root();
        var coreDir = Path.Combine(root, "src", "PaintQueueLocalAi");
        var csproj = File.ReadAllText(Path.Combine(coreDir, "PaintQueueLocalAi.csproj"));
        Assert.DoesNotContain("LLamaSharp", csproj, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PackageReference", csproj, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ProjectReference", csproj, StringComparison.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(coreDir, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("HttpClient", text, StringComparison.Ordinal);
            Assert.DoesNotContain("LLama", text, StringComparison.Ordinal);
            Assert.DoesNotContain("http://", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("https://", text, StringComparison.OrdinalIgnoreCase);
        }

        var strayWeights = Directory.EnumerateFiles(root, "*.gguf", SearchOption.AllDirectories)
            .Where(path => !IsLocalWeightPath(path))
            .ToArray();
        Assert.Empty(strayWeights);
    }

    [Fact]
    public void Jsonl_IsChatShaped()
    {
        using var doc = JsonDocument.Parse(AdvisorDatasetWriter.Serialize().Split('\n', StringSplitOptions.RemoveEmptyEntries)[0]);
        var messages = doc.RootElement.GetProperty("messages");
        Assert.Equal(3, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("assistant", messages[2].GetProperty("role").GetString());
    }

    private static bool IsLocalWeightPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/models/", StringComparison.Ordinal)
            || normalized.Contains("/finetune/output/", StringComparison.Ordinal)
            || normalized.Contains("/bin/", StringComparison.Ordinal)
            || normalized.Contains("/obj/", StringComparison.Ordinal);
    }

    private static void AssertText(string path, string expected)
    {
        Assert.True(File.Exists(path), path);
        var actual = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(expected, actual);
    }
}

internal static class RepoPaths
{
    public static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PaintQueueLocalAi.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("找不到仓库根目录。");
    }
}
