namespace PaintQueueLocalAi;

/// <summary>
/// One painted body. <see cref="SequenceNumber"/> is the production sequence, not the arrival order.
/// Smaller numbers should leave the buffer bank first when the line is not starving.
/// </summary>
public sealed record Vehicle
{
    /// <summary>Stable body id (body number, VIN, or carrier id). Unique inside one request snapshot.</summary>
    public required string Id { get; init; }

    /// <summary>Production sequence. The dispatch score prefers smaller values.</summary>
    public required int SequenceNumber { get; init; }

    /// <summary>Booth exit that painted this body. Informational; it does not by itself open the fast lane.</summary>
    public required BoothExitId SourceExit { get; init; }

    /// <summary>Optional color code copied onto the decision so an HMI can show it without revisiting the queue.</summary>
    public string? ColorCode { get; init; }

    /// <summary>Optional model or body-style code. Not used by the scorer.</summary>
    public string? ModelCode { get; init; }

    /// <summary>When the body entered its current slot. Not used by the scorer.</summary>
    public DateTimeOffset? ArrivedAt { get; init; }
}
