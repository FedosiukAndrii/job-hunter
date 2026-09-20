using System.Globalization;
using System.Text.Json;
using JobHunter.AI.Abstractions;
using JobHunter.Application.Persistence;
using JobHunter.Application.Profiles;
using JobHunter.Application.Sources;
using JobHunter.Domain.Jobs;

namespace JobHunter.Application.Evaluation;

public static class AiEvaluationPolicy
{
    public const string Version = "ai-fit-v1";

    public static IReadOnlyList<JobAnalysisCriterionDefinition> Criteria { get; } =
        [new("overallFit", 100)];
}

public static class JobAnalysisRequestFactory
{
    private const int PromptEnvelopeReserve = 8_000;
    private const int MaximumDescriptionCharacters = 24_000;
    private const int MaximumCvCharacters = 12_000;
    private const int MaximumAiPreferencesCharacters = 4_000;
    private static readonly JsonSerializerOptions JsonOptions =
        JobAnalysisJson.CreateSerializerOptions();

    public static JobAnalysisRequest Create(
        LoadedCandidateProfile loadedProfile,
        CandidateProfileSnapshotReference profileSnapshot,
        PersistedJob job,
        JobAnalyzerCapabilities capabilities,
        DateTimeOffset now,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(loadedProfile);
        ArgumentNullException.ThrowIfNull(profileSnapshot);
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(capabilities);

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            capabilities.MaximumInputCharacters,
            PromptEnvelopeReserve,
            nameof(capabilities));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            timeout,
            TimeSpan.Zero);

        var profile = loadedProfile.Profile;
        var collector = new EvidenceCollector(
            capabilities.MaximumInputCharacters - PromptEnvelopeReserve);
        collector.Add(
            "profile:target-titles",
            JobAnalysisEvidenceSource.Profile,
            Join(profile.TargetTitles));
        collector.Add(
            "profile:required-skills",
            JobAnalysisEvidenceSource.Profile,
            Join(profile.RequiredSkills));
        collector.Add(
            "profile:hard-filters",
            JobAnalysisEvidenceSource.Profile,
            $"locations={Join(profile.HardFilters.Locations)}; "
            + $"remote={profile.HardFilters.RemotePolicy}");
        collector.Add(
            "profile:ai-preferences",
            JobAnalysisEvidenceSource.Profile,
            string.Join(
                Environment.NewLine,
                profile.AiPreferences.Select(preference => $"- {preference}")),
            MaximumAiPreferencesCharacters);
        collector.Add(
            "profile:supplemental-cv",
            JobAnalysisEvidenceSource.Profile,
            loadedProfile.SupplementalCvRedacted is null
                ? string.Empty
                : SensitiveTextRedactor.Redact(loadedProfile.SupplementalCvRedacted),
            MaximumCvCharacters);

        var record = job.Record;
        collector.Add("job:title", JobAnalysisEvidenceSource.Job, record.Title);
        collector.Add("job:company", JobAnalysisEvidenceSource.Job, record.Company);
        collector.Add(
            "job:description",
            JobAnalysisEvidenceSource.Job,
            record.DescriptionText,
            MaximumDescriptionCharacters);
        collector.Add(
            "job:locations",
            JobAnalysisEvidenceSource.Job,
            Join(record.Locations));
        collector.Add(
            "job:workplace-mode",
            JobAnalysisEvidenceSource.Job,
            record.WorkplaceMode.ToString());
        collector.Add(
            "job:employment-type",
            JobAnalysisEvidenceSource.Job,
            record.EmploymentType.ToString());
        collector.Add(
            "job:seniority",
            JobAnalysisEvidenceSource.Job,
            record.Seniority ?? string.Empty);
        collector.Add(
            "job:skills",
            JobAnalysisEvidenceSource.Job,
            Join(record.Skills));
        collector.Add(
            "job:categories",
            JobAnalysisEvidenceSource.Job,
            Join(record.Categories));
        collector.Add(
            "job:compensation",
            JobAnalysisEvidenceSource.Job,
            FormatCompensation(record));

        return new JobAnalysisRequest(
            job.JobId,
            profileSnapshot.Id,
            job.RevisionNumber,
            JobAnalysisSchema.Version,
            AiEvaluationPolicy.Version,
            now.Add(timeout),
            AiEvaluationPolicy.Criteria,
            collector.Fragments,
            collector.WasTruncated ? ["EvidenceTruncated"] : []);
    }

    private static string FormatCompensation(JobSourceRecord job) =>
        job.CompensationMinimum is null && job.CompensationMaximum is null
            ? string.Empty
            : $"minimum={FormatDecimal(job.CompensationMinimum)}; "
                + $"maximum={FormatDecimal(job.CompensationMaximum)}; "
                + $"currency={job.CompensationCurrency ?? "unspecified"}; "
                + $"period={job.CompensationPeriod}";

    private static string FormatDecimal(decimal? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "unspecified";

    private static string Join<T>(IEnumerable<T> values) =>
        string.Join(", ", values);

    private sealed class EvidenceCollector(int maximumCharacters)
    {
        private readonly List<JobAnalysisEvidenceFragment> _fragments = [];
        private int _remainingCharacters = maximumCharacters;

        public IReadOnlyList<JobAnalysisEvidenceFragment> Fragments => _fragments;

        public bool WasTruncated { get; private set; }

        public void Add(
            string id,
            JobAnalysisEvidenceSource source,
            string content,
            int maximumContentCharacters = 4_000)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            var normalized = SensitiveTextRedactor.Redact(content.Trim());
            var desiredLength = Math.Min(normalized.Length, maximumContentCharacters);
            var overhead = id.Length + 32;
            var available = Math.Max(0, _remainingCharacters - overhead);
            var acceptedLength = GetAcceptedLength(
                normalized,
                desiredLength,
                available);
            if (acceptedLength == 0)
            {
                WasTruncated = true;
                return;
            }

            if (acceptedLength < normalized.Length)
            {
                WasTruncated = true;
            }

            _fragments.Add(
                new JobAnalysisEvidenceFragment(
                    id,
                    source,
                    normalized[..acceptedLength]));
            _remainingCharacters -= overhead
                + GetSerializedLength(normalized[..acceptedLength]);
        }

        private static int GetAcceptedLength(
            string value,
            int maximumRawLength,
            int maximumSerializedLength)
        {
            var lower = 0;
            var upper = maximumRawLength;
            while (lower < upper)
            {
                var candidate = lower + (upper - lower + 1) / 2;
                if (GetSerializedLength(value[..candidate])
                    <= maximumSerializedLength)
                {
                    lower = candidate;
                }
                else
                {
                    upper = candidate - 1;
                }
            }

            if (lower > 0
                && lower < value.Length
                && char.IsHighSurrogate(value[lower - 1])
                && char.IsLowSurrogate(value[lower]))
            {
                lower--;
            }

            return lower;
        }

        private static int GetSerializedLength(string value) =>
            JsonSerializer.Serialize(value, JsonOptions).Length - 2;
    }
}
