using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using PaintQueueLocalAi;
using PaintQueueLocalAi.Simulation;

namespace PaintQueueLocalAi.Local;

public static class DatasetDefaults
{
    public const int Count = 48;
    public const int Seed = 20261023;
}

public sealed record DatasetRecord(
    string System,
    string User,
    string Assistant,
    IReadOnlyList<string> RuleOrder,
    IReadOnlyList<string> DisplayOrder,
    string Explanation,
    int? ShuffleSeed);

public static class AdvisorDatasetWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly string[] Colors = { "白", "黑", "红", "蓝" };

    public static async Task<IReadOnlyList<DatasetRecord>> BuildAsync(
        int count = DatasetDefaults.Count,
        int seed = DatasetDefaults.Seed,
        CancellationToken cancellationToken = default)
    {
        if (count < 1 || count > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        var engine = new DispatchEngine();
        var records = new List<DatasetRecord>(count);
        var index = 0;
        foreach (var request in EnumerateRequests(count))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var decision = await engine.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
            if (!decision.ShouldRelease || decision.RankedCandidates.Count < 2)
            {
                throw new InvalidOperationException("训练集需要至少两个可放行候选：" + request.RequestId);
            }

            var top = decision.RankedCandidates.Take(DispatchPolicy.Default.AdvisorTopK).ToArray();
            if (top.Length < 2)
            {
                throw new InvalidOperationException("前 K 名不足：" + request.RequestId);
            }

            var adviceRequest = new LlmAdviceRequest
            {
                Dispatch = request,
                TopCandidates = top,
                Policy = DispatchPolicy.Default,
                AppliedMinSequenceWeight = decision.AppliedMinSequenceWeight,
                RuleSummary = RuleSummary(request, top, decision.AppliedMinSequenceWeight)
            };
            int? shuffle = index % 3 == 0 ? seed + index : null;
            var display = shuffle is null ? top : Shuffle(top, shuffle.Value);
            var explanation = AdvisorPrompt.Explain(top[0], request.IsUrgent);
            if (explanation.Length > 40)
            {
                throw new InvalidOperationException("说明超过 40 字。");
            }

            var order = top.Select(candidate => candidate.CandidateId).ToArray();
            records.Add(new DatasetRecord(
                AdvisorPrompt.SystemPrompt,
                AdvisorPrompt.BuildUser(adviceRequest, display),
                SerializeAssistant(order, explanation),
                order,
                display.Select(candidate => candidate.CandidateId).ToArray(),
                explanation,
                shuffle));
            index++;
        }

        return records;
    }

    public static async Task<string> SerializeAsync(int count = DatasetDefaults.Count, int seed = DatasetDefaults.Seed, CancellationToken cancellationToken = default)
        => SerializeRecords(await BuildAsync(count, seed, cancellationToken).ConfigureAwait(false));

    public static async Task<IReadOnlyList<DatasetRecord>> BuildPlantOverridesAsync(CancellationToken cancellationToken = default)
    {
        var engine = new DispatchEngine();
        var calm = SampleSnapshots.Urgent(urgent: false);
        var calmDecision = await engine.EvaluateAsync(calm, cancellationToken).ConfigureAwait(false);
        var calmTop = calmDecision.RankedCandidates.Take(DispatchPolicy.Default.AdvisorTopK).ToArray();
        var relieved = calmTop.Reverse().ToArray();
        var min = SampleSnapshots.MinSequence();
        var minDecision = await engine.EvaluateAsync(min, cancellationToken).ConfigureAwait(false);
        var minTop = minDecision.RankedCandidates.Take(DispatchPolicy.Default.AdvisorTopK).ToArray();
        return new[]
        {
            Override(calm, calmDecision.AppliedMinSequenceWeight, calmTop, relieved, "满库车道先放"),
            Override(min, minDecision.AppliedMinSequenceWeight, minTop, minTop, "序号差已经明确")
        };
    }

    public static async Task<string> SerializePlantOverridesAsync(CancellationToken cancellationToken = default)
        => SerializeRecords(await BuildPlantOverridesAsync(cancellationToken).ConfigureAwait(false));

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

        return "FROM " + ggufPath.Replace('\\', '/') + "\n"
            + "PARAMETER temperature 0.1\n"
            + "PARAMETER num_predict 80\n"
            + "PARAMETER num_ctx 2048\n"
            + "PARAMETER stop <|im_end|>\n"
            + "SYSTEM \"\"\"\n"
            + AdvisorPrompt.SystemPrompt + "\n"
            + "\"\"\"\n";
    }

    public static string SerializeAssistant(IReadOnlyList<string> order, string explanation)
    {
        var payload = new Dictionary<string, object>
        {
            ["order"] = order,
            ["explanation"] = explanation
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static DatasetRecord Override(
        DispatchRequest request,
        decimal appliedWeight,
        IReadOnlyList<CandidateScore> display,
        IReadOnlyList<CandidateScore> target,
        string explanation)
    {
        var adviceRequest = new LlmAdviceRequest
        {
            Dispatch = request,
            TopCandidates = display,
            Policy = DispatchPolicy.Default,
            AppliedMinSequenceWeight = appliedWeight,
            RuleSummary = RuleSummary(request, display, appliedWeight)
        };
        var order = target.Select(candidate => candidate.CandidateId).ToArray();
        return new DatasetRecord(
            AdvisorPrompt.SystemPrompt,
            AdvisorPrompt.BuildUser(adviceRequest, display),
            SerializeAssistant(order, explanation),
            order,
            display.Select(candidate => candidate.CandidateId).ToArray(),
            explanation,
            null);
    }

    private static string RuleSummary(DispatchRequest request, IReadOnlyList<CandidateScore> top, decimal appliedWeight)
    {
        var builder = new StringBuilder();
        builder.Append(request.IsUrgent ? "紧急要车。" : "非紧急。");
        builder.Append("最小序号权重 ").Append(appliedWeight.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Append("。规则排序：");
        for (var i = 0; i < top.Count; i++)
        {
            if (i > 0)
            {
                builder.Append('；');
            }

            builder.Append(top[i].CandidateId)
                .Append(" 序号 ")
                .Append(top[i].SequenceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append(" 得分 ")
                .Append(top[i].Score.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static IEnumerable<DispatchRequest> EnumerateRequests(int count)
    {
        if (count >= 1)
        {
            yield return SampleSnapshots.MinSequence();
        }

        if (count >= 2)
        {
            yield return SampleSnapshots.FastLane();
        }

        if (count >= 3)
        {
            yield return SampleSnapshots.Urgent(urgent: false);
        }

        if (count >= 4)
        {
            yield return SampleSnapshots.Urgent(urgent: true);
        }

        var synthetic = Math.Max(0, count - 4);
        for (var i = 0; i < synthetic; i++)
        {
            yield return Synthetic(i);
        }
    }

    private static DispatchRequest Synthetic(int index)
    {
        var laneCount = 4 + (index % 5);
        var lanes = new BufferQueue[laneCount];
        for (var lane = 0; lane < laneCount; lane++)
        {
            var sequence = 8 + ((index * 5 + lane * 9) % 36);
            var fill = 1 + ((index + lane * 2) % 10);
            var vehicles = new Vehicle[fill];
            for (var slot = 0; slot < fill; slot++)
            {
                vehicles[slot] = new Vehicle
                {
                    Id = $"T{index:00}{lane}{slot}",
                    SequenceNumber = sequence + slot,
                    SourceExit = BoothExitId.B,
                    ColorCode = Colors[(index + lane + slot) % Colors.Length]
                };
            }

            lanes[lane] = new BufferQueue
            {
                Id = "BUF-" + (lane + 1).ToString("00", System.Globalization.CultureInfo.InvariantCulture),
                Capacity = 10,
                Vehicles = vehicles,
                ReleaseBlocked = lane == laneCount - 1 && index % 4 == 0
            };
        }

        var fast = index % 5 == 0;
        var fastVehicle = fast
            ? new Vehicle
            {
                Id = $"F{index:00}",
                SequenceNumber = 40 + (index % 8),
                SourceExit = BoothExitId.A,
                ColorCode = "红"
            }
            : null;
        return new DispatchRequest
        {
            RequestId = "train-" + index.ToString("00", System.Globalization.CultureInfo.InvariantCulture),
            SharedResourceId = SampleSnapshots.Merge,
            RequestingExit = fast ? BoothExitId.A : BoothExitId.B,
            IsUrgent = index % 7 == 0,
            Queues = lanes,
            Exits = new[]
            {
                new BoothExit { Id = BoothExitId.A, FastLaneOpen = fast, PresentVehicle = fastVehicle },
                new BoothExit { Id = BoothExitId.B }
            },
            Safety = SafetyInterlockState.Nominal,
            RequestedAt = new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.Zero)
        };
    }

    private static IReadOnlyList<CandidateScore> Shuffle(IReadOnlyList<CandidateScore> ranked, int seed)
    {
        var list = ranked.ToList();
        var random = new Random(seed);
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }

    private static string SerializeRecords(IReadOnlyList<DatasetRecord> records)
    {
        var builder = new StringBuilder();
        foreach (var record in records)
        {
            var line = new Dictionary<string, object>
            {
                ["messages"] = new object[]
                {
                    new Dictionary<string, string> { ["role"] = "system", ["content"] = record.System },
                    new Dictionary<string, string> { ["role"] = "user", ["content"] = record.User },
                    new Dictionary<string, string> { ["role"] = "assistant", ["content"] = record.Assistant }
                }
            };
            builder.Append(JsonSerializer.Serialize(line, JsonOptions));
            builder.Append('\n');
        }

        return builder.ToString();
    }
}
