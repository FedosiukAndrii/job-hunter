namespace JobHunter.Worker;

public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    public int SchedulerTickSeconds { get; set; } = 60;

    public int ShutdownTimeoutSeconds { get; set; } = 30;
}
