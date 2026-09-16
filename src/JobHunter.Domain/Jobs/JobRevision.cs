namespace JobHunter.Domain.Jobs;

public sealed class JobRevision
{
    private JobRevision()
    {
    }

    public Guid Id { get; private set; }

    public Guid JobId { get; private set; }

    public Job Job { get; private set; } = null!;

    public int RevisionNumber { get; private set; }

    public string PreviousContentHash { get; private set; } = string.Empty;

    public string PreviousSnapshotJson { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static JobRevision Create(
        Guid jobId,
        int revisionNumber,
        string previousContentHash,
        string previousSnapshotJson,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            RevisionNumber = revisionNumber,
            PreviousContentHash = previousContentHash,
            PreviousSnapshotJson = previousSnapshotJson,
            CreatedAtUtc = now
        };
}
