using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JobHunter.Application.Sources;
using JobHunter.Domain.Sources;
using JobHunter.JobSources.JobSpy;
using JobHunter.JobSources.JobSpy.Configuration;
using Microsoft.Extensions.Options;

namespace JobHunter.ContractTests.JobSpy;

public sealed class JobSpyJobSourceTests
{
    [Fact]
    public async Task FetchAsyncToleratesUnknownFieldsAndMapsValidContract()
    {
        const string json =
            """
            {
              "status": "succeeded",
              "jobs": [{
                "source": "linkedin",
                "sourceJobId": "li-123",
                "sourceUrl": "https://www.linkedin.com/jobs/view/123?trk=public_jobs&utm_source=test",
                "title": "Senior .NET Engineer",
                "company": "Example",
                "description": "Remote C# role",
                "workplaceMode": "remote",
                "employmentType": "full_time",
                "skills": [".NET", "C#"],
                "futureField": "ignored"
              }],
              "providerVersion": "1.2.3",
              "futureEnvelopeField": true
            }
            """;
        using var source = CreateSource(
            new StubHttpMessageHandler(
                _ => JsonResponse(HttpStatusCode.OK, json)));

        var result = await source.FetchAsync(CreateRequest(), CancellationToken.None);

        var job = Assert.Single(result.Jobs);
        Assert.Equal(SourceRunStatus.Succeeded, result.Status);
        Assert.Equal("li-123", job.SourceJobId?.Value);
        Assert.Equal("https://www.linkedin.com/jobs/view/123", job.CanonicalUrl.ToString());
    }

    [Fact]
    public async Task FetchAsyncForwardsConfiguredSearchBoundsToSidecar()
    {
        string? requestBody = null;
        using var source = CreateSource(
            new StubHttpMessageHandler(
                request =>
                {
                    requestBody = request.Content!
                        .ReadAsStringAsync()
                        .GetAwaiter()
                        .GetResult();
                    return JsonResponse(
                        HttpStatusCode.OK,
                        "{\"status\":\"succeeded\",\"jobs\":[]}");
                }),
            location: " Ukraine ",
            lookbackHours: 48);

        var result = await source.FetchAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(SourceRunStatus.Succeeded, result.Status);
        using var document = JsonDocument.Parse(requestBody!);
        var payload = document.RootElement;
        Assert.Equal("linkedin", payload.GetProperty("source").GetString());
        Assert.Equal(".NET", payload.GetProperty("searchTerm").GetString());
        Assert.Equal("Ukraine", payload.GetProperty("location").GetString());
        Assert.Equal(20, payload.GetProperty("resultsWanted").GetInt32());
        Assert.Equal(48, payload.GetProperty("hoursOld").GetInt32());
    }

    [Fact]
    public async Task FetchAsyncIgnoresTrackingParametersInContentHash()
    {
        const string firstJson =
            """
            {
              "status": "succeeded",
              "jobs": [{
                "source": "linkedin",
                "sourceJobId": "li-123",
                "sourceUrl": "https://www.linkedin.com/jobs/view/123?trk=first&utm_source=test",
                "title": "Senior .NET Engineer",
                "company": "Example",
                "description": "Remote C# role"
              }]
            }
            """;
        const string secondJson =
            """
            {
              "status": "succeeded",
              "jobs": [{
                "source": "linkedin",
                "sourceJobId": "li-123",
                "sourceUrl": "https://www.linkedin.com/jobs/view/123?trk=second&utm_campaign=changed",
                "title": "Senior .NET Engineer",
                "company": "Example",
                "description": "Remote C# role"
              }]
            }
            """;
        using var firstSource = CreateSource(
            new StubHttpMessageHandler(
                _ => JsonResponse(HttpStatusCode.OK, firstJson)));
        using var secondSource = CreateSource(
            new StubHttpMessageHandler(
                _ => JsonResponse(HttpStatusCode.OK, secondJson)));

        var firstJob = Assert.Single(
            (await firstSource.FetchAsync(CreateRequest(), CancellationToken.None)).Jobs);
        var secondJob = Assert.Single(
            (await secondSource.FetchAsync(CreateRequest(), CancellationToken.None)).Jobs);

        Assert.Equal(firstJob.ContentHash, secondJob.ContentHash);
        Assert.NotEqual(firstJob.RawPayloadHash, secondJob.RawPayloadHash);
    }

    [Fact]
    public async Task FetchAsyncRejectsMissingRequiredContractField()
    {
        const string json = """{"status":"succeeded","futureField":true}""";
        using var source = CreateSource(
            new StubHttpMessageHandler(
                _ => JsonResponse(HttpStatusCode.OK, json)));

        var result = await source.FetchAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(SourceRunStatus.Failed, result.Status);
        Assert.Equal("InvalidContract", result.ErrorCode);
        Assert.Empty(result.Jobs);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task FetchAsyncMapsRestrictedHttpResponseToBlocked(HttpStatusCode statusCode)
    {
        var response = new HttpResponseMessage(statusCode);
        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(2));
        }

        using var source = CreateSource(new StubHttpMessageHandler(_ => response));

        var result = await source.FetchAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(SourceRunStatus.Blocked, result.Status);
        Assert.Empty(result.Jobs);
        Assert.NotNull(result.RetryAfterUtc);
    }

    [Fact]
    public async Task FetchAsyncMapsSignInRedirectToBlocked()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri("https://www.linkedin.com/login");
        using var source = CreateSource(new StubHttpMessageHandler(_ => response));

        var result = await source.FetchAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(SourceRunStatus.Blocked, result.Status);
    }

    [Fact]
    public async Task FetchAsyncMapsBlockedEnvelopeToBlocked()
    {
        const string json =
            """
            {
              "status": "blocked",
              "jobs": [],
              "error": {
                "code": "linkedin_challenge",
                "message": "LinkedIn returned a challenge.",
                "retryAfterSeconds": 86400,
                "action": "Review the source before manually enabling it."
              }
            }
            """;
        using var source = CreateSource(
            new StubHttpMessageHandler(
                _ => JsonResponse(HttpStatusCode.OK, json)));

        var result = await source.FetchAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(SourceRunStatus.Blocked, result.Status);
        Assert.Equal("linkedin_challenge", result.ErrorCode);
        Assert.NotNull(result.RetryAfterUtc);
    }

    [Fact]
    public async Task FetchAsyncDoesNotExposeUntrustedEnvelopeMessageInDiagnostic()
    {
        const string marker = "private vacancy description must not be logged";
        var json =
            $$"""
            {
              "status": "failed",
              "jobs": [],
              "error": {
                "code": "parser_incompatible",
                "message": "{{marker}}"
              }
            }
            """;
        using var source = CreateSource(
            new StubHttpMessageHandler(
                _ => JsonResponse(HttpStatusCode.OK, json)));

        var result = await source.FetchAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(SourceRunStatus.Failed, result.Status);
        Assert.Equal("parser_incompatible", result.ErrorCode);
        Assert.Equal(
            "JobSpy reported a failed result (code: parser_incompatible).",
            result.Diagnostic);
        Assert.DoesNotContain(marker, result.Diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FetchAsyncRejectsUntrustedEnvelopeErrorCode()
    {
        const string marker = "<untrusted-source-content>";
        var json =
            $$"""
            {
              "status": "blocked",
              "jobs": [],
              "error": {
                "code": "{{marker}}",
                "message": "ignored"
              }
            }
            """;
        using var source = CreateSource(
            new StubHttpMessageHandler(
                _ => JsonResponse(HttpStatusCode.OK, json)));

        var result = await source.FetchAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(SourceRunStatus.Failed, result.Status);
        Assert.Equal("InvalidContract", result.ErrorCode);
        Assert.DoesNotContain(marker, result.Diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FetchAsyncRejectsOversizedResponse()
    {
        var response = JsonResponse(
            HttpStatusCode.OK,
            $"{{\"status\":\"succeeded\",\"jobs\":[],\"padding\":\"{new string('x', 20_000)}\"}}");
        using var source = CreateSource(
            new StubHttpMessageHandler(_ => response),
            maximumResponseBytes: 16 * 1024);

        var result = await source.FetchAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(SourceRunStatus.Failed, result.Status);
        Assert.Equal("ResponseLimitExceeded", result.ErrorCode);
        Assert.Empty(result.Jobs);
    }

    private static JobSpyJobSource CreateSource(
        HttpMessageHandler handler,
        int maximumResponseBytes = 2 * 1024 * 1024,
        string? location = null,
        int lookbackHours = 24) =>
        new(
            new HttpClient(handler),
            TimeProvider.System,
            Options.Create(
                new JobSpyOptions
                {
                    Enabled = true,
                    ExperimentalAcknowledged = true,
                    Endpoint = "http://127.0.0.1:8080/",
                    Location = location,
                    MinimumIntervalMinutes = 60,
                    RequestTimeoutSeconds = 5,
                    MaximumResults = 50,
                    MaximumResponseBytes = maximumResponseBytes,
                    BlockedBackoffHours = 24
                }),
            Options.Create(
                new JobSearchOptions
                {
                    LookbackHours = lookbackHours
                }));

    private static JobSourceRequest CreateRequest() =>
        new(
            Guid.NewGuid(),
            new Uri("http://127.0.0.1:8080/v1/search"),
            ".NET",
            null,
            20);

    private static HttpResponseMessage JsonResponse(
        HttpStatusCode statusCode,
        string json)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return response;
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
