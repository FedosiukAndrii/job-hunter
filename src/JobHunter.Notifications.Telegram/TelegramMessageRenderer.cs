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

    public static string Render(JobNotification notification, DateTimeOffset now)
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

        var title = EncodeBounded(
            Redact(Require(notification.Title, nameof(notification.Title))),
            768);
        var company = EncodeBounded(
            Redact(
                string.IsNullOrWhiteSpace(notification.Company)
                    ? "Company not specified"
                    : notification.Company),
            512);
        var location = EncodeBounded(Redact(FormatLocation(notification)), 512);
        var scoreMode = EncodeBounded(
            Require(notification.ScoreMode, nameof(notification.ScoreMode)),
            64);
        var optionalLines = new List<string>();
        var compensation = FormatCompensation(notification);
        if (compensation is not null)
        {
            optionalLines.Add(EncodeBounded(compensation, 256));
        }

        if (notification.PublishedAtUtc is not null)
        {
            optionalLines.Add(FormatAge(notification.PublishedAtUtc.Value, now));
        }

        var prefix = string.Join(
            '\n',
            $"<b>{title}</b>",
            company,
            location,
            FormattableString.Invariant(
                $"Score: <b>{Math.Clamp(notification.Score, 0, 100)}/100</b> ({scoreMode})"));
        var suffixBuilder = new StringBuilder();
        foreach (var line in optionalLines)
        {
            suffixBuilder.Append('\n');
            suffixBuilder.Append(line);
        }

        suffixBuilder.Append('\n');
        suffixBuilder.Append(CultureInfo.InvariantCulture, $"<a href=\"{encodedUrl}\">Open vacancy</a>");
        var suffix = suffixBuilder.ToString();
        var baseLength = prefix.Length + suffix.Length;
        if (baseLength > MaximumMessageLength)
        {
            throw new InvalidDataException(
                "The required Telegram notification fields exceed the message limit.");
        }

        var summarySpace = MaximumMessageLength - baseLength - 1;
        var summary = string.IsNullOrWhiteSpace(notification.Summary)
            || summarySpace < 4
            ? string.Empty
            : EncodeBounded(
                Redact(notification.Summary),
                Math.Min(2200, summarySpace));
        var rendered = summary.Length == 0
            ? string.Concat(prefix, suffix)
            : string.Concat(prefix, "\n", summary, suffix);

        if (rendered.Length > MaximumMessageLength)
        {
            throw new InvalidDataException(
                "The rendered Telegram notification exceeds 4,096 characters.");
        }

        return rendered;
    }

    private static string FormatLocation(JobNotification notification)
    {
        var locations = notification.Locations.Count == 0
            ? "Location not specified"
            : string.Join(", ", notification.Locations);
        var workplaceMode = notification.WorkplaceMode switch
        {
            WorkplaceMode.Remote => "remote",
            WorkplaceMode.Hybrid => "hybrid",
            WorkplaceMode.OnSite => "on-site",
            _ => "work mode not specified"
        };
        return $"{locations} | {workplaceMode}";
    }

    private static string? FormatCompensation(JobNotification notification)
    {
        if (notification.CompensationMinimum is null
            && notification.CompensationMaximum is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(notification.CompensationCurrency)
            || notification.CompensationPeriod == CompensationPeriod.Unknown)
        {
            return null;
        }

        var amount = (notification.CompensationMinimum, notification.CompensationMaximum) switch
        {
            ({ } minimum, { } maximum) when minimum == maximum =>
                minimum.ToString("0.##", CultureInfo.InvariantCulture),
            ({ } minimum, { } maximum) =>
                $"{minimum.ToString("0.##", CultureInfo.InvariantCulture)}-"
                + maximum.ToString("0.##", CultureInfo.InvariantCulture),
            ({ } minimum, null) =>
                $"from {minimum.ToString("0.##", CultureInfo.InvariantCulture)}",
            (null, { } maximum) =>
                $"up to {maximum.ToString("0.##", CultureInfo.InvariantCulture)}",
            _ => throw new InvalidOperationException("Unsupported compensation range.")
        };
        var period = notification.CompensationPeriod switch
        {
            CompensationPeriod.Hour => "hour",
            CompensationPeriod.Month => "month",
            CompensationPeriod.Year => "year",
            _ => throw new InvalidOperationException("Unsupported compensation period.")
        };
        return $"Compensation: {amount} {notification.CompensationCurrency.Trim().ToUpperInvariant()}/{period}";
    }

    private static string FormatAge(DateTimeOffset publishedAtUtc, DateTimeOffset now)
    {
        var age = now - publishedAtUtc;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        return age.TotalDays >= 1
            ? FormattableString.Invariant($"Published {Math.Floor(age.TotalDays)}d ago")
            : age.TotalHours >= 1
                ? FormattableString.Invariant($"Published {Math.Floor(age.TotalHours)}h ago")
                : FormattableString.Invariant(
                    $"Published {Math.Max(0, Math.Floor(age.TotalMinutes))}m ago");
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
