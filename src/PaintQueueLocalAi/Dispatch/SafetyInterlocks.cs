namespace PaintQueueLocalAi;

public static class SafetyInterlocks
{
    /// <summary>烘房温度允许低于工艺要求的幅度。超过则该工单不进入候选。</summary>
    public const double OvenToleranceC = 5;

    public static InterlockKind? BoothBlock(BoothState booth)
    {
        ArgumentNullException.ThrowIfNull(booth);
        if (booth.EmergencyStop)
        {
            return InterlockKind.EmergencyStop;
        }

        if (!booth.ConveyorRunning)
        {
            return InterlockKind.ConveyorStopped;
        }

        if (!booth.BoothHealthy)
        {
            return InterlockKind.BoothFault;
        }

        if (!booth.OvenReady)
        {
            return InterlockKind.OvenNotReady;
        }

        return null;
    }

    public static bool TryAccept(PaintJob job, BoothState booth, out string reason)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(booth);

        if (job.QualityHold)
        {
            reason = "质量保留";
            return false;
        }

        if (!job.FlashOffComplete)
        {
            reason = "闪干未完成";
            return false;
        }

        if (job.Chemistry != booth.LoadedChemistry)
        {
            reason = "涂料体系不一致";
            return false;
        }

        if (job.RequiredOvenC > booth.OvenTempC + OvenToleranceC)
        {
            reason = "烘房温度不足";
            return false;
        }

        reason = "";
        return true;
    }
}

public static class InterlockText
{
    public static string ToChinese(InterlockKind kind) => kind switch
    {
        InterlockKind.EmergencyStop => "急停",
        InterlockKind.ConveyorStopped => "输送停止",
        InterlockKind.BoothFault => "喷房故障",
        InterlockKind.OvenNotReady => "烘房未就绪",
        _ => "联锁"
    };
}
