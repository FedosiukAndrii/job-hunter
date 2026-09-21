using System.Text.Json.Serialization;

namespace JobHunter.JobSources.JobSpy.Contracts;

internal sealed record JobSpySearchRequest
{
    public required string Source { get; init; }

    public required string SearchTerm { get; init; }

    public string? Location { get; init; }

    public int ResultsWanted { get; init; }

    public int HoursOld { get; init; }
}

internal sealed record JobSpySearchResponse
{
    [JsonRequired]
    public required string Status { get; init; }

    [JsonRequired]
    public required List<JobSpyJob> Jobs { get; init; }

    public JobSpyError? Error { get; init; }

    public string? ProviderVersion { get; init; }
}

internal sealed record JobSpyJob
{
    [JsonRequired]
    public required string Source { get; init; }

    [JsonRequired]
    public required string SourceJobId { get; init; }

    [JsonRequired]
    public required string SourceUrl { get; init; }

    [JsonRequired]
    public required string Title { get; init; }

    [JsonRequired]
    public required string Company { get; init; }

    [JsonRequired]
    public required string Description { get; init; }

    public string? Location { get; init; }

    public string? WorkplaceMode { get; init; }

    public string? EmploymentType { get; init; }

    public string? Seniority { get; init; }

    public List<string>? Skills { get; init; }

    public List<string>? Categories { get; init; }

    public decimal? CompensationMinimum { get; init; }

    public decimal? CompensationMaximum { get; init; }

    public string? CompensationCurrency { get; init; }

    public string? CompensationPeriod { get; init; }

    public DateTimeOffset? PublishedAtUtc { get; init; }

    public string? ApplicationUrl { get; init; }
}

internal sealed record JobSpyError
{
    [JsonRequired]
    public required string Code { get; init; }

    [JsonRequired]
    public required string Message { get; init; }

    public int? RetryAfterSeconds { get; init; }

    public string? Action { get; init; }
}

internal sealed record JobSpyHealthResponse
{
    [JsonRequired]
    public required string Status { get; init; }
}

internal sealed record JobSpyVersionResponse
{
    [JsonRequired]
    public required string ServiceVersion { get; init; }

    [JsonRequired]
    public required string ContractVersion { get; init; }
}
