using PaintQueueLocalAi;

namespace PaintQueueLocalAi.Simulation;

/// <summary>
/// 演示和验收测试使用的固定车间快照。
/// 最小序号快照里的充满率都低且几乎相等，以免负载均衡推翻最小车头。
/// 紧急快照是相反情形：一条几乎空的车道拿着更小的序号，一条满库车道拿着下一个序号。
/// </summary>
public static class SampleSnapshots
{
    /// <summary>示例请求共用的合流点。</summary>
    public const string Merge = "MERGE-01";

    /// <summary>
    /// 八条缓存车道。车间里最小的序号埋在 BUF-01 车头后面，
    /// 更小的一辆车停在出口 B。两者都不能放行。最小车头是 B-0012。
    /// </summary>
    public static DispatchRequest MinSequence()
    {
        return new DispatchRequest
        {
            RequestId = "min-sequence",
            SharedResourceId = Merge,
            RequestingExit = BoothExitId.B,
            IsUrgent = false,
            Queues = new[]
            {
                Lane("BUF-01", 10, Car("B-0030", 30, color: "白"), Car("B-0001", 1, color: "银")),
                Lane("BUF-02", 10, Car("B-0012", 12, color: "白")),
                Lane("BUF-03", 10, Car("B-0018", 18, color: "黑")),
                Lane("BUF-04", 10, Car("B-0040", 40, color: "红")),
                Lane("BUF-05", 10, Car("B-0022", 22, color: "蓝")),
                Lane("BUF-06", 10, Car("B-0027", 27, color: "白")),
                Lane("BUF-07", 10, Car("B-0033", 33, color: "灰")),
                Lane("BUF-08", 10)
            },
            Exits = new[]
            {
                new BoothExit { Id = BoothExitId.A, FastLaneOpen = false },
                new BoothExit
                {
                    Id = BoothExitId.B,
                    FastLaneOpen = true,
                    PresentVehicle = Car("B-0004", 4, BoothExitId.B, "黑")
                }
            },
            Safety = SafetyInterlockState.Nominal
        };
    }

    /// <summary>
    /// 出口 A 的序号高于两个缓存车头，且快车道已打开。
    /// 即使某个缓存车头的序号得分更高，快车道档位也必须胜出。
    /// </summary>
    public static DispatchRequest FastLane()
    {
        return new DispatchRequest
        {
            RequestId = "fast-lane-a",
            SharedResourceId = Merge,
            RequestingExit = BoothExitId.A,
            IsUrgent = false,
            Queues = new[]
            {
                Lane("BUF-01", 10, Car("B-0005", 5, color: "白")),
                Lane("BUF-02", 10, Car("B-0008", 8, color: "蓝"))
            },
            Exits = new[]
            {
                new BoothExit
                {
                    Id = BoothExitId.A,
                    FastLaneOpen = true,
                    PresentVehicle = Car("B-0020", 20, BoothExitId.A, "红")
                },
                new BoothExit { Id = BoothExitId.B }
            },
            Safety = SafetyInterlockState.Nominal
        };
    }

    /// <summary>
    /// 两条有车的车道，外加六条空车道。BUF-01 以 10% 充满率持有序号 100。
    /// BUF-02 以 100% 充满率持有序号 110。非紧急时仍放行 100；紧急时改放满库车道。
    /// </summary>
    public static DispatchRequest Urgent(bool urgent)
    {
        var full = new Vehicle[10];
        for (var i = 0; i < full.Length; i++)
        {
            full[i] = Car("B-0" + (110 + i).ToString(), 110 + i, color: i == 0 ? "白" : "灰");
        }

        return new DispatchRequest
        {
            RequestId = urgent ? "urgent-exit" : "calm-exit",
            SharedResourceId = Merge,
            RequestingExit = BoothExitId.B,
            IsUrgent = urgent,
            Queues = new[]
            {
                Lane("BUF-01", 10, Car("B-0100", 100, color: "黑")),
                Lane("BUF-02", 10, full),
                Lane("BUF-03", 10),
                Lane("BUF-04", 10),
                Lane("BUF-05", 10),
                Lane("BUF-06", 10),
                Lane("BUF-07", 10),
                Lane("BUF-08", 10)
            },
            Exits = new[]
            {
                new BoothExit { Id = BoothExitId.A, FastLaneOpen = false },
                new BoothExit { Id = BoothExitId.B }
            },
            Safety = SafetyInterlockState.Nominal
        };
    }

    /// <summary>两条车道各自要把自己的车头放到同一合流点上放行。</summary>
    public static (DispatchRequest Left, DispatchRequest Right) ContendedPair()
    {
        DispatchRequest One(string requestId, string queueId, string vehicleId, int sequence) => new()
        {
            RequestId = requestId,
            SharedResourceId = Merge,
            RequestingExit = BoothExitId.A,
            Queues = new[] { Lane(queueId, 10, Car(vehicleId, sequence)) },
            Exits = new[]
            {
                new BoothExit { Id = BoothExitId.A },
                new BoothExit { Id = BoothExitId.B }
            },
            Safety = SafetyInterlockState.Nominal
        };

        return (
            One("gate-left", "BUF-01", "B-0001", 1),
            One("gate-right", "BUF-02", "B-0002", 2));
    }

    private static Vehicle Car(string id, int sequence, BoothExitId source = BoothExitId.B, string? color = null) => new()
    {
        Id = id,
        SequenceNumber = sequence,
        SourceExit = source,
        ColorCode = color
    };

    private static BufferQueue Lane(string id, int capacity, params Vehicle[] vehicles) => new()
    {
        Id = id,
        Capacity = capacity,
        Vehicles = vehicles
    };
}
