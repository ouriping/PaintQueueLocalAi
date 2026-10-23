namespace PaintQueueLocalAi;

/// <summary>请求在任何候选评分之前被拒绝的原因。</summary>
public enum SafetyHoldKind
{
    /// <summary>全局联锁允许放行。</summary>
    None = 0,

    /// <summary>急停已动作。任何设备都不得移动。</summary>
    EStop = 1,

    /// <summary>下游输送链尚未准备好接收车身。</summary>
    ConveyorNotReady = 2,

    /// <summary>请求的共享资源编号位于封锁集合中。</summary>
    SharedResourceBlocked = 3
}

/// <summary>
/// 与请求一起采样的现场联锁。引擎把它当作硬闸：
/// 绝不会请顾问推翻一次保持。
/// </summary>
public sealed record SafetyInterlockState
{
    /// <summary>联锁解除：输送链就绪，急停已复位，没有被封锁的资源。</summary>
    public static SafetyInterlockState Nominal { get; } = new();

    /// <summary>急停。先于输送链、也先于任何资源编号检查。</summary>
    public bool EStopActive { get; init; }

    /// <summary>下游输送链可以接收车身。默认就绪。</summary>
    public bool ConveyorReady { get; init; } = true;

    /// <summary>
    /// 不得移动的资源编号。与共享资源编号、车道编号、
    /// <see cref="ReleaseResources.Queue"/> 和 <see cref="ReleaseResources.ExitA"/> 按序数比较。
    /// </summary>
    public IReadOnlyCollection<string> BlockedResourceIds { get; init; } = Array.Empty<string>();

    /// <summary>可选的操作员文本，会复制进保持说明。</summary>
    public string? Message { get; init; }

    /// <summary><paramref name="resourceId"/> 位于 <see cref="BlockedResourceIds"/> 中时为 true。</summary>
    public bool Blocks(string resourceId)
    {
        if (string.IsNullOrEmpty(resourceId) || BlockedResourceIds is null || BlockedResourceIds.Count == 0)
        {
            return false;
        }

        foreach (var blocked in BlockedResourceIds)
        {
            if (string.Equals(blocked, resourceId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 本次请求的全局保持（如果有）。按车道封锁不是全局保持；那些车道稍后会被跳过。
    /// </summary>
    public SafetyHoldKind GetHold(string sharedResourceId)
    {
        if (EStopActive)
        {
            return SafetyHoldKind.EStop;
        }

        if (!ConveyorReady)
        {
            return SafetyHoldKind.ConveyorNotReady;
        }

        if (Blocks(sharedResourceId))
        {
            return SafetyHoldKind.SharedResourceBlocked;
        }

        return SafetyHoldKind.None;
    }
}
