namespace PaintQueueLocalAi;

/// <summary>Why a request was refused before any candidate was scored.</summary>
public enum SafetyHoldKind
{
    /// <summary>Release is allowed by the global interlocks.</summary>
    None = 0,

    /// <summary>E-stop is active. Nothing may move.</summary>
    EStop = 1,

    /// <summary>The downstream conveyor is not ready to accept a body.</summary>
    ConveyorNotReady = 2,

    /// <summary>The request's shared resource id is listed in the blocked set.</summary>
    SharedResourceBlocked = 3
}

/// <summary>
/// Plant interlocks sampled with the request. The engine treats this as a hard gate:
/// it never asks the advisor to override a hold.
/// </summary>
public sealed record SafetyInterlockState
{
    /// <summary>Clear interlocks: conveyor ready, e-stop released, no blocked resources.</summary>
    public static SafetyInterlockState Nominal { get; } = new();

    /// <summary>E-stop. Checked before the conveyor and before any resource id.</summary>
    public bool EStopActive { get; init; }

    /// <summary>Downstream conveyor can accept a body. Defaults to ready.</summary>
    public bool ConveyorReady { get; init; } = true;

    /// <summary>
    /// Resource ids that must not move. Compared ordinally with the shared resource id,
    /// a queue id, <see cref="ReleaseResources.Queue"/>, and <see cref="ReleaseResources.ExitA"/>.
    /// </summary>
    public IReadOnlyCollection<string> BlockedResourceIds { get; init; } = Array.Empty<string>();

    /// <summary>Optional operator text copied into the hold explanation.</summary>
    public string? Message { get; init; }

    /// <summary>True when <paramref name="resourceId"/> is in <see cref="BlockedResourceIds"/>.</summary>
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
    /// Global hold for this request, if any. Per-queue blocks are not global; those lanes are skipped later.
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
