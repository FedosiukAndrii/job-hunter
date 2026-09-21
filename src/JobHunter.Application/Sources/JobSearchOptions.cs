namespace JobHunter.Application.Sources;

public sealed class JobSearchOptions
{
    public const string SectionName = "Search";

    public int LookbackHours { get; set; } = 24;
}
