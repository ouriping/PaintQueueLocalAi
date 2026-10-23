namespace PaintQueueLocalAi;

/// <summary>喷房当前装载的涂料体系。跨体系工单不会进入候选。</summary>
public enum PaintChemistry
{
    Waterborne = 0,
    Solvent = 1,
    Powder = 2
}

public enum JobPriority
{
    Normal = 0,
    High = 1,
    Urgent = 2
}

/// <summary>工位级硬联锁。命中后本轮不调度，也不询问模型。</summary>
public enum InterlockKind
{
    EmergencyStop = 1,
    ConveyorStopped = 2,
    BoothFault = 3,
    OvenNotReady = 4
}

public sealed record PaintJob(
    string JobId,
    string ColorCode,
    string ColorName,
    PaintChemistry Chemistry,
    JobPriority Priority,
    DateTimeOffset DueAt,
    string PartFamily,
    int Quantity,
    bool QualityHold,
    bool FlashOffComplete,
    double RequiredOvenC);

public sealed record BoothState(
    string BoothId,
    string? CurrentColorCode,
    string? CurrentPartFamily,
    PaintChemistry LoadedChemistry,
    bool EmergencyStop,
    bool OvenReady,
    bool BoothHealthy,
    bool ConveyorRunning,
    double OvenTempC,
    DateTimeOffset Now);

public sealed record RankedCandidate(
    string CandidateId,
    PaintJob Job,
    int Score,
    int Rank,
    IReadOnlyList<string> Reasons);

public sealed record ExcludedJob(string JobId, string Reason);

public sealed record PaintQueueSample(BoothState Booth, IReadOnlyList<PaintJob> Jobs);
