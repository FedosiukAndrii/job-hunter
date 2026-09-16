using JobHunter.Application.Notifications;
using JobHunter.Domain.Jobs;
using JobHunter.Notifications.Telegram;

namespace JobHunter.ContractTests.Telegram;

public sealed class TelegramMessageRendererTests
{
    [Fact]
    public void RenderEscapesEverySourceControlledField()
    {
        var notification = CreateNotification(
            title: "<b>Lead & \"Owner\"</b>",
            company: "<script>company</script>",
            summary: "Strong <match> & evidence",
            canonicalUrl: "https://jobs.dou.ua/vacancies/123/?x=1&y=2") with
        {
            Locations = ["<remote>"],
            ScoreMode = "<rules>",
            CompensationCurrency = "<usd>"
        };

        var rendered = TelegramMessageRenderer.Render(
            notification,
            DateTimeOffset.Parse(
                "2026-09-16T10:00:00Z",
                System.Globalization.CultureInfo.InvariantCulture));

        Assert.DoesNotContain("<script>", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("<match>", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("<remote>", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("<rules>", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("<USD>", rendered, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;Lead &amp; &quot;Owner&quot;&lt;/b&gt;", rendered);
        Assert.Contains("&lt;script&gt;company&lt;/script&gt;", rendered);
        Assert.Contains("Strong &lt;match&gt; &amp; evidence", rendered);
        Assert.Contains("x=1&amp;y=2", rendered);
    }

    [Fact]
    public void RenderKeepsMessageWithinTelegramLimit()
    {
        var notification = CreateNotification(
            title: new string('&', 2_000),
            company: new string('<', 2_000),
            summary: new string('>', 20_000),
            canonicalUrl: "https://jobs.dou.ua/vacancies/123/");

        var rendered = TelegramMessageRenderer.Render(
            notification,
            DateTimeOffset.UnixEpoch);

        Assert.InRange(rendered.Length, 1, TelegramMessageRenderer.MaximumMessageLength);
        Assert.EndsWith("</a>", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderRejectsUntrustedJobUrl()
    {
        var notification = CreateNotification(
            canonicalUrl: "http://jobs.dou.ua/vacancies/123/");

        var exception = Assert.Throws<InvalidDataException>(
            () => TelegramMessageRenderer.Render(
                notification,
                DateTimeOffset.UnixEpoch));

        Assert.Contains("HTTPS", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderRedactsContactDetailsFromUntrustedFields()
    {
        var notification = CreateNotification(
            title: "Contact vacancy@example.com",
            company: "+1 (425) 555-0123",
            summary: "Recruiter: recruiter@example.com\nAddress: 1 Private Street") with
        {
            Locations = ["Address: 2 Private Avenue"]
        };

        var rendered = TelegramMessageRenderer.Render(
            notification,
            DateTimeOffset.UnixEpoch);

        Assert.DoesNotContain("vacancy@example.com", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("recruiter@example.com", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("555-0123", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("Private Street", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("Private Avenue", rendered, StringComparison.Ordinal);
        Assert.Contains("[redacted-email]", rendered, StringComparison.Ordinal);
        Assert.Contains("[redacted-phone]", rendered, StringComparison.Ordinal);
        Assert.Contains("[redacted-address]", rendered, StringComparison.Ordinal);
    }

    private static JobNotification CreateNotification(
        string title = "Senior .NET Engineer",
        string company = "Example",
        string summary = "Strong match.",
        string canonicalUrl = "https://jobs.dou.ua/vacancies/123/") =>
        new(
            title,
            company,
            ["Kyiv <remote>"],
            WorkplaceMode.Remote,
            90,
            "rules-only",
            summary,
            5_000,
            6_000,
            "usd",
            CompensationPeriod.Month,
            DateTimeOffset.UnixEpoch,
            canonicalUrl);
}
