using PaintQueueLocalAi;

namespace PaintQueueLocalAi.Simulation;

/// <summary>
/// Fixed shop snapshots used by the demo and the acceptance tests.
/// Fills in the min-sequence snapshot stay low and nearly equal so load balance does not overturn the smallest head.
/// The urgent snapshot is the opposite case: one almost-empty lane holds the smaller sequence, and a full lane holds the next one.
/// </summary>
public static class SampleSnapshots
{
    /// <summary>Shared merge used by the sample requests.</summary>
    public const string Merge = "MERGE-01";

    /// <summary>
    /// Eight buffer lanes. The smallest sequence in the building is buried behind BUF-01's head,
    /// and an even smaller body is sitting on exit B. Neither can be released. The smallest head is B-0012.
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
    /// Exit A has a higher sequence than both buffer heads, and the fast lane is open.
    /// The fast-lane tier must win even though a buffer head scores higher on sequence.
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
    /// Two occupied lanes plus six empty ones. BUF-01 holds sequence 100 at 10% fill.
    /// BUF-02 holds sequence 110 at 100% fill. Non-urgent keeps 100; urgent switches to the full lane.
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

    /// <summary>Two lanes that each want to release their own head onto the same merge.</summary>
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
