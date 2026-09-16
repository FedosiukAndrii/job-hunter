using JobHunter.Domain.Jobs;
using JobHunter.Domain.Sources;

namespace JobHunter.Application.Sources;

public interface IJobSource
{
    SourceName Name { get; }

    Task<JobSourceResult> FetchAsync(
        JobSourceRequest request,
        CancellationToken cancellationToken);
}

public sealed record JobSourceRequest(
    Guid SubscriptionId,
    Uri Endpoint,
    string? QueryId,
    SourceCursorValue? Cursor,
    int MaximumItems);

public sealed record SourceCursorValue(
    string? EntityTag,
    DateTimeOffset? LastModifiedAtUtc,
    string? OpaqueValue);

public sealed record JobSourceResult(
    SourceRunStatus Status,
    IReadOnlyList<JobSourceRecord> Jobs,
    SourceCursorValue? Cursor,
    bool NotModified,
    DateTimeOffset? NextPollNotBeforeUtc,
    DateTimeOffset? RetryAfterUtc,
    string? ErrorCode,
    string? Diagnostic)
{
    public static JobSourceResult Succeeded(
        IReadOnlyList<JobSourceRecord> jobs,
        SourceCursorValue? cursor,
        DateTimeOffset? nextPollNotBeforeUtc = null,
        bool notModified = false) =>
        new(
            SourceRunStatus.Succeeded,
            jobs,
            cursor,
            notModified,
            nextPollNotBeforeUtc,
            null,
            null,
            null);

    public static JobSourceResult Failed(
        string errorCode,
        string diagnostic,
        DateTimeOffset? retryAfterUtc = null) =>
        new(
            SourceRunStatus.Failed,
            [],
            null,
            false,
            null,
            retryAfterUtc,
            errorCode,
            diagnostic);

    public static JobSourceResult Blocked(
        string errorCode,
        string diagnostic,
        DateTimeOffset? retryAfterUtc = null) =>
        new(
            SourceRunStatus.Blocked,
            [],
            null,
            false,
            null,
            retryAfterUtc,
            errorCode,
            diagnostic);
}

public sealed record JobSourceRecord
{
    public required SourceName Source { get; init; }

    public NativeSourceId? SourceJobId { get; init; }

    public required Uri SourceUrl { get; init; }

    public required CanonicalJobUrl CanonicalUrl { get; init; }

    public string? SourceGuid { get; init; }

    public required string Title { get; init; }

    public required string Company { get; init; }

    public required string DescriptionHtml { get; init; }

    public required string DescriptionText { get; init; }

    public IReadOnlyList<string> Locations { get; init; } = [];

    public WorkplaceMode WorkplaceMode { get; init; }

    public EmploymentType EmploymentType { get; init; }

    public string? Seniority { get; init; }

    public IReadOnlyList<string> Skills { get; init; } = [];

    public IReadOnlyList<string> Categories { get; init; } = [];

    public decimal? CompensationMinimum { get; init; }

    public decimal? CompensationMaximum { get; init; }

    public string? CompensationCurrency { get; init; }

    public CompensationPeriod CompensationPeriod { get; init; }

    public DateTimeOffset? PublishedAtUtc { get; init; }

    public PublishedAtPrecision PublishedAtPrecision { get; init; }

    public Uri? ApplicationUrl { get; init; }

    public required string ParserVersion { get; init; }

    public required string ContentHash { get; init; }

    public required string RawPayloadHash { get; init; }

    public required DateTimeOffset RetrievedAtUtc { get; init; }
}
