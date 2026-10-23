namespace PaintQueueLocalAi;

/// <summary>
/// 一辆已涂装车身。<see cref="SequenceNumber"/> 是生产序号，不是到达顺序。
/// 产线不缺车时，较小的序号应先离开涂装缓存。
/// </summary>
public sealed record Vehicle
{
    /// <summary>稳定的车身编号（车身号、VIN 或载具号）。在一次请求快照内唯一。</summary>
    public required string Id { get; init; }

    /// <summary>生产序号。放行评分偏向较小的值。</summary>
    public required int SequenceNumber { get; init; }

    /// <summary>涂装该车身的喷房出口。仅供参考，本身不会打开快车道。</summary>
    public required BoothExitId SourceExit { get; init; }

    /// <summary>可选颜色代码，会复制到决策上，使人机界面不必再回看队列。</summary>
    public string? ColorCode { get; init; }

    /// <summary>可选车型或车身形式代码。评分不使用。</summary>
    public string? ModelCode { get; init; }

    /// <summary>车身进入当前车位的时刻。评分不使用。</summary>
    public DateTimeOffset? ArrivedAt { get; init; }
}
