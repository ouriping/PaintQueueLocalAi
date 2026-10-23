namespace PaintQueueLocalAi.Local;

public interface IAdvisorWarmup
{
    Task WarmUpAsync(CancellationToken cancellationToken);
}

public interface ILocalAdvisorStatus
{
    string Status { get; }
}
