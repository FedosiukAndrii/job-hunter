using JobHunter.Domain.Common;
using JobHunter.Domain.Sources;

namespace JobHunter.Domain.Jobs;

public sealed class Job : IConcurrencyTracked
{
    private Job()
    {
    }

    public Guid Id { get; private set; }

    public SourceName Source { get; private set; }

    public string? SourceJobId { get; private set; }

    public string CanonicalUrl { get; private set; } = string.Empty;

    public string SourceUrl { get; private set; } = string.Empty;

    public string? ApplicationUrl { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Company { get; private set; } = string.Empty;

    public string DescriptionHtml { get; private set; } = string.Empty;

    public string DescriptionText { get; private set; } = string.Empty;

    public string LocationsJson { get; private set; } = "[]";

    public WorkplaceMode WorkplaceMode { get; private set; }

    public EmploymentType EmploymentType { get; private set; }

    public string? Seniority { get; private set; }

    public string SkillsJson { get; private set; } = "[]";

    public string CategoriesJson { get; private set; } = "[]";

    public decimal? CompensationMinimum { get; private set; }

    public decimal? CompensationMaximum { get; private set; }

    public string? CompensationCurrency { get; private set; }

    public CompensationPeriod CompensationPeriod { get; private set; }

    public DateTimeOffset? PublishedAtUtc { get; private set; }

    public PublishedAtPrecision PublishedAtPrecision { get; private set; }

    public string ContentHash { get; private set; } = string.Empty;

    public string Fingerprint { get; private set; } = string.Empty;

    public int FingerprintVersion { get; private set; }

    public DateTimeOffset FirstSeenAtUtc { get; private set; }

    public DateTimeOffset LastSeenAtUtc { get; private set; }

    public DateTimeOffset LastCheckedAtUtc { get; private set; }

    public JobLifecycle Lifecycle { get; private set; }

    public string? StatusReason { get; private set; }

    public long ConcurrencyVersion { get; set; }

    public ICollection<JobRevision> Revisions { get; } = [];

    public ICollection<JobObservation> Observations { get; } = [];

    public static Job Create(
        SourceName source,
        string? sourceJobId,
        string canonicalUrl,
        string sourceUrl,
        string? applicationUrl,
        string title,
        string company,
        string descriptionHtml,
        string descriptionText,
        string locationsJson,
        WorkplaceMode workplaceMode,
        EmploymentType employmentType,
        string? seniority,
        string skillsJson,
        string categoriesJson,
        decimal? compensationMinimum,
        decimal? compensationMaximum,
        string? compensationCurrency,
        CompensationPeriod compensationPeriod,
        DateTimeOffset? publishedAtUtc,
        PublishedAtPrecision publishedAtPrecision,
        string contentHash,
        string fingerprint,
        int fingerprintVersion,
        DateTimeOffset observedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);

        return new Job
        {
            Id = Guid.NewGuid(),
            Source = source,
            SourceJobId = sourceJobId,
            CanonicalUrl = canonicalUrl,
            SourceUrl = sourceUrl,
            ApplicationUrl = applicationUrl,
            Title = title,
            Company = company,
            DescriptionHtml = descriptionHtml,
            DescriptionText = descriptionText,
            LocationsJson = locationsJson,
            WorkplaceMode = workplaceMode,
            EmploymentType = employmentType,
            Seniority = seniority,
            SkillsJson = skillsJson,
            CategoriesJson = categoriesJson,
            CompensationMinimum = compensationMinimum,
            CompensationMaximum = compensationMaximum,
            CompensationCurrency = compensationCurrency,
            CompensationPeriod = compensationPeriod,
            PublishedAtUtc = publishedAtUtc,
            PublishedAtPrecision = publishedAtPrecision,
            ContentHash = contentHash,
            Fingerprint = fingerprint,
            FingerprintVersion = fingerprintVersion,
            FirstSeenAtUtc = observedAtUtc,
            LastSeenAtUtc = observedAtUtc,
            LastCheckedAtUtc = observedAtUtc,
            Lifecycle = JobLifecycle.Active
        };
    }

    public JobRevision? ApplyObservation(
        string sourceUrl,
        string? applicationUrl,
        string title,
        string company,
        string descriptionHtml,
        string descriptionText,
        string locationsJson,
        WorkplaceMode workplaceMode,
        EmploymentType employmentType,
        string? seniority,
        string skillsJson,
        string categoriesJson,
        decimal? compensationMinimum,
        decimal? compensationMaximum,
        string? compensationCurrency,
        CompensationPeriod compensationPeriod,
        DateTimeOffset? publishedAtUtc,
        PublishedAtPrecision publishedAtPrecision,
        string contentHash,
        string fingerprint,
        int fingerprintVersion,
        string previousSnapshotJson,
        DateTimeOffset observedAtUtc)
    {
        LastSeenAtUtc = observedAtUtc;
        LastCheckedAtUtc = observedAtUtc;

        if (string.Equals(ContentHash, contentHash, StringComparison.Ordinal))
        {
            return null;
        }

        var revision = JobRevision.Create(
            Id,
            Revisions.Count + 1,
            ContentHash,
            previousSnapshotJson,
            observedAtUtc);
        Revisions.Add(revision);

        SourceUrl = sourceUrl;
        ApplicationUrl = applicationUrl;
        Title = title;
        Company = company;
        DescriptionHtml = descriptionHtml;
        DescriptionText = descriptionText;
        LocationsJson = locationsJson;
        WorkplaceMode = workplaceMode;
        EmploymentType = employmentType;
        Seniority = seniority;
        SkillsJson = skillsJson;
        CategoriesJson = categoriesJson;
        CompensationMinimum = compensationMinimum;
        CompensationMaximum = compensationMaximum;
        CompensationCurrency = compensationCurrency;
        CompensationPeriod = compensationPeriod;
        PublishedAtUtc = publishedAtUtc;
        PublishedAtPrecision = publishedAtPrecision;
        ContentHash = contentHash;
        Fingerprint = fingerprint;
        FingerprintVersion = fingerprintVersion;
        Lifecycle = JobLifecycle.Active;
        StatusReason = null;

        return revision;
    }
}
