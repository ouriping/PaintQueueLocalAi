namespace PaintQueueLocalAi;

/// <summary>共享输送链，以及正在放行的车道或出口的锁键辅助方法。</summary>
public static class ReleaseResources
{
    /// <summary>喷房 A 口快车道的锁键。</summary>
    public const string ExitA = "exit:A";

    /// <summary>缓存车道的锁键。提交放行时与请求的共享资源成对使用。</summary>
    public static string Queue(string queueId)
    {
        if (string.IsNullOrWhiteSpace(queueId))
        {
            throw new ArgumentException("Queue id is required.", nameof(queueId));
        }

        return "queue:" + queueId.Trim();
    }
}
