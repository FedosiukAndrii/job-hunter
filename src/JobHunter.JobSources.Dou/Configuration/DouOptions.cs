namespace JobHunter.JobSources.Dou.Configuration;

public sealed class DouOptions
{
    public const string SectionName = "Sources:Dou";

    public bool Enabled { get; set; } = true;

    public string FeedUrl { get; set; } =
        "https://jobs.dou.ua/vacancies/feeds/?category=.NET";

    public int DefaultIntervalMinutes { get; set; } = 12;

    public int RequestTimeoutSeconds { get; set; } = 30;

    public int MaximumResponseBytes { get; set; } = 2 * 1024 * 1024;

    public int MaximumItems { get; set; } = 200;

    public int MaximumDescriptionCharacters { get; set; } = 50_000;

    public bool DetailEnrichmentEnabled { get; set; }

    public int DetailMinimumIntervalSeconds { get; set; } = 2;
}
