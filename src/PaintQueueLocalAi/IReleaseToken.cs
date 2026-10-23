namespace PaintQueueLocalAi;

/// <summary>
/// 对一个或多个物理资源的独占持有。
/// 释放令牌即放开它持有的全部资源。Dispose 是幂等的。
/// </summary>
public interface IReleaseToken : IDisposable, IAsyncDisposable
{
    /// <summary>持有的资源，顺序为获取顺序（编号的序数顺序）。</summary>
    IReadOnlyList<string> ResourceIds { get; }

    /// <summary>本次获取的编号，供日志使用。不是密钥。</summary>
    Guid TokenId { get; }

    /// <summary>集合中最后一个资源被获取的 UTC 时间。</summary>
    DateTimeOffset AcquiredAt { get; }
}
