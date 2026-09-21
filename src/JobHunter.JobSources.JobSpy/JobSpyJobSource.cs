using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobHunter.Application.Sources;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Sources;
using JobHunter.JobSources.JobSpy.Configuration;
using JobHunter.JobSources.JobSpy.Contracts;
using Microsoft.Extensions.Options;

namespace JobHunter.JobSources.JobSpy;

public sealed class JobSpyJobSource(
    HttpClient httpClient,
    TimeProvider timeProvider,
    IOptions<JobSpyOptions> options,
    IOptions<JobSearchOptions> searchOptions)
    : IJobSource, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Skip,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true
    };

    public SourceName Name => SourceName.LinkedInJobSpy;

    public async Task<JobSourceResult> FetchAsync(
        JobSourceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!options.Value.Enabled)
        {
            throw new InvalidOperationException(
                "The JobSpy source cannot run because it is disabled.");
        }

        if (!options.Value.ExperimentalAcknowledged)
        {
            throw new InvalidOperationException(
                "The JobSpy source requires explicit experimental acknowledgement.");
        }

        var endpoint = ResolveSearchEndpoint(request.Endpoint);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.MaximumItems, 1);
        var now = timeProvider.GetUtcNow();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.RequestTimeoutSeconds));

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                endpoint,
                new JobSpySearchRequest
                {
                    Source = "linkedin",
                    SearchTerm = RequireQuery(request.QueryId),
                    Location = NormalizeOptionalSearchText(options.Value.Location),
                    ResultsWanted = Math.Min(
                        request.MaximumItems,
                        options.Value.MaximumResults),
                    HoursOld = searchOptions.Value.LookbackHours
                },
                JsonOptions,
                timeout.Token);

            if (response.StatusCode is HttpStatusCode.Forbidden
                or HttpStatusCode.TooManyRequests
                || IsSignInOrChallengeRedirect(response))
            {
                return JobSourceResult.Blocked(
                    $"Http{(int)response.StatusCode}",
                    "JobSpy/LinkedIn reported an access-control response. The source is blocked and no bypass will be attempted.",
                    GetBlockedRetryAt(response, now));
            }

            if (!response.IsSuccessStatusCode)
            {
                return JobSourceResult.Failed(
                    $"Http{(int)response.StatusCode}",
                    $"JobSpy returned HTTP {(int)response.StatusCode}.",
                    now.AddMinutes(options.Value.MinimumIntervalMinutes));
            }

            JobSpySearchResponse contract;
            try
            {
                var payload = await ReadBoundedAsync(response.Content, timeout.Token);
                contract = JsonSerializer.Deserialize<JobSpySearchResponse>(
                    payload,
                    JsonOptions)
                    ?? throw new JsonException("The JobSpy response body is empty.");
                ValidateContract(contract, options.Value.MaximumResults);
            }
            catch (JsonException)
            {
                return JobSourceResult.Failed(
                    "InvalidContract",
                    "JobSpy response contract is invalid.",
                    now.AddMinutes(options.Value.MinimumIntervalMinutes));
            }
            catch (InvalidDataException)
            {
                return JobSourceResult.Failed(
                    "ResponseLimitExceeded",
                    "JobSpy response exceeded the configured size limit.",
                    now.AddMinutes(options.Value.MinimumIntervalMinutes));
            }

            var errorCode = GetErrorCode(contract.Error);
            var retryAfterUtc = contract.Error?.RetryAfterSeconds is > 0
                ? now.AddSeconds(contract.Error.RetryAfterSeconds.Value)
                : (DateTimeOffset?)null;
            return contract.Status.ToLowerInvariant() switch
            {
                "succeeded" => new JobSourceResult(
                    SourceRunStatus.Succeeded,
                    MapJobs(contract.Jobs, now),
                    null,
                    false,
                    now.AddMinutes(options.Value.MinimumIntervalMinutes),
                    null,
                    null,
                    null),
                "partial" => new JobSourceResult(
                    SourceRunStatus.Partial,
                    MapJobs(contract.Jobs, now),
                    null,
                    false,
                    now.AddMinutes(options.Value.MinimumIntervalMinutes),
                    retryAfterUtc,
                    errorCode ?? "PartialResult",
                    CreateEnvelopeDiagnostic("partial", errorCode)),
                "blocked" => JobSourceResult.Blocked(
                    errorCode ?? "Blocked",
                    CreateEnvelopeDiagnostic("blocked", errorCode),
                    retryAfterUtc ?? now.AddHours(options.Value.BlockedBackoffHours)),
                "failed" => JobSourceResult.Failed(
                    errorCode ?? "JobSpyFailed",
                    CreateEnvelopeDiagnostic("failed", errorCode),
                    retryAfterUtc ?? now.AddMinutes(options.Value.MinimumIntervalMinutes)),
                _ => JobSourceResult.Failed(
                    "InvalidContract",
                    "JobSpy returned an unknown status value.",
                    now.AddMinutes(options.Value.MinimumIntervalMinutes))
            };
        }
        catch (HttpRequestException)
        {
            return JobSourceResult.Failed(
                "TransportFailure",
                "JobSpy request could not be completed.",
                now.AddMinutes(options.Value.MinimumIntervalMinutes));
        }
        catch (JsonException)
        {
            return JobSourceResult.Failed(
                "InvalidContract",
                "JobSpy response contract is invalid.",
                now.AddMinutes(options.Value.MinimumIntervalMinutes));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return JobSourceResult.Failed(
                "Timeout",
                "JobSpy request exceeded its configured timeout.",
                now.AddMinutes(options.Value.MinimumIntervalMinutes));
        }
    }

    public void Dispose() => httpClient.Dispose();

    private static void ValidateContract(
        JobSpySearchResponse response,
        int maximumResults)
    {
        if (string.IsNullOrWhiteSpace(response.Status))
        {
            throw new JsonException("Required field 'status' is blank.");
        }

        if (response.Status.Length > 32)
        {
            throw new JsonException("Required field 'status' exceeds the maximum length.");
        }

        if (response.Jobs.Count > maximumResults)
        {
            throw new JsonException("The JobSpy response contains too many jobs.");
        }

        for (var index = 0; index < response.Jobs.Count; index++)
        {
            var job = response.Jobs[index];
            if (!string.Equals(job.Source, "linkedin", StringComparison.OrdinalIgnoreCase))
            {
                throw new JsonException($"jobs[{index}].source must be 'linkedin'.");
            }

            if (string.IsNullOrWhiteSpace(job.SourceJobId)
                || string.IsNullOrWhiteSpace(job.Title)
                || string.IsNullOrWhiteSpace(job.Company)
                || string.IsNullOrWhiteSpace(job.Description)
                || !Uri.TryCreate(job.SourceUrl, UriKind.Absolute, out var sourceUri)
                || sourceUri.Scheme != Uri.UriSchemeHttps
                || !IsLinkedInHost(sourceUri.IdnHost)
                || !string.IsNullOrEmpty(sourceUri.UserInfo))
            {
                throw new JsonException($"jobs[{index}] is missing a required valid field.");
            }
        }

        if (response.Status.Equals("blocked", StringComparison.OrdinalIgnoreCase)
            || response.Status.Equals("failed", StringComparison.OrdinalIgnoreCase))
        {
            if (response.Error is null)
            {
                throw new JsonException($"Status '{response.Status}' requires an error object.");
            }
        }

        if (response.Error is not null && !IsSafeErrorCode(response.Error.Code))
        {
            throw new JsonException("error.code has an unsupported format.");
        }
    }

    private static JobSourceRecord[] MapJobs(
        IReadOnlyCollection<JobSpyJob> jobs,
        DateTimeOffset retrievedAtUtc) =>
        jobs.Select(job => MapJob(job, retrievedAtUtc)).ToArray();

    private static JobSourceRecord MapJob(
        JobSpyJob job,
        DateTimeOffset retrievedAtUtc)
    {
        var sourceUrl = new Uri(job.SourceUrl, UriKind.Absolute);
        var canonicalUrl = CanonicalJobUrl.Create(job.SourceUrl);
        var applicationUrl = ParseOptionalHttpUri(job.ApplicationUrl);
        var description = Bound(job.Description, 100_000);
        var rawPayload = JsonSerializer.Serialize(job, JsonOptions);
        var normalizedPayload = JsonSerializer.Serialize(
            job with
            {
                SourceUrl = canonicalUrl.ToString(),
                ApplicationUrl = applicationUrl is null
                    ? null
                    : CanonicalJobUrl.Create(applicationUrl.AbsoluteUri).ToString()
            },
            JsonOptions);
        return new JobSourceRecord
        {
            Source = SourceName.LinkedInJobSpy,
            SourceJobId = NativeSourceId.Create(job.SourceJobId),
            SourceUrl = sourceUrl,
            CanonicalUrl = canonicalUrl,
            SourceGuid = job.SourceJobId,
            Title = Bound(job.Title.Trim(), 512),
            Company = Bound(job.Company.Trim(), 512),
            DescriptionHtml = WebUtility.HtmlEncode(description),
            DescriptionText = CollapseWhitespace(description),
            Locations = string.IsNullOrWhiteSpace(job.Location) ? [] : [job.Location.Trim()],
            WorkplaceMode = ParseWorkplaceMode(job.WorkplaceMode),
            EmploymentType = ParseEmploymentType(job.EmploymentType),
            Seniority = BoundNullable(job.Seniority, 128),
            Skills = job.Skills?.Select(value => Bound(value.Trim(), 128)).ToArray() ?? [],
            Categories = job.Categories?.Select(value => Bound(value.Trim(), 128)).ToArray() ?? [],
            CompensationMinimum = job.CompensationMinimum,
            CompensationMaximum = job.CompensationMaximum,
            CompensationCurrency = BoundNullable(job.CompensationCurrency?.ToUpperInvariant(), 3),
            CompensationPeriod = ParseCompensationPeriod(job.CompensationPeriod),
            PublishedAtUtc = job.PublishedAtUtc?.ToUniversalTime(),
            PublishedAtPrecision = job.PublishedAtUtc is null
                ? PublishedAtPrecision.Unknown
                : PublishedAtPrecision.DateTime,
            ApplicationUrl = applicationUrl,
            ParserVersion = "jobspy-contract-v1",
            ContentHash = Hash(normalizedPayload),
            RawPayloadHash = Hash(rawPayload),
            RetrievedAtUtc = retrievedAtUtc
        };
    }

    private static Uri ResolveSearchEndpoint(Uri configuredEndpoint)
    {
        if (!configuredEndpoint.IsAbsoluteUri
            || !configuredEndpoint.IsLoopback
            || configuredEndpoint.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(configuredEndpoint.UserInfo))
        {
            throw new ArgumentException(
                "A JobSpy endpoint must be an absolute loopback HTTP or HTTPS URL.",
                nameof(configuredEndpoint));
        }

        return configuredEndpoint.AbsolutePath == "/"
            ? new Uri(configuredEndpoint, "v1/search")
            : configuredEndpoint;
    }

    private static string RequireQuery(string? query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var trimmed = query.Trim();
        if (trimmed.Length > 256)
        {
            throw new ArgumentException(
                "A JobSpy search term must contain at most 256 characters.",
                nameof(query));
        }

        return trimmed;
    }

    private static string? NormalizeOptionalSearchText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        var maximumBytes = options.Value.MaximumResponseBytes;
        if (content.Headers.ContentLength > maximumBytes)
        {
            throw new InvalidDataException(
                $"JobSpy response exceeds the configured {maximumBytes}-byte limit.");
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
                    $"JobSpy response exceeds the configured {maximumBytes}-byte limit.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return destination.ToArray();
    }

    private DateTimeOffset GetBlockedRetryAt(
        HttpResponseMessage response,
        DateTimeOffset now)
    {
        if (response.Headers.RetryAfter?.Date is not null)
        {
            return response.Headers.RetryAfter.Date.Value;
        }

        return now.Add(
            response.Headers.RetryAfter?.Delta
                ?? TimeSpan.FromHours(options.Value.BlockedBackoffHours));
    }

    private static bool IsSignInOrChallengeRedirect(HttpResponseMessage response)
    {
        if ((int)response.StatusCode is < 300 or >= 400)
        {
            return false;
        }

        var location = response.Headers.Location?.ToString() ?? string.Empty;
        return location.Contains("login", StringComparison.OrdinalIgnoreCase)
            || location.Contains("signin", StringComparison.OrdinalIgnoreCase)
            || location.Contains("challenge", StringComparison.OrdinalIgnoreCase);
    }

    private static WorkplaceMode ParseWorkplaceMode(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "remote" => WorkplaceMode.Remote,
            "hybrid" => WorkplaceMode.Hybrid,
            "on_site" or "onsite" => WorkplaceMode.OnSite,
            _ => WorkplaceMode.Unknown
        };

    private static EmploymentType ParseEmploymentType(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "full_time" or "fulltime" => EmploymentType.FullTime,
            "part_time" or "parttime" => EmploymentType.PartTime,
            "contract" => EmploymentType.Contract,
            "temporary" => EmploymentType.Temporary,
            "internship" => EmploymentType.Internship,
            _ => EmploymentType.Unknown
        };

    private static CompensationPeriod ParseCompensationPeriod(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "hour" or "hourly" => CompensationPeriod.Hour,
            "month" or "monthly" => CompensationPeriod.Month,
            "year" or "yearly" => CompensationPeriod.Year,
            _ => CompensationPeriod.Unknown
        };

    private static Uri? ParseOptionalHttpUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new JsonException("A JobSpy application URL is invalid.");
        }

        return uri;
    }

    private static bool IsLinkedInHost(string host) =>
        host.Equals("linkedin.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".linkedin.com", StringComparison.OrdinalIgnoreCase);

    private static string? GetErrorCode(JobSpyError? error) =>
        error is null ? null : error.Code;

    private static string CreateEnvelopeDiagnostic(string status, string? errorCode) =>
        errorCode is null
            ? $"JobSpy reported a {status} result."
            : $"JobSpy reported a {status} result (code: {errorCode}).";

    private static bool IsSafeErrorCode(string value)
    {
        if (value.Length is 0 or > 64)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!(character is >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    private static string CollapseWhitespace(string value) =>
        string.Join(
            ' ',
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Hash(string value) =>
        $"sha256:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))}";

    private static string Bound(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    private static string? BoundNullable(string? value, int maximumLength) =>
        value is null ? null : Bound(value.Trim(), maximumLength);
}
