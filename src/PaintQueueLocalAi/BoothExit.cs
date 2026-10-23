namespace PaintQueueLocalAi;

/// <summary>请求时刻某一喷房出口的现场状态。</summary>
public sealed record BoothExit
{
    /// <summary>本快照对应的物理出口。</summary>
    public required BoothExitId Id { get; init; }

    /// <summary>当前停在出口上的车身；出口空闲时为 null。</summary>
    public Vehicle? PresentVehicle { get; init; }

    /// <summary>
    /// 直放通道已打开。仅对 <see cref="BoothExitId.A"/> 生效。
    /// 出口 B 上的同一标志会被忽略。
    /// </summary>
    public bool FastLaneOpen { get; init; }

    /// <summary>出口被机械保持。置位期间不会生成快车道候选。</summary>
    public bool ReleaseBlocked { get; init; }
}
