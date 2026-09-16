using System.Globalization;
using System.Text;
using System.Xml;
using JobHunter.Domain.Jobs;
using JobHunter.JobSources.Dou.Configuration;
using JobHunter.JobSources.Dou.Parsing;

namespace JobHunter.ContractTests.Dou;

public sealed class DouRssParserTests
{
    private readonly DouRssParser _parser = new();
    private readonly DouOptions _options = new();

    [Fact]
    public void ParseReadsStandardFeedAndSanitizesStoredHtml()
    {
        var result = ParseFixture("standard-dotnet.xml");

        var job = Assert.Single(result.Records);
        Assert.Equal("123", job.SourceJobId?.Value);
        Assert.Equal("Senior .NET Engineer", job.Title);
        Assert.Equal("Example", job.Company);
        Assert.Equal(
            "https://jobs.dou.ua/vacancies/123/?search=dotnet",
            job.CanonicalUrl.ToString());
        Assert.Equal(WorkplaceMode.Remote, job.WorkplaceMode);
        Assert.Equal(
            ParseDate("2026-09-15T06:30:00Z"),
            job.PublishedAtUtc);
        Assert.DoesNotContain("<script", job.DescriptionHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ignored()", job.DescriptionText, StringComparison.Ordinal);
        Assert.StartsWith("sha256:", job.ContentHash, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseDecodesEscapedUkrainianDescription()
    {
        var result = ParseFixture("ukrainian-escaped.xml");

        var job = Assert.Single(result.Records);
        Assert.Contains("\u0412\u0456\u0434\u0434\u0430\u043b\u0435\u043d\u0430", job.DescriptionText, StringComparison.Ordinal);
        Assert.Equal(WorkplaceMode.Remote, job.WorkplaceMode);
    }

    [Fact]
    public void ParseKeepsItemsWithMissingOrInvalidDatesWithoutInventingValues()
    {
        var result = ParseFixture("missing-invalid-date.xml");

        Assert.Equal(2, result.Records.Count);
        Assert.All(result.Records, job => Assert.Null(job.PublishedAtUtc));
        Assert.All(
            result.Records,
            job => Assert.Equal(PublishedAtPrecision.Unknown, job.PublishedAtPrecision));
    }

    [Fact]
    public void ParseIgnoresDuplicateNativeId()
    {
        var result = ParseFixture("duplicate-item.xml");

        Assert.Single(result.Records);
        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public void ParseRejectsMalformedXml()
    {
        var payload = File.ReadAllBytes(FixturePath("malformed.xml"));

        Assert.Throws<XmlException>(
            () => _parser.Parse(payload, "test", DateTimeOffset.UnixEpoch, _options));
    }

    [Fact]
    public void ParseRejectsDtdAndExternalEntityInput()
    {
        var payload = Encoding.UTF8.GetBytes(
            """
            <?xml version="1.0"?>
            <!DOCTYPE rss [<!ENTITY xxe SYSTEM "file:///private.txt">]>
            <rss version="2.0"><channel><item><title>&xxe;</title></item></channel></rss>
            """);

        Assert.Throws<XmlException>(
            () => _parser.Parse(payload, "test", DateTimeOffset.UnixEpoch, _options));
    }

    private DouParseResult ParseFixture(string fileName) =>
        _parser.Parse(
            File.ReadAllBytes(FixturePath(fileName)),
            "test",
            ParseDate("2026-09-15T10:00:00Z"),
            _options);

    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Dou", fileName);

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
