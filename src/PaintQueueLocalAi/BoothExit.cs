namespace PaintQueueLocalAi;

/// <summary>Live state of one spray-booth exit at the moment of the request.</summary>
public sealed record BoothExit
{
    /// <summary>Which physical exit this snapshot describes.</summary>
    public required BoothExitId Id { get; init; }

    /// <summary>Body currently sitting on the exit, or null when the exit is clear.</summary>
    public Vehicle? PresentVehicle { get; init; }

    /// <summary>
    /// Direct-release path is open. Honored only for <see cref="BoothExitId.A"/>.
    /// The same flag on exit B is ignored.
    /// </summary>
    public bool FastLaneOpen { get; init; }

    /// <summary>Exit is mechanically held. A fast-lane candidate is not created while this is set.</summary>
    public bool ReleaseBlocked { get; init; }
}
