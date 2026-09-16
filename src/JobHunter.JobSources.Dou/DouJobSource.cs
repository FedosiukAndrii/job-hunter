using System.Net;
using System.Net.Http.Headers;
using System.Xml;
using JobHunter.Application.Sources;
using JobHunter.Domain.Sources;
using JobHunter.JobSources.Dou.Configuration;
using JobHunter.JobSources.Dou.Parsing;
using Microsoft.Extensions.Options;

namespace JobHunter.JobSources.Dou;

public sealed class DouJobSource(
    HttpClient httpClient,
    DouRssParser parser,
    TimeProvider timeProvider,
    IOptions<DouOptions> options)
    : IJobSource, IDisposable
{
    private static readonly TimeSpan[] RetryDelays =
        [TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(750)];

    public SourceName Name => SourceName.Dou;

    public void Dispose() => httpClient.Dispose();

    public async Task<JobSourceResult> FetchAsync(
        JobSourceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateEndpoint(request.Endpoint);
        var now = timeProvider.GetUtcNow();

        try
        {
            using var response = await SendWithRetryAsync(request, cancellationToken);
            var cursor = CreateCursor(response, request.Cursor);
            var nextPoll = GetNextPollAt(response, now);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return JobSourceResult.Succeeded([], cursor, nextPoll, notModified: true);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return JobSourceResult.Failed(
                    "HttpRateLimited",
                    "DOU returned HTTP 429. The source will wait before retrying.",
                    GetRetryAfter(response, now));
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return JobSourceResult.Failed(
                    "HttpForbidden",
                    "DOU returned HTTP 403. The source is degraded and no bypass will be attempted.",
                    now.AddHours(1));
            }

            if (!response.IsSuccessStatusCode)
            {
                return JobSourceResult.Failed(
                    $"Http{(int)response.StatusCode}",
                    $"DOU returned HTTP {(int)response.StatusCode}.",
                    now.AddMinutes(options.Value.DefaultIntervalMinutes));
            }

            if (!IsSupportedContentType(response.Content.Headers.ContentType))
            {
                return JobSourceResult.Failed(
                    "UnsupportedContentType",
                    $"DOU returned unsupported content type '{response.Content.Headers.ContentType?.MediaType ?? "missing"}'.",
                    now.AddMinutes(options.Value.DefaultIntervalMinutes));
            }

            var payload = await ReadBoundedAsync(response.Content, cancellationToken);
            var parseResult = parser.Parse(
                payload,
                request.QueryId,
                now,
                options.Value,
                request.MaximumItems);
            if (parseResult.Records.Count == 0 && parseResult.Diagnostics.Count > 0)
            {
                return JobSourceResult.Failed(
                    "ParserFailure",
                    string.Join(" ", parseResult.Diagnostics),
                    now.AddMinutes(options.Value.DefaultIntervalMinutes));
            }

            return new JobSourceResult(
                parseResult.Diagnostics.Count == 0
                    ? SourceRunStatus.Succeeded
                    : SourceRunStatus.Partial,
                parseResult.Records,
                cursor,
                false,
                nextPoll,
                null,
                parseResult.Diagnostics.Count == 0 ? null : "PartialParse",
                parseResult.Diagnostics.Count == 0
                    ? null
                    : string.Join(" ", parseResult.Diagnostics));
        }
        catch (XmlException exception)
        {
            return JobSourceResult.Failed(
                "MalformedXml",
                $"DOU RSS parsing failed: {Bound(exception.Message, 512)}",
                now.AddMinutes(options.Value.DefaultIntervalMinutes));
        }
        catch (InvalidDataException exception)
        {
            return JobSourceResult.Failed(
                "ResponseLimitExceeded",
                exception.Message,
                now.AddMinutes(options.Value.DefaultIntervalMinutes));
        }
        catch (HttpRequestException exception)
        {
            return JobSourceResult.Failed(
                "TransportFailure",
                $"DOU request failed: {Bound(exception.Message, 512)}",
                now.AddMinutes(options.Value.DefaultIntervalMinutes));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return JobSourceResult.Failed(
                "Timeout",
                "DOU request exceeded its configured timeout.",
                now.AddMinutes(options.Value.DefaultIntervalMinutes));
        }
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        JobSourceRequest request,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var message = CreateRequest(request);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.RequestTimeoutSeconds));

            try
            {
                var response = await httpClient.SendAsync(
                    message,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token);
                if ((int)response.StatusCode < 500 || attempt >= RetryDelays.Length)
                {
                    return response;
                }

                response.Dispose();
            }
            catch (HttpRequestException) when (attempt < RetryDelays.Length)
            {
            }

            await Task.Delay(RetryDelays[attempt], timeProvider, cancellationToken);
        }
    }

    private static HttpRequestMessage CreateRequest(JobSourceRequest request)
    {
        var message = new HttpRequestMessage(HttpMethod.Get, request.Endpoint);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/rss+xml"));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
        message.Headers.UserAgent.ParseAdd("JobHunter/1.0");

        if (!string.IsNullOrWhiteSpace(request.Cursor?.EntityTag)
            && EntityTagHeaderValue.TryParse(request.Cursor.EntityTag, out var entityTag))
        {
            message.Headers.IfNoneMatch.Add(entityTag);
        }

        if (request.Cursor?.LastModifiedAtUtc is not null)
        {
            message.Headers.IfModifiedSince = request.Cursor.LastModifiedAtUtc;
        }

        return message;
    }

    private async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        var maximumBytes = options.Value.MaximumResponseBytes;
        if (content.Headers.ContentLength > maximumBytes)
        {
            throw new InvalidDataException(
                $"DOU response exceeds the configured {maximumBytes}-byte limit.");
        }

        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream();
        var buffer = new byte[8192];
        var total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > maximumBytes)
            {
                throw new InvalidDataException(
                    $"DOU response exceeds the configured {maximumBytes}-byte limit.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return destination.ToArray();
    }

    private DateTimeOffset GetNextPollAt(
        HttpResponseMessage response,
        DateTimeOffset now)
    {
        var configuredInterval = TimeSpan.FromMinutes(options.Value.DefaultIntervalMinutes);
        var cacheInterval = response.Headers.CacheControl?.MaxAge;
        var interval = cacheInterval is not null && cacheInterval.Value > configuredInterval
            ? cacheInterval.Value
            : configuredInterval;
        return now.Add(interval);
    }

    private static SourceCursorValue CreateCursor(
        HttpResponseMessage response,
        SourceCursorValue? previous) =>
        new(
            response.Headers.ETag?.ToString() ?? previous?.EntityTag,
            response.Content.Headers.LastModified ?? previous?.LastModifiedAtUtc,
            previous?.OpaqueValue);

    private static DateTimeOffset GetRetryAfter(
        HttpResponseMessage response,
        DateTimeOffset now)
    {
        if (response.Headers.RetryAfter?.Date is not null)
        {
            return response.Headers.RetryAfter.Date.Value;
        }

        return now.Add(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromHours(1));
    }

    private static bool IsSupportedContentType(MediaTypeHeaderValue? contentType) =>
        contentType?.MediaType is not null
        && (contentType.MediaType.Equals("application/rss+xml", StringComparison.OrdinalIgnoreCase)
            || contentType.MediaType.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
            || contentType.MediaType.Equals("text/xml", StringComparison.OrdinalIgnoreCase));

    private static void ValidateEndpoint(Uri endpoint)
    {
        if (!endpoint.IsAbsoluteUri
            || endpoint.Scheme != Uri.UriSchemeHttps
            || !string.Equals(endpoint.IdnHost, "jobs.dou.ua", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(endpoint.UserInfo))
        {
            throw new ArgumentException(
                "A DOU endpoint must be an HTTPS URL on jobs.dou.ua.",
                nameof(endpoint));
        }
    }

    private static string Bound(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}
