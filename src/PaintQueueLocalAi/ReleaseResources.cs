namespace PaintQueueLocalAi;

/// <summary>Lock-key helpers for the shared conveyor and the lane or exit being released.</summary>
public static class ReleaseResources
{
    /// <summary>Lock key for the booth A fast-lane path.</summary>
    public const string ExitA = "exit:A";

    /// <summary>Lock key for a buffer lane. Paired with the request's shared resource during a commit.</summary>
    public static string Queue(string queueId)
    {
        if (string.IsNullOrWhiteSpace(queueId))
        {
            throw new ArgumentException("Queue id is required.", nameof(queueId));
        }

        return "queue:" + queueId.Trim();
    }
}
