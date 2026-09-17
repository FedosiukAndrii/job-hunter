using System.Reflection;
using System.Text.Json;

namespace JobHunter.Notifications.Telegram;

internal sealed class TelegramMessageTemplate
{
    private const string ResourceName =
        "JobHunter.Notifications.Telegram.Templates.TelegramNotification.json";

    public required string Header { get; init; }

    public required string Footer { get; init; }

    public required string CompensationBlock { get; init; }

    public required string AgeBlock { get; init; }

    public required string InsightsBlock { get; init; }

    public required string AiSummaryBlock { get; init; }

    public required string DebugBlock { get; init; }

    public required string ScoreModeAi { get; init; }

    public required string ScoreModeRules { get; init; }

    public required string MatchHigh { get; init; }

    public required string MatchMedium { get; init; }

    public required string MatchLow { get; init; }

    public required string CompanyNotSpecified { get; init; }

    public required string LocationNotSpecified { get; init; }

    public required string LocationWithWorkplaceMode { get; init; }

    public required string WorkplaceRemote { get; init; }

    public required string WorkplaceHybrid { get; init; }

    public required string WorkplaceOnSite { get; init; }

    public required string CompensationFrom { get; init; }

    public required string CompensationUpTo { get; init; }

    public required string CompensationHourly { get; init; }

    public required string CompensationYearly { get; init; }

    public required string CurrencyUsd { get; init; }

    public required string CurrencyEur { get; init; }

    public required string CurrencyGbp { get; init; }

    public required string CurrencyUah { get; init; }

    public required string CurrencyOther { get; init; }

    public required string AgeDays { get; init; }

    public required string AgeHours { get; init; }

    public required string AgeMinutes { get; init; }

    public required string InsightStrength { get; init; }

    public required string InsightConcern { get; init; }

    public required string InsightLine { get; init; }

    public required string NotAvailable { get; init; }

    public required string None { get; init; }

    public required string Passed { get; init; }

    public required string Failed { get; init; }

    public required string AiUsageNotRequested { get; init; }

    public required string AiUsageCallCount { get; init; }

    public required string AiUsageCallCountUnavailable { get; init; }

    public required string AiUsageTokens { get; init; }

    public required string AiUsageTokensUnavailable { get; init; }

    public required string AiUsageCredits { get; init; }

    public required string AiUsageCreditsUnavailable { get; init; }

    public required string AiUsage { get; init; }

    public required string UnknownValue { get; init; }

    public required string Debug { get; init; }

    public static TelegramMessageTemplate Load()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                "The Telegram notification template resource is missing.");
        var template = JsonSerializer.Deserialize<TelegramMessageTemplate>(stream)
            ?? throw new InvalidOperationException(
                "The Telegram notification template resource is invalid.");
        var missingProperties = typeof(TelegramMessageTemplate)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(
                property => string.IsNullOrWhiteSpace(
                    property.GetValue(template) as string))
            .Select(property => property.Name)
            .ToArray();
        if (missingProperties.Length > 0)
        {
            throw new InvalidOperationException(
                "The Telegram notification template is incomplete: "
                + string.Join(", ", missingProperties));
        }

        return template;
    }

    public string Format(
        string value,
        params (string Placeholder, string Replacement)[] replacements)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (Header.Length == 0)
        {
            throw new InvalidOperationException(
                "The Telegram notification template has not been validated.");
        }

        foreach (var (placeholder, replacement) in replacements)
        {
            value = value.Replace(
                $"{{{{{placeholder}}}}}",
                replacement,
                StringComparison.Ordinal);
        }

        return value;
    }
}
