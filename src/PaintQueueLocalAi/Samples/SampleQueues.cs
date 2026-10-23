namespace PaintQueueLocalAi;

/// <summary>演示和测试共用的固定队列，不读取时钟。</summary>
public static class SampleQueues
{
    public const string MorningWinnerId = "W-URG";

    public static readonly DateTimeOffset MorningNow = new(2026, 10, 23, 8, 0, 0, TimeSpan.Zero);

    public static PaintQueueSample MorningShift()
    {
        var booth = new BoothState(
            BoothId: "B1",
            CurrentColorCode: "RAL9016",
            CurrentPartFamily: "门板",
            LoadedChemistry: PaintChemistry.Waterborne,
            EmergencyStop: false,
            OvenReady: true,
            BoothHealthy: true,
            ConveyorRunning: true,
            OvenTempC: 180,
            Now: MorningNow);

        var jobs = new PaintJob[]
        {
            Job("W-URG", "RAL9016", "白", PaintChemistry.Waterborne, JobPriority.Urgent, 30, "门板", 2, false, true, 160),
            Job("K-LATE", "RAL9005", "黑", PaintChemistry.Waterborne, JobPriority.Normal, -45, "保险杠", 1, false, true, 160),
            Job("W-NORM", "RAL9016", "白", PaintChemistry.Waterborne, JobPriority.Normal, 240, "翼子板", 3, false, true, 160),
            Job("R-HIGH", "RAL3020", "红", PaintChemistry.Waterborne, JobPriority.High, 20, "翼子板", 1, false, true, 160),
            Job("W-HOLD", "RAL9016", "白", PaintChemistry.Waterborne, JobPriority.Urgent, 10, "门板", 1, true, true, 160),
            Job("S-CHEM", "RAL9016", "白", PaintChemistry.Solvent, JobPriority.Urgent, 10, "门板", 1, false, true, 160),
            Job("W-FLASH", "RAL9016", "白", PaintChemistry.Waterborne, JobPriority.Urgent, 10, "门板", 1, false, false, 160),
            Job("W-HOT", "RAL9016", "白", PaintChemistry.Waterborne, JobPriority.Urgent, 10, "门板", 1, false, true, 200)
        };

        return new PaintQueueSample(booth, jobs);
    }

    public static PaintQueueSample EmergencyStop()
    {
        var sample = MorningShift();
        return sample with { Booth = sample.Booth with { EmergencyStop = true } };
    }

    private static PaintJob Job(
        string id,
        string color,
        string colorName,
        PaintChemistry chemistry,
        JobPriority priority,
        int dueMinutes,
        string family,
        int quantity,
        bool qualityHold,
        bool flashOffComplete,
        double requiredOvenC)
    {
        return new PaintJob(
            id,
            color,
            colorName,
            chemistry,
            priority,
            MorningNow.AddMinutes(dueMinutes),
            family,
            quantity,
            qualityHold,
            flashOffComplete,
            requiredOvenC);
    }
}
