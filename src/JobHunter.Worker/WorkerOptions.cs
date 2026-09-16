namespace JobHunter.Worker;

public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    public int SchedulerTickSeconds { get; set; } = 60;

    public int ShutdownTimeoutSeconds { get; set; } = 30;

    public int MaximumConcurrentSources { get; set; } = 2;

    public int MaximumSubscriptionsPerTick { get; set; } = 16;

    public int SourceLeaseSeconds { get; set; } = 300;

    public int NotificationDispatchIntervalSeconds { get; set; } = 1;

    public int NotificationBatchSize { get; set; } = 20;

    public int NotificationLeaseSeconds { get; set; } = 120;

    public int NotificationMaximumAttempts { get; set; } = 5;

    public int NotificationMaximumRetrySeconds { get; set; } = 1800;
}
