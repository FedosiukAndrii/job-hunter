using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using JobHunter.Application.Sources;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Sources;
using JobHunter.JobSources.Dou.Configuration;

namespace JobHunter.JobSources.Dou.Parsing;

public sealed partial class DouRssParser
{
    public const string ParserVersion = "dou-rss-v1";

    private readonly string _parserVersion;

    public DouRssParser()
    {
        _parserVersion = ParserVersion;
    }

    public DouParseResult Parse(
        ReadOnlyMemory<byte> payload,
        string? queryId,
        DateTimeOffset retrievedAtUtc,
        DouOptions options,
        int? requestedMaximumItems = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        using var stream = new MemoryStream(payload.ToArray(), writable: false);
        using var reader = XmlReader.Create(
            stream,
            new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                MaxCharactersInDocument = options.MaximumResponseBytes
            });
        var document = XDocument.Load(reader, LoadOptions.None);
        var channel = document.Root?.Name.LocalName == "rss"
            ? document.Root.Elements().FirstOrDefault(element => element.Name.LocalName == "channel")
            : null;
        if (channel is null)
        {
            throw new XmlException("The response is not an RSS document with a channel element.");
        }

        var records = new List<JobSourceRecord>();
        var diagnostics = new List<string>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var itemNumber = 0;
        var maximumItems = Math.Min(
            options.MaximumItems,
            requestedMaximumItems.GetValueOrDefault(options.MaximumItems));
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumItems, 1);

        foreach (var item in channel.Elements().Where(element => element.Name.LocalName == "item"))
        {
            itemNumber++;
            if (records.Count >= maximumItems)
            {
                diagnostics.Add($"Item limit {maximumItems} reached.");
                break;
            }

            try
            {
                var record = ParseItem(item, queryId, retrievedAtUtc, options);
                var key = JobKey.Create(record.Source, record.SourceJobId, record.CanonicalUrl).Value;
                if (!seenKeys.Add(key))
                {
                    diagnostics.Add($"Duplicate item {itemNumber} was ignored.");
                    continue;
                }

                records.Add(record);
            }
            catch (ArgumentException exception)
            {
                diagnostics.Add($"Item {itemNumber} is invalid: {exception.Message}");
            }
            catch (FormatException exception)
            {
                diagnostics.Add($"Item {itemNumber} is invalid: {exception.Message}");
            }
        }

        return new DouParseResult(records, diagnostics);
    }

    private JobSourceRecord ParseItem(
        XElement item,
        string? queryId,
        DateTimeOffset retrievedAtUtc,
        DouOptions options)
    {
        var rawTitle = RequiredElement(item, "title", 512);
        var sourceUrlValue = RequiredElement(item, "link", 2048);
        var sourceUrl = CreateDouUri(sourceUrlValue);
        var canonicalUrl = CanonicalJobUrl.Create(sourceUrlValue);
        var guid = OptionalElement(item, "guid", 1024);
        var description = OptionalElement(
            item,
            "description",
            options.MaximumDescriptionCharacters)
            ?? string.Empty;
        var sanitized = SafeHtmlContent.Sanitize(
            description,
            options.MaximumDescriptionCharacters);
        var titleParts = SplitTitle(rawTitle);
        var sourceJobId = TryExtractVacancyId(sourceUrl, guid);
        var categories = item.Elements()
            .Where(element => element.Name.LocalName == "category")
            .Select(element => Bound(element.Value.Trim(), 128))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var publishedAt = ParsePublishedAt(OptionalElement(item, "pubDate", 128));
        var rawItem = item.ToString(SaveOptions.DisableFormatting);
        var contentHash = Hash(
            string.Join(
                '\n',
                titleParts.Title,
                titleParts.Company,
                sanitized.PlainText,
                canonicalUrl.ToString(),
                publishedAt?.ToString("O", CultureInfo.InvariantCulture)));

        return new JobSourceRecord
        {
            Source = SourceName.Dou,
            SourceJobId = sourceJobId,
            SourceUrl = sourceUrl,
            CanonicalUrl = canonicalUrl,
            SourceGuid = guid,
            Title = titleParts.Title,
            Company = titleParts.Company,
            DescriptionHtml = sanitized.Html,
            DescriptionText = sanitized.PlainText,
            Locations = titleParts.Locations,
            WorkplaceMode = DetectWorkplaceMode(rawTitle, sanitized.PlainText),
            EmploymentType = EmploymentType.Unknown,
            Skills = categories,
            Categories = categories,
            PublishedAtUtc = publishedAt,
            PublishedAtPrecision = publishedAt is null
                ? PublishedAtPrecision.Unknown
                : PublishedAtPrecision.DateTime,
            ApplicationUrl = canonicalUrl.Value,
            ParserVersion = _parserVersion,
            ContentHash = contentHash,
            RawPayloadHash = Hash(rawItem),
            RetrievedAtUtc = retrievedAtUtc
        };
    }

    private static string RequiredElement(
        XElement item,
        string localName,
        int maximumLength)
    {
        var value = OptionalElement(item, localName, maximumLength);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Required RSS element '{localName}' is missing.");
        }

        return value;
    }

    private static string? OptionalElement(
        XElement item,
        string localName,
        int maximumLength)
    {
        var value = item.Elements()
            .FirstOrDefault(element => element.Name.LocalName == localName)
            ?.Value
            .Trim();
        return value is null ? null : Bound(value, maximumLength);
    }

    private static DouTitleParts SplitTitle(string rawTitle)
    {
        foreach (var separator in new[] { " в ", " at " })
        {
            var separatorIndex = rawTitle.LastIndexOf(separator, StringComparison.OrdinalIgnoreCase);
            if (separatorIndex > 0 && separatorIndex + separator.Length < rawTitle.Length)
            {
                var companyAndLocation = rawTitle[(separatorIndex + separator.Length)..]
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var company = companyAndLocation[0];
                var locations = companyAndLocation
                    .Skip(1)
                    .Where(value =>
                        !value.Contains("remote", StringComparison.OrdinalIgnoreCase)
                        && !value.Contains("віддал", StringComparison.OrdinalIgnoreCase)
                        && !value.Contains("hybrid", StringComparison.OrdinalIgnoreCase)
                        && !value.Contains("гібрид", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                return new DouTitleParts(
                    rawTitle[..separatorIndex].Trim(),
                    company,
                    locations);
            }
        }

        return new DouTitleParts(rawTitle.Trim(), string.Empty, []);
    }

    private static NativeSourceId? TryExtractVacancyId(Uri sourceUrl, string? guid)
    {
        var match = VacancyIdPattern().Match(sourceUrl.AbsolutePath);
        if (match.Success)
        {
            return NativeSourceId.Create(match.Groups["id"].Value);
        }

        return string.IsNullOrWhiteSpace(guid) ? null : NativeSourceId.Create(guid);
    }

    private static DateTimeOffset? ParsePublishedAt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var formats = new[]
        {
            "r",
            "ddd, dd MMM yyyy HH:mm:ss zzz",
            "ddd, d MMM yyyy HH:mm:ss zzz"
        };
        return DateTimeOffset.TryParseExact(
            value,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    private static Uri CreateDouUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.IdnHost, "jobs.dou.ua", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException(
                "A DOU vacancy URL must be an HTTPS URL on jobs.dou.ua.",
                nameof(value));
        }

        return uri;
    }

    private static WorkplaceMode DetectWorkplaceMode(string title, string description)
    {
        var text = $"{title} {description}";
        if (text.Contains("hybrid", StringComparison.OrdinalIgnoreCase)
            || text.Contains("гібрид", StringComparison.OrdinalIgnoreCase))
        {
            return WorkplaceMode.Hybrid;
        }

        if (text.Contains("remote", StringComparison.OrdinalIgnoreCase)
            || text.Contains("віддал", StringComparison.OrdinalIgnoreCase))
        {
            return WorkplaceMode.Remote;
        }

        if (text.Contains("on-site", StringComparison.OrdinalIgnoreCase)
            || text.Contains("onsite", StringComparison.OrdinalIgnoreCase)
            || text.Contains("в офісі", StringComparison.OrdinalIgnoreCase))
        {
            return WorkplaceMode.OnSite;
        }

        return WorkplaceMode.Unknown;
    }

    private static string Hash(string value) =>
        $"sha256:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))}";

    private static string Bound(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    [GeneratedRegex(@"/vacancies/(?<id>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex VacancyIdPattern();

    private sealed record DouTitleParts(
        string Title,
        string Company,
        IReadOnlyList<string> Locations);
}

public sealed record DouParseResult(
    IReadOnlyList<JobSourceRecord> Records,
    IReadOnlyList<string> Diagnostics);
