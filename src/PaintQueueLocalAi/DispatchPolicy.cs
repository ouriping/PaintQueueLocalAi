namespace PaintQueueLocalAi;

/// <summary>
/// Knobs for the deterministic score. Defaults match the paint-line contract:
/// fast lane is a higher tier, min-sequence outweighs load balance, and an urgent request
/// multiplies the sequence weight by 0.2.
/// </summary>
public sealed record DispatchPolicy
{
    /// <summary>Policy used when an engine is constructed without one.</summary>
    public static DispatchPolicy Default { get; } = new();

    /// <summary>
    /// When true, an open exit-A fast lane outranks every buffer head regardless of sequence.
    /// When false, a body on exit A is not a direct-release candidate.
    /// </summary>
    public bool EnableFastLaneA { get; init; } = true;

    /// <summary>Weight of sequence closeness in the non-urgent score. Must be zero or positive.</summary>
    public decimal MinSequenceWeight { get; init; } = 400m;

    /// <summary>
    /// Multiplier applied to <see cref="MinSequenceWeight"/> when the request is urgent.
    /// Must be from 0 to 1 so an urgent request cannot increase the sequence bias.
    /// </summary>
    public decimal UrgentMinSequenceWeightFactor { get; init; } = 0.2m;

    /// <summary>Weight of buffer fill ratio. Must be zero or positive. The fast lane does not earn this term.</summary>
    public decimal LoadBalanceWeight { get; init; } = 150m;

    /// <summary>How many rule-ranked candidates are shown to <see cref="ILlmAdvisor"/>.</summary>
    public int AdvisorTopK { get; init; } = 3;

    /// <summary>
    /// Budget for one advisor call. The default is short on purpose: the target PC has no discrete GPU.
    /// When the budget is exceeded the rule order is kept.
    /// </summary>
    public TimeSpan AdvisorTimeout { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Throws <see cref="ArgumentOutOfRangeException"/> when a knob is outside its contract.</summary>
    public void EnsureValid()
    {
        if (MinSequenceWeight < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(MinSequenceWeight), MinSequenceWeight, "Min-sequence weight cannot be negative.");
        }

        if (LoadBalanceWeight < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(LoadBalanceWeight), LoadBalanceWeight, "Load-balance weight cannot be negative.");
        }

        if (UrgentMinSequenceWeightFactor < 0m || UrgentMinSequenceWeightFactor > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(UrgentMinSequenceWeightFactor),
                UrgentMinSequenceWeightFactor,
                "Urgent min-sequence factor must be from 0 to 1 so urgency cannot raise the sequence weight.");
        }

        if (AdvisorTopK < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(AdvisorTopK), AdvisorTopK, "Advisor top-K must be at least 1.");
        }

        if (AdvisorTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(AdvisorTimeout), AdvisorTimeout, "Advisor timeout must be positive.");
        }
    }
}
