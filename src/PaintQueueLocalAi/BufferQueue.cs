namespace PaintQueueLocalAi;

/// <summary>
/// 一条先进先出的缓存车道。下标 0 是车头，也是唯一可以放行的车身。
/// 车头后面的车身可供追溯，但不是候选。
/// </summary>
public sealed record BufferQueue
{
    /// <summary>车道编号，在一次请求内唯一。也通过 <see cref="ReleaseResources.Queue"/> 用作锁键。</summary>
    public required string Id { get; init; }

    /// <summary>下标 0 为车头，其后是后面的车身。车道没有可放行车身时为空。</summary>
    public IReadOnlyList<Vehicle> Vehicles { get; init; } = Array.Empty<Vehicle>();

    /// <summary>物理车位数量。充满率为 <c>Vehicles.Count / Capacity</c>，上限为 1。</summary>
    public int Capacity { get; init; } = 10;

    /// <summary>本车道的机械或质量保持。置位期间车头不是候选。</summary>
    public bool ReleaseBlocked { get; init; }

    /// <summary>可选的操作员原因。引擎不解释该文本。</summary>
    public string? BlockReason { get; init; }
}
