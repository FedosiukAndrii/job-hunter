namespace JobHunter.Infrastructure.Configuration;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string? DataDirectory { get; set; }

    public string DatabaseFileName { get; set; } = "job-hunter.db";

    public int BusyTimeoutSeconds { get; set; } = 5;
}
