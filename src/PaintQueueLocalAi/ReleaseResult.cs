namespace PaintQueueLocalAi;

/// <summary><see cref="IDispatchEngine.CommitReleaseAsync"/> 的结果。</summary>
public sealed record ReleaseResult
{
    /// <summary>已提交的那次评估，或跳过放行闸的那次保持。</summary>
    public required DispatchDecision Decision { get; init; }

    /// <summary>物理放行回调在持有令牌期间被调用时为 true。</summary>
    public required bool Committed { get; init; }

    /// <summary>请求中的共享资源编号。</summary>
    public required string ResourceId { get; init; }

    /// <summary>本次提交取得的全部锁。<see cref="Committed"/> 为 false 时为空。</summary>
    public required IReadOnlyList<string> LockedResourceIds { get; init; }

    /// <summary>回调运行期间的令牌编号。未提交时为 null。</summary>
    public Guid? TokenId { get; init; }
}
