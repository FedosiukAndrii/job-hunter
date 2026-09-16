using JobHunter.Domain.Jobs;

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
    private static readonly HashSet<string> LanguageLevels = new(
        ["A1", "A2", "B1", "B2", "C1", "C2", "Native"],
        StringComparer.OrdinalIgnoreCase);

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
        ValidateSkills(profile.Skills, errors);
        ValidateRolePreferences(profile.RolePreferences, errors);
        ValidateLanguages(profile.Languages, errors);
        ValidateSalary(profile.Salary, errors);
        ValidateScoring(profile.Scoring, errors);
        ValidateUniqueValues(profile.ExcludedEmployers, "$.excludedEmployers", errors);
        ValidateUniqueValues(profile.ExcludedKeywords, "$.excludedKeywords", errors);
        ValidateUniqueValues(profile.PreferredDomains, "$.preferredDomains", errors);

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

    private static void ValidateSkills(
        List<CandidateSkill>? skills,
        List<ProfileValidationError> errors)
    {
        if (skills is null)
        {
            errors.Add(
                new ProfileValidationError(
                    "$.skills",
                    "The skills collection cannot be null.",
                    "Provide a YAML/JSON array with at least one skill."));
            return;
        }

        if (skills.Count == 0)
        {
            errors.Add(
                new ProfileValidationError(
                    "$.skills",
                    "At least one skill is required.",
                    "Add explicit core or related skills with evidence."));
            return;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var skill in skills)
        {
            var path = $"$.skills[{index}]";
            if (string.IsNullOrWhiteSpace(skill.Name))
            {
                errors.Add(
                    new ProfileValidationError(
                        $"{path}.name",
                        "A skill name is required.",
                        "Provide a technology or competency name."));
            }
            else if (!names.Add(skill.Name.Trim()))
            {
                errors.Add(
                    new ProfileValidationError(
                        $"{path}.name",
                        $"Skill '{skill.Name}' is duplicated.",
                        "Merge evidence and aliases into one skill entry."));
            }

            if (skill.YearsExperience < 0 || skill.YearsExperience > 80)
            {
                errors.Add(
                    new ProfileValidationError(
                        $"{path}.yearsExperience",
                        "Years of experience must be between 0 and 80.",
                        "Use a verified non-negative number or omit the field."));
            }

            ValidateUniqueValues(skill.Aliases, $"{path}.aliases", errors);
            ValidateUniqueValues(skill.Evidence, $"{path}.evidence", errors);
            index++;
        }
    }

    private static void ValidateRolePreferences(
        RolePreferences? preferences,
        List<ProfileValidationError> errors)
    {
        if (preferences is null)
        {
            errors.Add(
                new ProfileValidationError(
                    "$.rolePreferences",
                    "Role preferences are required.",
                    "Add rolePreferences, using empty lists where no restriction is needed."));
            return;
        }

        ValidateUniqueValues(preferences.Seniorities, "$.rolePreferences.seniorities", errors);
        ValidateUniqueValues(preferences.Locations, "$.rolePreferences.locations", errors);

        if (!Enum.IsDefined(preferences.RemotePolicy))
        {
            errors.Add(
                new ProfileValidationError(
                    "$.rolePreferences.remotePolicy",
                    "The remote policy is unsupported.",
                    "Use any, remoteOnly, remoteOrHybrid, preferRemote, or onSiteOnly."));
        }

        if (preferences.EmploymentTypes is null)
        {
            errors.Add(
                new ProfileValidationError(
                    "$.rolePreferences.employmentTypes",
                    "Employment types cannot be null.",
                    "Use an empty array when no employment type restriction is needed."));
        }
        else if (preferences.EmploymentTypes.Distinct().Count()
                 != preferences.EmploymentTypes.Count)
        {
            errors.Add(
                new ProfileValidationError(
                    "$.rolePreferences.employmentTypes",
                    "Employment types must be unique.",
                    "Remove repeated employment type values."));
        }
    }

    private static void ValidateLanguages(
        List<LanguagePreference>? languages,
        List<ProfileValidationError> errors)
    {
        if (languages is null)
        {
            errors.Add(
                new ProfileValidationError(
                    "$.languages",
                    "Languages cannot be null.",
                    "Use an empty array when no language preference is needed."));
            return;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var language in languages)
        {
            var path = $"$.languages[{index}]";
            if (string.IsNullOrWhiteSpace(language.Name))
            {
                errors.Add(
                    new ProfileValidationError(
                        $"{path}.name",
                        "A language name is required.",
                        "Provide the language name."));
            }
            else if (!names.Add(language.Name.Trim()))
            {
                errors.Add(
                    new ProfileValidationError(
                        $"{path}.name",
                        $"Language '{language.Name}' is duplicated.",
                        "Keep one requirement per language."));
            }

            if (language.MinimumLevel is not null
                && !LanguageLevels.Contains(language.MinimumLevel))
            {
                errors.Add(
                    new ProfileValidationError(
                        $"{path}.minimumLevel",
                        $"Language level '{language.MinimumLevel}' is unsupported.",
                        "Use A1, A2, B1, B2, C1, C2, Native, or omit the level."));
            }

            index++;
        }
    }

    private static void ValidateSalary(
        SalaryExpectation? salary,
        List<ProfileValidationError> errors)
    {
        if (salary is null)
        {
            return;
        }

        if (salary.Minimum <= 0)
        {
            errors.Add(
                new ProfileValidationError(
                    "$.salary.minimum",
                    "The salary floor must be greater than zero.",
                    "Provide a positive verified amount or remove salary."));
        }

        if (salary.Currency.Length != 3
            || salary.Currency.Any(character => !char.IsAsciiLetterUpper(character)))
        {
            errors.Add(
                new ProfileValidationError(
                    "$.salary.currency",
                    "Currency must be a three-letter uppercase ISO-style code.",
                    "Use a value such as USD, EUR, or UAH."));
        }

        if (salary.Period == CompensationPeriod.Unknown)
        {
            errors.Add(
                new ProfileValidationError(
                    "$.salary.period",
                    "A salary period is required.",
                    "Use hour, month, or year."));
        }
    }

    private static void ValidateScoring(
        ScoringPreferences? scoring,
        List<ProfileValidationError> errors)
    {
        if (scoring is null)
        {
            errors.Add(
                new ProfileValidationError(
                    "$.scoring",
                    "Scoring configuration is required.",
                    "Add thresholds and weights, or use the documented defaults."));
            return;
        }

        ValidateScore(scoring.RulesOnlyThreshold, "$.scoring.rulesOnlyThreshold", errors);
        ValidateScore(scoring.RulesAndAiThreshold, "$.scoring.rulesAndAiThreshold", errors);
        if (scoring.Weights is null)
        {
            errors.Add(
                new ProfileValidationError(
                    "$.scoring.weights",
                    "Scoring weights cannot be null.",
                    "Provide all seven criterion weights."));
            return;
        }

        if (scoring.Weights.Total != 100)
        {
            errors.Add(
                new ProfileValidationError(
                    "$.scoring.weights",
                    $"Scoring weights total {scoring.Weights.Total}, not 100.",
                    "Adjust the seven criterion weights so they sum to 100."));
        }

        var weights = new Dictionary<string, int>
        {
            ["coreSkills"] = scoring.Weights.CoreSkills,
            ["seniority"] = scoring.Weights.Seniority,
            ["relatedStack"] = scoring.Weights.RelatedStack,
            ["roleResponsibilities"] = scoring.Weights.RoleResponsibilities,
            ["locationLanguage"] = scoring.Weights.LocationLanguage,
            ["domain"] = scoring.Weights.Domain,
            ["compensation"] = scoring.Weights.Compensation
        };
        foreach (var (name, value) in weights)
        {
            if (value < 0)
            {
                errors.Add(
                    new ProfileValidationError(
                        $"$.scoring.weights.{name}",
                        "A criterion weight cannot be negative.",
                        "Use a value from 0 to 100."));
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
                    "Add one or more explicit values."));
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
        var index = 0;
        foreach (var value in values)
        {
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

            index++;
        }
    }

    private static void ValidateScore(
        int value,
        string path,
        List<ProfileValidationError> errors)
    {
        if (value is < 0 or > 100)
        {
            errors.Add(
                new ProfileValidationError(
                    path,
                    "A threshold must be between 0 and 100.",
                    "Choose an inclusive score from 0 to 100."));
        }
    }
}
