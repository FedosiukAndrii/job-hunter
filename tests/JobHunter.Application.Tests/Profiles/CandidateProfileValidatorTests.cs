using JobHunter.Application.Profiles;

namespace JobHunter.Application.Tests.Profiles;

public sealed class CandidateProfileValidatorTests
{
    [Fact]
    public void ValidateReportsFieldPathAndRemediation()
    {
        var profile = ValidProfile();
        profile.RequiredSkills.Add(".net");
        profile.AiPreferences = [" "];

        var exception = Assert.Throws<CandidateProfileValidationException>(
            () => CandidateProfileValidator.Validate(profile));

        Assert.Contains(exception.Errors, error => error.Path == "$.requiredSkills[1]");
        Assert.Contains(exception.Errors, error => error.Path == "$.aiPreferences[0]");
        Assert.All(exception.Errors, error => Assert.False(string.IsNullOrWhiteSpace(error.Remediation)));
    }

    [Fact]
    public void ValidateRejectsUnsupportedRemotePolicy()
    {
        var profile = ValidProfile();
        profile.HardFilters.RemotePolicy = (RemotePolicy)99;

        var exception = Assert.Throws<CandidateProfileValidationException>(
            () => CandidateProfileValidator.Validate(profile));

        Assert.Contains(
            exception.Errors,
            error => error.Path == "$.hardFilters.remotePolicy");
    }

    [Fact]
    public void ValidateRejectsBlankAndOversizedAiPreferences()
    {
        var profile = ValidProfile();
        profile.AiPreferences = [" ", new string('a', 501)];

        var exception = Assert.Throws<CandidateProfileValidationException>(
            () => CandidateProfileValidator.Validate(profile));

        Assert.Contains(exception.Errors, error => error.Path == "$.aiPreferences[0]");
        Assert.Contains(exception.Errors, error => error.Path == "$.aiPreferences[1]");
    }

    internal static CandidateProfile ValidProfile() =>
        new()
        {
            TargetTitles = ["Backend Engineer"],
            RequiredSkills = [".NET"],
            HardFilters = new HardFilters
            {
                Locations = ["Ukraine"],
                RemotePolicy = RemotePolicy.RemoteOnly
            },
            AiPreferences = ["Prefer backend-focused roles."]
        };
}
