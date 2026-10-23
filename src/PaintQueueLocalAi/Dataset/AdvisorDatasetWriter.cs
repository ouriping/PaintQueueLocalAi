using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PaintQueueLocalAi;

public static class DatasetDefaults
{
    public const int Count = 48;
    public const int Seed = 20261023;
    public const int TopK = 4;
}

public sealed record DatasetRecord(
    string System,
    string User,
    string Assistant,
    IReadOnlyList<string> RuleOrder,
    IReadOnlyList<string> DisplayOrder,
    IReadOnlyList<string> ExcludedIds,
    string Explanation,
    int? ShuffleSeed);

public static class AdvisorDatasetWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static IReadOnlyList<DatasetRecord> Build(int count = DatasetDefaults.Count, int seed = DatasetDefaults.Seed)
    {
        if (count < 1 || count > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        var engine = new DispatchEngine(new DispatchOptions
        {
            TopK = DatasetDefaults.TopK,
            AdvisorTimeout = TimeSpan.FromMilliseconds(AdvisorLimits.DefaultTimeoutMilliseconds)
        });
        var records = new List<DatasetRecord>(count);
        foreach (var scenario in ScenarioFactory.Create(count, seed))
        {
            var preview = engine.Preview(scenario.Booth, scenario.Jobs);
            if (preview.BlockingInterlock is not null)
            {
                throw new InvalidOperationException("训练集不包含联锁停线。");
            }

            var top = preview.Ranked.Take(DatasetDefaults.TopK).ToArray();
            if (top.Length < 2)
            {
                throw new InvalidOperationException("训练集每条至少两个候选。");
            }

            var display = OrderForPrompt(top, scenario.ShuffleSeed);
            var explanation = AdvisorPrompt.Explain(top[0]);
            if (explanation.Length > AdvisorLimits.PromptExplanationChars)
            {
                throw new InvalidOperationException("说明超过 40 字：" + explanation);
            }

            var assistant = SerializeAssistant(top.Select(item => item.CandidateId).ToArray(), explanation);
            records.Add(new DatasetRecord(
                AdvisorPrompt.SystemPrompt,
                AdvisorPrompt.BuildUser(scenario.Booth, display),
                assistant,
                top.Select(item => item.CandidateId).ToArray(),
                display.Select(item => item.CandidateId).ToArray(),
                preview.Excluded.Select(item => item.JobId).ToArray(),
                explanation,
                scenario.ShuffleSeed));
        }

        return records;
    }

    public static string Serialize(int count = DatasetDefaults.Count, int seed = DatasetDefaults.Seed)
        => SerializeRecords(Build(count, seed));

    public static string SerializePlantOverrides()
        => SerializeRecords(BuildPlantOverrides());

    public static IReadOnlyList<DatasetRecord> BuildPlantOverrides()
    {
        var now = SampleQueues.MorningNow;
        var booth = new BoothState("B1", "RAL9016", "门板", PaintChemistry.Waterborne, false, true, true, true, 180, now);
        var black = new PaintJob("K1", "RAL9005", "黑", PaintChemistry.Waterborne, JobPriority.Urgent, now.AddHours(3), "保险杠", 1, false, true, 160);
        var white = new PaintJob("W1", "RAL9016", "白", PaintChemistry.Waterborne, JobPriority.Normal, now.AddHours(3), "门板", 2, false, true, 160);
        var urgent = new PaintJob("U1", "RAL3020", "红", PaintChemistry.Waterborne, JobPriority.Urgent, now.AddMinutes(-30), "翼子板", 1, false, true, 160);
        var normal = new PaintJob("N1", "RAL5015", "蓝", PaintChemistry.Waterborne, JobPriority.Normal, now.AddHours(6), "翼子板", 1, false, true, 160);

        var nearTie = new[]
        {
            new RankedCandidate(black.JobId, black, 400, 1, new[] { RuleReason.Urgent }),
            new RankedCandidate(white.JobId, white, 380, 2, new[] { RuleReason.SameColor, RuleReason.SameFamily })
        };
        var wideGap = new[]
        {
            new RankedCandidate(urgent.JobId, urgent, 700, 1, new[] { RuleReason.Urgent, RuleReason.Overdue }),
            new RankedCandidate(normal.JobId, normal, 100, 2, new[] { RuleReason.SameFamily })
        };

        return new[]
        {
            Record(booth, nearTie, new[] { "W1", "K1" }, "同色优先以减少换色"),
            Record(booth, wideGap, new[] { "U1", "N1" }, "分差大，保持原顺序")
        };
    }

    public static void WriteFile(string path, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(full, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static string RenderModelfile(string ggufPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ggufPath);
        if (ggufPath.Contains("://", StringComparison.Ordinal))
        {
            throw new ArgumentException("Modelfile FROM 必须是本地 GGUF 路径。", nameof(ggufPath));
        }

        var normalized = ggufPath.Replace('\\', '/');
        return "FROM " + normalized + "\n"
            + "PARAMETER temperature 0.1\n"
            + "PARAMETER num_predict 80\n"
            + "PARAMETER num_ctx 2048\n"
            + "PARAMETER stop <|im_end|>\n"
            + "SYSTEM \"\"\"\n"
            + AdvisorPrompt.SystemPrompt + "\n"
            + "\"\"\"\n";
    }

    private static DatasetRecord Record(BoothState booth, IReadOnlyList<RankedCandidate> display, IReadOnlyList<string> order, string explanation)
    {
        return new DatasetRecord(
            AdvisorPrompt.SystemPrompt,
            AdvisorPrompt.BuildUser(booth, display),
            SerializeAssistant(order, explanation),
            order,
            display.Select(item => item.CandidateId).ToArray(),
            Array.Empty<string>(),
            explanation,
            null);
    }

    private static string SerializeRecords(IReadOnlyList<DatasetRecord> records)
    {
        var sb = new StringBuilder();
        foreach (var record in records)
        {
            var line = new Dictionary<string, object>
            {
                ["messages"] = new object[]
                {
                    Message("system", record.System),
                    Message("user", record.User),
                    Message("assistant", record.Assistant)
                }
            };
            sb.Append(JsonSerializer.Serialize(line, JsonOptions));
            sb.Append('\n');
        }

        return sb.ToString();
    }

    private static Dictionary<string, string> Message(string role, string content)
        => new()
        {
            ["role"] = role,
            ["content"] = content
        };

    public static string SerializeAssistant(IReadOnlyList<string> order, string explanation)
    {
        var payload = new Dictionary<string, object>
        {
            ["order"] = order,
            ["explanation"] = explanation
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static IReadOnlyList<RankedCandidate> OrderForPrompt(IReadOnlyList<RankedCandidate> ranked, int? seed)
    {
        var list = ranked.ToList();
        if (seed is null)
        {
            return list;
        }

        var rng = new Random(seed.Value);
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }
}

internal readonly record struct TrainingScenario(BoothState Booth, IReadOnlyList<PaintJob> Jobs, int? ShuffleSeed);

internal static class ScenarioFactory
{
    private static readonly (string Code, string Name)[] Colors =
    {
        ("RAL9016", "白"),
        ("RAL9005", "黑"),
        ("RAL3020", "红"),
        ("RAL5015", "蓝")
    };

    private static readonly string[] Families = { "门板", "保险杠", "翼子板" };

    private static readonly PaintChemistry[] Chemistries =
    {
        PaintChemistry.Waterborne,
        PaintChemistry.Solvent,
        PaintChemistry.Powder
    };

    public static IEnumerable<TrainingScenario> Create(int count, int seed)
    {
        var now = SampleQueues.MorningNow;
        for (var i = 0; i < count; i++)
        {
            var chemistry = Chemistries[i % Chemistries.Length];
            var current = Colors[i % Colors.Length];
            var booth = new BoothState(
                $"B{(i % 3) + 1}",
                current.Code,
                Families[i % Families.Length],
                chemistry,
                false,
                true,
                true,
                true,
                180,
                now);

            var jobs = new List<PaintJob>(6);
            for (var j = 0; j < 6; j++)
            {
                var color = Colors[(i + j) % Colors.Length];
                var priority = (i + j) % 5 == 0
                    ? JobPriority.Urgent
                    : (i + j) % 3 == 0 ? JobPriority.High : JobPriority.Normal;
                var dueOffset = ((i * 17) + (j * 29)) % 181 - 40;
                var qualityHold = j == 5 && i % 2 == 0;
                var wrongChem = j == 4 && i % 2 == 1;
                var flash = !(j == 3 && i % 4 == 0);
                var required = j == 2 && i % 5 == 0 ? 200 : 160;
                jobs.Add(new PaintJob(
                    $"S{i:00}J{j}",
                    color.Code,
                    color.Name,
                    wrongChem ? NextChemistry(chemistry) : chemistry,
                    priority,
                    now.AddMinutes(dueOffset),
                    Families[(i + j) % Families.Length],
                    1 + ((i + j) % 4),
                    qualityHold,
                    flash,
                    required));
            }

            if (CountEligible(booth, jobs) < 2)
            {
                jobs[0] = MakeEligible(jobs[0], chemistry);
                jobs[1] = MakeEligible(jobs[1], chemistry);
            }

            int? shuffle = i % 3 == 0 ? seed + i : null;
            yield return new TrainingScenario(booth, jobs, shuffle);
        }
    }

    private static int CountEligible(BoothState booth, IReadOnlyList<PaintJob> jobs)
    {
        var count = 0;
        foreach (var job in jobs)
        {
            if (SafetyInterlocks.TryAccept(job, booth, out _))
            {
                count++;
            }
        }

        return count;
    }

    private static PaintJob MakeEligible(PaintJob job, PaintChemistry chemistry)
        => job with
        {
            QualityHold = false,
            FlashOffComplete = true,
            Chemistry = chemistry,
            RequiredOvenC = 160
        };

    private static PaintChemistry NextChemistry(PaintChemistry chemistry) => chemistry switch
    {
        PaintChemistry.Waterborne => PaintChemistry.Solvent,
        PaintChemistry.Solvent => PaintChemistry.Powder,
        _ => PaintChemistry.Waterborne
    };
}
