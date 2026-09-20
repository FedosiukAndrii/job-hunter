using JobHunter.Application.Profiles;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Infrastructure.Tests.Profiles;

public sealed class FileCandidateProfileLoaderTests
{
    [Fact]
    public async Task LoadAsyncDiscoversYamlAndRedactsCvContactData()
    {
        await using var host = await Persistence.PersistenceTestHost.CreateAsync();
        var profilePath = Path.Combine(host.Directory.Path, "profile.yaml");
        var cvPath = Path.Combine(host.Directory.Path, "cv.md");
        await File.WriteAllTextAsync(profilePath, ValidYaml);
        await File.WriteAllTextAsync(
            cvPath,
            """
            # Candidate
            Email: candidate@example.com
            Phone: +380 67 123 45 67
            Address: 1 Private Street
            Built production .NET systems.
            """);

        var profile = await host.Services
            .GetRequiredService<ICandidateProfileLoader>()
            .LoadAsync(CancellationToken.None);

        Assert.Equal(["Backend Engineer"], profile.Profile.TargetTitles);
        Assert.Equal([".NET"], profile.Profile.RequiredSkills);
        Assert.Contains("[redacted-email]", profile.SupplementalCvRedacted);
        Assert.Contains("[redacted-phone]", profile.SupplementalCvRedacted);
        Assert.Contains("[redacted-address]", profile.SupplementalCvRedacted);
        Assert.DoesNotContain("candidate@example.com", profile.SupplementalCvRedacted);
        Assert.StartsWith("sha256:", profile.ContentHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsyncRejectsUnknownJsonPropertyWithUsefulPath()
    {
        await using var host = await Persistence.PersistenceTestHost.CreateAsync();
        var profilePath = Path.Combine(host.Directory.Path, "profile.json");
        await File.WriteAllTextAsync(
            profilePath,
            """
            {
              "schemaVersion": 2,
              "targetTitles": ["Backend Engineer"],
              "requiredSkills": [".NET"],
              "hardFilters": {},
              "unexpected": true
            }
            """);

        var exception = await Assert.ThrowsAsync<CandidateProfileValidationException>(
            () => host.Services
                .GetRequiredService<ICandidateProfileLoader>()
                .LoadAsync(CancellationToken.None));

        Assert.Contains(exception.Errors, error => error.Path.Contains("unexpected", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadAsyncRejectsMalformedYamlWithLocationAndRemediation()
    {
        await using var host = await Persistence.PersistenceTestHost.CreateAsync();
        var profilePath = Path.Combine(host.Directory.Path, "profile.yaml");
        await File.WriteAllTextAsync(profilePath, "schemaVersion: [");

        var exception = await Assert.ThrowsAsync<CandidateProfileValidationException>(
            () => host.Services
                .GetRequiredService<ICandidateProfileLoader>()
                .LoadAsync(CancellationToken.None));

        var error = Assert.Single(exception.Errors);
        Assert.Contains("line", error.Path, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(error.Remediation));
    }

    private const string ValidYaml =
        """
        schemaVersion: 2
        targetTitles:
          - Backend Engineer
        requiredSkills:
          - .NET
        hardFilters:
          locations:
            - Ukraine
          remotePolicy: remoteOnly
        supplementalCvPath: cv.md
        """;
}
