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
            CompensationCurrency = "<usd>",
            UsedAi = true,
            AiSummary = "Strong <match> & evidence",
            Strengths = ["Strong <stack> match"],
            Concerns = ["Missing <cloud> experience"]
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
        Assert.Contains("Strong &lt;stack&gt; match", rendered);
        Assert.Contains("Missing &lt;cloud&gt; experience", rendered);
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
            Locations = ["Address: 2 Private Avenue"],
            UsedAi = true,
            AiSummary = "Recruiter: recruiter@example.com\nAddress: 1 Private Street",
            Strengths = ["Contact vacancy@example.com"],
            Concerns = ["Call +1 (425) 555-0123"]
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

    [Fact]
    public void RenderShowsCompactAiNotificationWithoutTechnicalDetails()
    {
        var notification = CreateNotification() with
        {
            Score = 78,
            ScoreMode = "rules-and-ai",
            UsedAi = true,
            AiSummary = "Strong match for .NET, SQL and backend work; AWS is not stated.",
            Strengths = ["Strong .NET/backend match"],
            Concerns = ["AWS experience is not stated"],
            DeterministicScore = 72,
            PassedHardFilters = true,
            StrongestCriterion = "coreSkills (35/35)",
            MissingFields = ["job:compensation"],
            PublishedAtUtc = DateTimeOffset.Parse(
                "2026-09-16T00:00:00Z",
                System.Globalization.CultureInfo.InvariantCulture)
        };

        var rendered = TelegramMessageRenderer.Render(
            notification,
            DateTimeOffset.Parse(
                "2026-09-16T02:00:00Z",
                System.Globalization.CultureInfo.InvariantCulture));

        Assert.Contains("🟢 78% · ✨ AI", rendered, StringComparison.Ordinal);
        Assert.Contains("🏢 Example", rendered, StringComparison.Ordinal);
        Assert.Contains("💰 $5,000–$6,000", rendered, StringComparison.Ordinal);
        Assert.Contains("&#127757; Remote", rendered, StringComparison.Ordinal);
        Assert.Contains("🕒 2h ago", rendered, StringComparison.Ordinal);
        Assert.Contains("✅ Strong .NET/backend match", rendered, StringComparison.Ordinal);
        Assert.Contains("⚠️ AWS experience is not stated", rendered, StringComparison.Ordinal);
        Assert.Contains("✨ Strong match for .NET, SQL and backend work; AWS is not stated.", rendered, StringComparison.Ordinal);
        Assert.Contains("🔗 <a href=", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("Rules score", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("Strongest criterion", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("Missing fields", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("Hard filters", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderAppendsTechnicalFieldsOnlyWhenDebugModeIsEnabled()
    {
        var notification = CreateNotification() with
        {
            Score = 60,
            DeterministicScore = 55,
            PassedHardFilters = true,
            StrongestCriterion = "coreSkills (24/35)",
            MissingFields = ["job:compensation", "job:seniority", "job:languages"],
            AiInputTokens = 1_234,
            AiOutputTokens = 567,
            AiCredits = 0.0125,
            AiRequestCount = 2
        };

        var rendered = TelegramMessageRenderer.Render(
            notification,
            DateTimeOffset.UnixEpoch,
            debugMode: true);

        Assert.Contains("🟡 60% · ⚙️ Rules", rendered, StringComparison.Ordinal);
        Assert.Contains("<b>Debug</b>", rendered, StringComparison.Ordinal);
        Assert.Contains("⚙️ Rules score: 55%", rendered, StringComparison.Ordinal);
        Assert.Contains("🎯 Final score: 60%", rendered, StringComparison.Ordinal);
        Assert.Contains("🏆 Strongest criterion: coreSkills (24/35)", rendered, StringComparison.Ordinal);
        Assert.Contains("🧩 Missing fields: job:compensation, job:seniority, job:languages", rendered, StringComparison.Ordinal);
        Assert.Contains("🛡 Hard filters: passed", rendered, StringComparison.Ordinal);
        Assert.Contains("🧮 Scoring method: ⚙️ Rules", rendered, StringComparison.Ordinal);
        Assert.Contains("💳 AI request: 2 call(s); 1,234 in / 567 out tokens; 0.0125 AI credits", rendered, StringComparison.Ordinal);
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
            5_000,
            6_000,
            "usd",
            CompensationPeriod.Month,
            DateTimeOffset.UnixEpoch,
            canonicalUrl)
        {
            AiSummary = summary
        };
}
