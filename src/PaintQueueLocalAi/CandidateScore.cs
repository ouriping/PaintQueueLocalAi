namespace PaintQueueLocalAi;

/// <summary>可放行车身的来源。</summary>
public enum CandidateOrigin
{
    /// <summary>缓存车道的车头（下标 0）。车头后面的车身不会出现。</summary>
    BufferHead = 0,

    /// <summary>停在喷房出口 A、且快车道已打开的车身。</summary>
    FastLaneA = 1
}

/// <summary>一辆可放行车身，以及给它排序的各项权重。</summary>
public sealed record CandidateScore
{
    /// <summary>
    /// 顾问重排时必须使用的编号。
    /// 缓存车头为 <c>buffer:{queueId}:{vehicleId}</c>。快车道为 <c>fast-lane-a:{vehicleId}</c>。
    /// </summary>
    public required string CandidateId { get; init; }

    /// <summary>车身编号。</summary>
    public required string VehicleId { get; init; }

    /// <summary>该车身的生产序号。</summary>
    public required int SequenceNumber { get; init; }

    /// <summary>缓存车头，或出口 A 的快车道。</summary>
    public required CandidateOrigin Origin { get; init; }

    /// <summary>缓存车头的车道编号；快车道为 null。</summary>
    public string? QueueId { get; init; }

    /// <summary>放行点所属喷房出口。快车道候选始终是 <see cref="BoothExitId.A"/>。</summary>
    public required BoothExitId SourceExit { get; init; }

    /// <summary>
    /// 硬档位。快车道为 1，缓存车头为 0。
    /// 规则排序先按档位，再按 <see cref="Score"/>。
    /// 已接受的顾问重排可以把较低档位排到前面。
    /// </summary>
    public required int PriorityTier { get; init; }

    /// <summary><see cref="SequenceComponent"/> 与 <see cref="LoadBalanceComponent"/> 之和。</summary>
    public required decimal Score { get; init; }

    /// <summary>实际使用的最小序号权重，乘以相对最小缓存车头序号的接近程度。</summary>
    public required decimal SequenceComponent { get; init; }

    /// <summary>负载均衡权重乘以车道充满率。快车道此项为 0。</summary>
    public required decimal LoadBalanceComponent { get; init; }

    /// <summary>车道充满率，范围 0 到 1。快车道不是缓存，此项为 0。</summary>
    public required decimal FillRatio { get; init; }

    /// <summary>规则排序及已接受的顾问重排之后的名次，从 1 起计。名次 1 被放行。</summary>
    public required int Rank { get; init; }

    /// <summary>供人机界面使用的 <see cref="Vehicle.ColorCode"/> 副本。</summary>
    public string? ColorCode { get; init; }
}
