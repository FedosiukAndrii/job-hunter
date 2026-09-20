using System.Globalization;
using System.Net;
using System.Text;
using JobHunter.Application.Notifications;
using JobHunter.Application.Profiles;
using JobHunter.Domain.Jobs;

namespace JobHunter.Notifications.Telegram;

public static class TelegramMessageRenderer
{
    public const int MaximumMessageLength = 4096;

    private static readonly TelegramMessageTemplate Template =
        TelegramMessageTemplate.Load();

    public static string Render(
        JobNotification notification,
        DateTimeOffset now,
        bool debugMode = false)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (!Uri.TryCreate(notification.CanonicalUrl, UriKind.Absolute, out var url)
            || url.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(url.Host)
            || !string.IsNullOrEmpty(url.UserInfo))
        {
            throw new InvalidDataException(
                "A Telegram notification requires a trusted absolute HTTPS job URL.");
        }

        var encodedUrl = WebUtility.HtmlEncode(url.AbsoluteUri);
        if (encodedUrl.Length > 1536)
        {
            throw new InvalidDataException(
                "The job URL is too long for a safe Telegram notification.");
        }

        var finalScore = Math.Clamp(notification.Score, 0, 100);
        var title = EncodeBounded(
            Redact(Require(notification.Title, nameof(notification.Title))),
            240);
        var company = EncodeBounded(
            Redact(
                string.IsNullOrWhiteSpace(notification.Company)
                    ? Template.CompanyNotSpecified
                    : notification.Company),
            220);
        var location = EncodeBounded(Redact(FormatLocation(notification)), 240);
        var footer = Template.Format(Template.Footer, ("url", encodedUrl));
        var builder = new StringBuilder(
            Template.Format(
                Template.Header,
                ("indicator", GetMatchIndicator(finalScore)),
                ("score", finalScore.ToString(CultureInfo.InvariantCulture)),
                ("title", title),
                ("company", company),
                ("location", location)));

        var compensation = FormatCompensation(notification);
        if (compensation.Length > 0)
        {
            AppendIfFits(
                builder,
                Template.Format(
                    Template.CompensationBlock,
                    ("compensation", EncodeBounded(compensation, 140))),
                footer);
        }

        if (notification.PublishedAtUtc is not null)
        {
            AppendIfFits(
                builder,
                Template.Format(
                    Template.AgeBlock,
                    ("age", FormatAge(notification.PublishedAtUtc.Value, now))),
                footer);
        }

        var insights = FormatInsights(notification);
        if (insights.Length > 0)
        {
            AppendIfFits(
                builder,
                Template.Format(Template.InsightsBlock, ("insights", insights)),
                footer);
        }

        if (!string.IsNullOrWhiteSpace(notification.AiSummary))
        {
            var summary = EncodeBounded(Redact(notification.AiSummary), 420);
            AppendIfFits(
                builder,
                Template.Format(Template.AiSummaryBlock, ("summary", summary)),
                footer);
        }

        if (debugMode)
        {
            AppendIfFits(
                builder,
                Template.Format(
                    Template.DebugBlock,
                    ("debug", FormatDebug(notification, finalScore))),
                footer);
        }

        if (builder.Length + footer.Length > MaximumMessageLength)
        {
            throw new InvalidDataException(
                "The required Telegram notification fields exceed the message limit.");
        }

        builder.Append(footer);
        return builder.ToString();
    }

    private static void AppendIfFits(
        StringBuilder builder,
        string content,
        string footer)
    {
        if (builder.Length + content.Length + footer.Length <= MaximumMessageLength)
        {
            builder.Append(content);
        }
    }

    private static string GetMatchIndicator(int score) => score switch
    {
        >= 75 => Template.MatchHigh,
        >= 50 => Template.MatchMedium,
        _ => Template.MatchLow
    };

    private static string FormatLocation(JobNotification notification)
    {
        var locations = notification.Locations
            .Where(location => !string.IsNullOrWhiteSpace(location))
            .Select(location => location.Trim())
            .Where(
                location => notification.WorkplaceMode == WorkplaceMode.Unknown
                    || !string.Equals(location, "remote", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var location = locations.Length == 0
            ? Template.LocationNotSpecified
            : string.Join(", ", locations);
        var workplaceMode = notification.WorkplaceMode switch
        {
            WorkplaceMode.Remote => Template.WorkplaceRemote,
            WorkplaceMode.Hybrid => Template.WorkplaceHybrid,
            WorkplaceMode.OnSite => Template.WorkplaceOnSite,
            _ => null
        };

        return workplaceMode is null
            ? location
            : Template.Format(
                Template.LocationWithWorkplaceMode,
                ("location", location),
                ("workplaceMode", workplaceMode));
    }

    private static string FormatCompensation(JobNotification notification)
    {
        if ((notification.CompensationMinimum is null
                && notification.CompensationMaximum is null)
            || string.IsNullOrWhiteSpace(notification.CompensationCurrency)
            || notification.CompensationPeriod == CompensationPeriod.Unknown)
        {
            return string.Empty;
        }

        var currency = notification.CompensationCurrency.Trim().ToUpperInvariant();
        var amount = (notification.CompensationMinimum, notification.CompensationMaximum) switch
        {
            ({ } minimum, { } maximum) when minimum == maximum =>
                FormatMoney(minimum, currency),
            ({ } minimum, { } maximum) =>
                $"{FormatMoney(minimum, currency)}–{FormatMoney(maximum, currency)}",
            ({ } minimum, null) => Template.Format(
                Template.CompensationFrom,
                ("amount", FormatMoney(minimum, currency))),
            (null, { } maximum) => Template.Format(
                Template.CompensationUpTo,
                ("amount", FormatMoney(maximum, currency))),
            _ => string.Empty
        };
        var period = notification.CompensationPeriod switch
        {
            CompensationPeriod.Hour => Template.CompensationHourly,
            CompensationPeriod.Month => string.Empty,
            CompensationPeriod.Year => Template.CompensationYearly,
            _ => string.Empty
        };
        return string.Concat(amount, period);
    }

    private static string FormatMoney(decimal amount, string currency)
    {
        var formatted = amount.ToString("#,0.##", CultureInfo.InvariantCulture);
        return currency switch
        {
            "USD" => Template.Format(Template.CurrencyUsd, ("amount", formatted)),
            "EUR" => Template.Format(Template.CurrencyEur, ("amount", formatted)),
            "GBP" => Template.Format(Template.CurrencyGbp, ("amount", formatted)),
            "UAH" => Template.Format(Template.CurrencyUah, ("amount", formatted)),
            _ => Template.Format(
                Template.CurrencyOther,
                ("amount", formatted),
                ("currency", currency))
        };
    }

    private static string FormatAge(DateTimeOffset publishedAtUtc, DateTimeOffset now)
    {
        var age = now - publishedAtUtc;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        return age.TotalDays >= 1
            ? Template.Format(
                Template.AgeDays,
                ("value", Math.Floor(age.TotalDays).ToString(CultureInfo.InvariantCulture)))
            : age.TotalHours >= 1
                ? Template.Format(
                    Template.AgeHours,
                    ("value", Math.Floor(age.TotalHours).ToString(CultureInfo.InvariantCulture)))
                : Template.Format(
                    Template.AgeMinutes,
                    ("value", Math.Max(0, Math.Floor(age.TotalMinutes))
                        .ToString(CultureInfo.InvariantCulture)));
    }

    private static string FormatInsights(JobNotification notification)
    {
        var strengths = NormalizeInsights(notification.Strengths);
        var concerns = NormalizeInsights(notification.Concerns);
        var selected = new List<(string Icon, string Text)>();
        if (strengths.Count > 0)
        {
            selected.Add((Template.InsightStrength, strengths[0]));
        }

        if (concerns.Count > 0)
        {
            selected.Add((Template.InsightConcern, concerns[0]));
        }

        foreach (var strength in strengths.Skip(1))
        {
            if (selected.Count == 2)
            {
                break;
            }

            selected.Add((Template.InsightStrength, strength));
        }

        foreach (var concern in concerns.Skip(1))
        {
            if (selected.Count == 2)
            {
                break;
            }

            selected.Add((Template.InsightConcern, concern));
        }

        return string.Join(
            '\n',
            selected.Select(
                insight => Template.Format(
                    Template.InsightLine,
                    ("icon", insight.Icon),
                    ("text", EncodeBounded(Redact(insight.Text), 180)))));
    }

    private static List<string> NormalizeInsights(IReadOnlyList<string>? values) =>
        NormalizeValues(values, 2);

    private static List<string> NormalizeValues(
        IReadOnlyList<string>? values,
        int maximumCount) =>
        (values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(maximumCount)
            .ToList();

    private static string FormatDebug(
        JobNotification notification,
        int finalScore)
    {
        var hardFilterResult = notification.PassedHardFilters is null
            ? Template.NotAvailable
            : notification.PassedHardFilters.Value ? Template.Passed : Template.Failed;

        return Template.Format(
            Template.Debug,
            ("finalScore", finalScore.ToString(CultureInfo.InvariantCulture)),
            ("hardFilterResult", hardFilterResult),
            ("aiUsage", FormatAiRequestUsage(notification)));
    }

    private static string FormatAiRequestUsage(JobNotification notification)
    {
        if (notification.AiRequestCount == 0
            && notification.AiInputTokens is null
            && notification.AiOutputTokens is null
            && notification.AiCredits is null)
        {
            return Template.AiUsageNotRequested;
        }

        var calls = notification.AiRequestCount > 0
            ? Template.Format(
                Template.AiUsageCallCount,
                ("value", notification.AiRequestCount.ToString(CultureInfo.InvariantCulture)))
            : Template.AiUsageCallCountUnavailable;
        var tokens = notification.AiInputTokens is null
            && notification.AiOutputTokens is null
                ? Template.AiUsageTokensUnavailable
                : Template.Format(
                    Template.AiUsageTokens,
                    ("input", notification.AiInputTokens?.ToString(
                        "N0",
                        CultureInfo.InvariantCulture) ?? Template.UnknownValue),
                    ("output", notification.AiOutputTokens?.ToString(
                        "N0",
                        CultureInfo.InvariantCulture) ?? Template.UnknownValue));
        var credits = notification.AiCredits is null
            ? Template.AiUsageCreditsUnavailable
            : Template.Format(
                Template.AiUsageCredits,
                ("value", notification.AiCredits.Value.ToString(
                    "0.########",
                    CultureInfo.InvariantCulture)));
        return Template.Format(
            Template.AiUsage,
            ("calls", calls),
            ("tokens", tokens),
            ("credits", credits));
    }

    private static string EncodeBounded(string value, int maximumEncodedLength)
    {
        var normalized = value.Trim();
        var encoded = WebUtility.HtmlEncode(normalized);
        if (encoded.Length <= maximumEncodedLength)
        {
            return encoded;
        }

        var maximumContentLength = maximumEncodedLength - 3;
        var low = 0;
        var high = normalized.Length;
        while (low < high)
        {
            var middle = low + (high - low + 1) / 2;
            var prefixLength = AvoidSplittingSurrogatePair(normalized, middle);
            if (WebUtility.HtmlEncode(normalized[..prefixLength]).Length
                <= maximumContentLength)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        var safeLength = AvoidSplittingSurrogatePair(normalized, low);
        return string.Concat(WebUtility.HtmlEncode(normalized[..safeLength]), "...");
    }

    private static int AvoidSplittingSurrogatePair(string value, int length) =>
        length > 0
        && length < value.Length
        && char.IsHighSurrogate(value[length - 1])
        && char.IsLowSurrogate(value[length])
            ? length - 1
            : length;

    private static string Redact(string value) => SensitiveTextRedactor.Redact(value);

    private static string Require(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }
}
