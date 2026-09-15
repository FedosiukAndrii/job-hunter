using Microsoft.Extensions.Options;

namespace JobHunter.Infrastructure.Configuration;

internal sealed class StorageOptionsValidator : IValidateOptions<StorageOptions>
{
    public ValidateOptionsResult Validate(string? name, StorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        var dataDirectory = options.DataDirectory?.Trim();

        if (dataDirectory is not null)
        {
            if (!Path.IsPathFullyQualified(dataDirectory))
            {
                failures.Add("Storage:DataDirectory must be an absolute path.");
            }

            if (IsUncPath(dataDirectory))
            {
                failures.Add("Storage:DataDirectory must use local storage, not a UNC network path.");
            }
        }

        if (string.IsNullOrWhiteSpace(options.DatabaseFileName)
            || options.DatabaseFileName.IndexOfAny(['/', '\\']) >= 0
            || options.DatabaseFileName is "." or "..")
        {
            failures.Add("Storage:DatabaseFileName must be a file name without directory segments.");
        }

        if (options.BusyTimeoutSeconds is < 1 or > 60)
        {
            failures.Add("Storage:BusyTimeoutSeconds must be between 1 and 60.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsUncPath(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal)
        || path.StartsWith("//", StringComparison.Ordinal);
}
