namespace PaintQueueLocalAi;

/// <summary>
/// Exclusive ownership of one or more physical resources.
/// Disposing the token frees every resource it holds. Dispose is idempotent.
/// </summary>
public interface IReleaseToken : IDisposable, IAsyncDisposable
{
    /// <summary>Resources held, in the order they were acquired (ordinal id order).</summary>
    IReadOnlyList<string> ResourceIds { get; }

    /// <summary>Id of this acquisition, for logs. Not a secret.</summary>
    Guid TokenId { get; }

    /// <summary>UTC time the last resource in the set was acquired.</summary>
    DateTimeOffset AcquiredAt { get; }
}
