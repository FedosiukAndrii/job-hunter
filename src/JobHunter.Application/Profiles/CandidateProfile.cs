namespace JobHunter.Application.Profiles;

public sealed class CandidateProfile
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<string> TargetTitles { get; set; } = [];

    public List<string> RequiredSkills { get; set; } = [];

    public HardFilters HardFilters { get; set; } = new();

    public List<string> AiPreferences { get; set; } = [];

    public string? SupplementalCvPath { get; set; }
}

public sealed class HardFilters
{
    public List<string> Locations { get; set; } = [];

    public RemotePolicy RemotePolicy { get; set; } = RemotePolicy.Any;

    public List<string> ExcludedEmployers { get; set; } = [];

    public List<string> ExcludedKeywords { get; set; } = [];
}

public enum RemotePolicy
{
    Any = 0,
    RemoteOnly = 1,
    RemoteOrHybrid = 2,
    OnSiteOnly = 3
}
