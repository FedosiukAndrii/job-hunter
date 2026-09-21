namespace JobHunter.JobSources.JobSpy.Configuration;

public sealed class JobSpyOptions
{
    public const string SectionName = "Sources:LinkedInJobSpy";

    public bool Enabled { get; set; }

    public bool ExperimentalAcknowledged { get; set; }

    public string Endpoint { get; set; } = "http://127.0.0.1:8080/";

    public string SearchTerm { get; set; } = ".NET";

    public string? Location { get; set; }

    public int MinimumIntervalMinutes { get; set; } = 60;

    public int RequestTimeoutSeconds { get; set; } = 45;

    public int MaximumResults { get; set; } = 50;

    public int MaximumResponseBytes { get; set; } = 2 * 1024 * 1024;

    public int BlockedBackoffHours { get; set; } = 24;
}
