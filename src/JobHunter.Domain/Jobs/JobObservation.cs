using JobHunter.Domain.Sources;

namespace JobHunter.Domain.Jobs;

public sealed class JobObservation
{
    private JobObservation()
    {
    }

    public Guid Id { get; private set; }

    public Guid JobId { get; private set; }

    public Job Job { get; private set; } = null!;

    public Guid SourceRunId { get; private set; }

    public SourceRun SourceRun { get; private set; } = null!;

    public Guid SourceSubscriptionId { get; private set; }

    public string SourceUrl { get; private set; } = string.Empty;

    public string? SourceGuid { get; private set; }

    public string ParserVersion { get; private set; } = string.Empty;

    public string ContentHash { get; private set; } = string.Empty;

    public string RawPayloadHash { get; private set; } = string.Empty;

    public string? QueryId { get; private set; }

    public DateTimeOffset RetrievedAtUtc { get; private set; }

    public static JobObservation Create(
        Guid jobId,
        Guid sourceRunId,
        Guid sourceSubscriptionId,
        string sourceUrl,
        string? sourceGuid,
        string parserVersion,
        string contentHash,
        string rawPayloadHash,
        string? queryId,
        DateTimeOffset retrievedAtUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            SourceRunId = sourceRunId,
            SourceSubscriptionId = sourceSubscriptionId,
            SourceUrl = sourceUrl,
            SourceGuid = sourceGuid,
            ParserVersion = parserVersion,
            ContentHash = contentHash,
            RawPayloadHash = rawPayloadHash,
            QueryId = queryId,
            RetrievedAtUtc = retrievedAtUtc
        };
}
