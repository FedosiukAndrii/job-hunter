namespace JobHunter.Infrastructure.Configuration;

public sealed class RetentionOptions
{
    public const string SectionName = "Retention";

    public bool Enabled { get; set; } = true;

    public int OperationalRecordDays { get; set; } = 30;

    public int CleanupIntervalHours { get; set; } = 24;
}
