namespace JobHunter.Application.Profiles;

public sealed record ProfileValidationError(
    string Path,
    string Message,
    string Remediation);

public sealed class CandidateProfileValidationException(
    IReadOnlyList<ProfileValidationError> errors,
    Exception? innerException = null)
    : Exception(CreateMessage(errors), innerException)
{
    public IReadOnlyList<ProfileValidationError> Errors { get; } = errors;

    private static string CreateMessage(IReadOnlyList<ProfileValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return errors.Count == 0
            ? "The candidate profile is invalid."
            : string.Join(
                Environment.NewLine,
                errors.Select(
                    error =>
                        $"{error.Path}: {error.Message} Remediation: {error.Remediation}"));
    }
}

public static class CandidateProfileValidator
{
    public static void Validate(CandidateProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var errors = new List<ProfileValidationError>();

        if (profile.SchemaVersion != CandidateProfile.CurrentSchemaVersion)
        {
            errors.Add(
                new ProfileValidationError(
                    "$.schemaVersion",
                    $"Unsupported schema version '{profile.SchemaVersion}'.",
                    $"Set schemaVersion to {CandidateProfile.CurrentSchemaVersion}."));
        }

        ValidateRequiredList(profile.TargetTitles, "$.targetTitles", errors);
        ValidateUniqueValues(profile.RequiredSkills, "$.requiredSkills", errors);
        ValidateHardFilters(profile.HardFilters, errors);
        ValidateAiPreferences(profile.AiPreferences, errors);

        if (profile.SupplementalCvPath?.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            errors.Add(
                new ProfileValidationError(
                    "$.supplementalCvPath",
                    "The path contains invalid control characters.",
                    "Use a normal absolute path or a path relative to the profile file."));
        }

        if (errors.Count > 0)
        {
            throw new CandidateProfileValidationException(errors);
        }
    }

    private static void ValidateHardFilters(
        HardFilters? filters,
        List<ProfileValidationError> errors)
    {
        if (filters is null)
        {
            errors.Add(
                new ProfileValidationError(
                    "$.hardFilters",
                    "Hard filters cannot be null.",
                    "Use an empty hardFilters mapping when no explicit restriction is needed."));
            return;
        }

        ValidateUniqueValues(filters.Locations, "$.hardFilters.locations", errors);
        ValidateUniqueValues(
            filters.ExcludedEmployers,
            "$.hardFilters.excludedEmployers",
            errors);
        ValidateUniqueValues(
            filters.ExcludedKeywords,
            "$.hardFilters.excludedKeywords",
            errors);

        if (!Enum.IsDefined(filters.RemotePolicy))
        {
            errors.Add(
                new ProfileValidationError(
                    "$.hardFilters.remotePolicy",
                    "The remote policy is unsupported.",
                    "Use any, remoteOnly, remoteOrHybrid, or onSiteOnly."));
        }
    }

    private static void ValidateAiPreferences(
        List<string>? preferences,
        List<ProfileValidationError> errors)
    {
        const int maximumPreferences = 12;
        const int maximumPreferenceCharacters = 500;

        if (preferences is null)
        {
            errors.Add(
                new ProfileValidationError(
                    "$.aiPreferences",
                    "AI preferences cannot be null.",
                    "Use an empty array when no additional preferences are needed."));
            return;
        }

        if (preferences.Count > maximumPreferences)
        {
            errors.Add(
                new ProfileValidationError(
                    "$.aiPreferences",
                    $"AI preferences cannot contain more than {maximumPreferences} entries.",
                    "Combine related preferences into no more than 12 concise entries."));
        }

        var uniquePreferences = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < preferences.Count; index++)
        {
            var preference = preferences[index];
            var path = $"$.aiPreferences[{index}]";
            if (string.IsNullOrWhiteSpace(preference))
            {
                errors.Add(
                    new ProfileValidationError(
                        path,
                        "An AI preference cannot be blank.",
                        "Provide a concise preference or remove this entry."));
                continue;
            }

            if (preference.Length > maximumPreferenceCharacters)
            {
                errors.Add(
                    new ProfileValidationError(
                        path,
                        $"An AI preference cannot exceed {maximumPreferenceCharacters} characters.",
                        "Split the preference into shorter concise entries."));
            }

            if (!uniquePreferences.Add(preference.Trim()))
            {
                errors.Add(
                    new ProfileValidationError(
                        path,
                        "AI preferences must be unique.",
                        "Remove the duplicate preference."));
            }
        }
    }

    private static void ValidateRequiredList(
        List<string>? values,
        string path,
        List<ProfileValidationError> errors)
    {
        if (values is null)
        {
            errors.Add(
                new ProfileValidationError(
                    path,
                    "The collection cannot be null.",
                    "Provide a YAML/JSON array with at least one value."));
            return;
        }

        if (values.Count == 0)
        {
            errors.Add(
                new ProfileValidationError(
                    path,
                    "At least one value is required.",
                    "Add one or more target titles."));
        }

        ValidateUniqueValues(values, path, errors);
    }

    private static void ValidateUniqueValues(
        List<string>? values,
        string path,
        List<ProfileValidationError> errors)
    {
        if (values is null)
        {
            errors.Add(
                new ProfileValidationError(
                    path,
                    "The collection cannot be null.",
                    "Use an empty array when no values are needed."));
            return;
        }

        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add(
                    new ProfileValidationError(
                        $"{path}[{index}]",
                        "The value cannot be blank.",
                        "Remove the entry or provide a non-empty value."));
            }
            else if (!unique.Add(value.Trim()))
            {
                errors.Add(
                    new ProfileValidationError(
                        $"{path}[{index}]",
                        $"Value '{value}' is duplicated.",
                        "Remove the duplicate value."));
            }
        }
    }
}
