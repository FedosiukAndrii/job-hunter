using Microsoft.Extensions.Options;

namespace JobHunter.Infrastructure.Configuration;

internal sealed class ProfileOptionsValidator : IValidateOptions<ProfileOptions>
{
    public ValidateOptionsResult Validate(string? name, ProfileOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        ValidateOptionalAbsolutePath(options.FilePath, "Profile:FilePath", failures);
        ValidateOptionalAbsolutePath(
            options.SupplementalCvPath,
            "Profile:SupplementalCvPath",
            failures);

        if (options.MaximumProfileBytes is < 1024 or > 1024 * 1024)
        {
            failures.Add(
                "Profile:MaximumProfileBytes must be between 1024 and 1048576.");
        }

        if (options.MaximumCvBytes is < 1024 or > 2 * 1024 * 1024)
        {
            failures.Add(
                "Profile:MaximumCvBytes must be between 1024 and 2097152.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateOptionalAbsolutePath(
        string? path,
        string configurationKey,
        List<string> failures)
    {
        if (!string.IsNullOrWhiteSpace(path) && !Path.IsPathFullyQualified(path))
        {
            failures.Add($"{configurationKey} must be an absolute path.");
        }
    }
}
