using System.Text.Json;

namespace JobHunter.AI.Abstractions;

public static class JobAnalysisSubmissionValidator
{
    private static readonly JsonSerializerOptions JsonOptions =
        JobAnalysisJson.CreateSerializerOptions();

    public static JobAnalysisValidationResult Validate(
        JobAnalysisRequest request,
        JobAnalysisSubmission submission,
        int maximumOutputCharacters)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumOutputCharacters);

        if (!string.Equals(
                request.SchemaVersion,
                JobAnalysisSchema.Version,
                StringComparison.Ordinal))
        {
            return JobAnalysisValidationResult.Invalid("UnsupportedSchemaVersion");
        }

        if (submission.Confidence is not (>= 0 and <= 1))
        {
            return JobAnalysisValidationResult.Invalid("InvalidConfidence");
        }

        if (string.IsNullOrWhiteSpace(submission.Summary)
            || submission.Summary.Trim().Length > 1024)
        {
            return JobAnalysisValidationResult.Invalid("InvalidSummary");
        }

        if (submission.Criteria is null
            || submission.Criteria.Count != request.Criteria.Count)
        {
            return JobAnalysisValidationResult.Invalid("InvalidCriterionCount");
        }

        if (JsonSerializer.Serialize(submission, JsonOptions).Length
            > maximumOutputCharacters)
        {
            return JobAnalysisValidationResult.Invalid("OutputTooLarge");
        }

        var definitions = new Dictionary<
            string,
            JobAnalysisCriterionDefinition>(StringComparer.Ordinal);
        foreach (var definition in request.Criteria)
        {
            if (string.IsNullOrWhiteSpace(definition.Id)
                || !definitions.TryAdd(definition.Id, definition))
            {
                return JobAnalysisValidationResult.Invalid(
                    "InvalidCriterionDefinitions");
            }
        }

        if (definitions.Count == 0
            || definitions.Values.Any(definition => definition.Weight < 0)
            || definitions.Values.Sum(definition => definition.Weight) <= 0)
        {
            return JobAnalysisValidationResult.Invalid("InvalidCriterionDefinitions");
        }

        var evidenceIds = request.Evidence
            .Select(fragment => fragment.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (evidenceIds.Count != request.Evidence.Count)
        {
            return JobAnalysisValidationResult.Invalid("DuplicateEvidenceId");
        }

        var seenCriteria = new HashSet<string>(StringComparer.Ordinal);
        var validatedCriteria = new List<JobAnalysisCriterionResult>(
            submission.Criteria.Count);
        long weightedScore = 0;
        foreach (var criterion in submission.Criteria)
        {
            if (string.IsNullOrWhiteSpace(criterion.CriterionId)
                || !definitions.TryGetValue(criterion.CriterionId, out var definition)
                || !seenCriteria.Add(criterion.CriterionId))
            {
                return JobAnalysisValidationResult.Invalid("InvalidCriterionId");
            }

            if (criterion.Score is not (>= 0 and <= 100))
            {
                return JobAnalysisValidationResult.Invalid("InvalidCriterionScore");
            }

            if (criterion.Confidence is not (>= 0 and <= 1))
            {
                return JobAnalysisValidationResult.Invalid(
                    "InvalidCriterionConfidence");
            }

            if (criterion.InsufficientEvidence is null
                || criterion.EvidenceIds is null
                || criterion.MismatchReasons is null)
            {
                return JobAnalysisValidationResult.Invalid("IncompleteCriterion");
            }

            if (criterion.EvidenceIds.Count > 16
                || criterion.EvidenceIds.Distinct(StringComparer.Ordinal).Count()
                    != criterion.EvidenceIds.Count
                || criterion.EvidenceIds.Any(id => !evidenceIds.Contains(id)))
            {
                return JobAnalysisValidationResult.Invalid("InvalidEvidenceReference");
            }

            if (!criterion.InsufficientEvidence.Value
                && criterion.EvidenceIds.Count == 0)
            {
                return JobAnalysisValidationResult.Invalid("MissingEvidenceReference");
            }

            if (criterion.MismatchReasons.Count > 8
                || criterion.MismatchReasons.Any(
                    reason => string.IsNullOrWhiteSpace(reason)
                        || reason.Trim().Length > 256))
            {
                return JobAnalysisValidationResult.Invalid("InvalidMismatchReason");
            }

            weightedScore += (long)criterion.Score.Value * definition.Weight;
            validatedCriteria.Add(
                new JobAnalysisCriterionResult(
                    criterion.CriterionId,
                    criterion.Score.Value,
                    criterion.Confidence.Value,
                    [.. criterion.EvidenceIds],
                    [.. criterion.MismatchReasons.Select(reason => reason.Trim())],
                    criterion.InsufficientEvidence.Value));
        }

        var totalWeight = definitions.Values.Sum(definition => definition.Weight);
        var score = (int)Math.Round(
            weightedScore / (decimal)totalWeight,
            MidpointRounding.AwayFromZero);

        return JobAnalysisValidationResult.Valid(
            new JobAnalysisOutput(
                score,
                submission.Confidence.Value,
                submission.Summary.Trim(),
                validatedCriteria));
    }
}

public sealed record JobAnalysisValidationResult(
    bool IsValid,
    JobAnalysisOutput? Output,
    string? FailureCode)
{
    public static JobAnalysisValidationResult Valid(JobAnalysisOutput output) =>
        new(true, output, null);

    public static JobAnalysisValidationResult Invalid(string failureCode) =>
        new(false, null, failureCode);
}
