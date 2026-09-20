namespace JobHunter.Domain.Jobs;

public sealed class JobPossibleDuplicate
{
    private JobPossibleDuplicate()
    {
    }

    public Guid Id { get; private set; }

    public Guid FirstJobId { get; private set; }

    public Job FirstJob { get; private set; } = null!;

    public Guid SecondJobId { get; private set; }

    public Job SecondJob { get; private set; } = null!;

    public string MatchReason { get; private set; } = string.Empty;

    public decimal Confidence { get; private set; }

    public string Fingerprint { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? ReviewedAtUtc { get; private set; }

    public static JobPossibleDuplicate CreateCompanyTitlePublishedAtV2(
        Guid leftJobId,
        Guid rightJobId,
        string matchKey,
        DateTimeOffset now)
    {
        if (leftJobId == rightJobId)
        {
            throw new ArgumentException("A job cannot be a possible duplicate of itself.", nameof(rightJobId));
        }

        var first = leftJobId.CompareTo(rightJobId) < 0 ? leftJobId : rightJobId;
        var second = leftJobId.CompareTo(rightJobId) < 0 ? rightJobId : leftJobId;

        var possibleDuplicate = new JobPossibleDuplicate
        {
            Id = Guid.NewGuid(),
            FirstJobId = first,
            SecondJobId = second,
            CreatedAtUtc = now
        };
        possibleDuplicate.ApplyCompanyTitlePublishedAtV2(matchKey);
        return possibleDuplicate;
    }

    public void ApplyCompanyTitlePublishedAtV2(string matchKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(matchKey);
        MatchReason = CrossSourceJobDuplicateMatcher.CompanyTitlePublishedAtV2;
        Confidence = 1m;
        Fingerprint = matchKey;
    }
}
