using Microsoft.Extensions.Options;

namespace JobHunter.Infrastructure.Configuration;

internal sealed class RetentionOptionsValidator : IValidateOptions<RetentionOptions>
{
    public ValidateOptionsResult Validate(string? name, RetentionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (options.OperationalRecordDays is < 1 or > 3650)
        {
            failures.Add(
                "Retention:OperationalRecordDays must be between 1 and 3650.");
        }

        if (options.CleanupIntervalHours is < 1 or > 168)
        {
            failures.Add(
                "Retention:CleanupIntervalHours must be between 1 and 168.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
