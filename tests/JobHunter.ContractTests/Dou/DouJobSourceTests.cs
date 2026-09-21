using System.Net;
using System.Net.Http.Headers;
using JobHunter.Application.Sources;
using JobHunter.Domain.Sources;
using JobHunter.JobSources.Dou;
using JobHunter.JobSources.Dou.Configuration;
using JobHunter.JobSources.Dou.Parsing;
using Microsoft.Extensions.Options;

namespace JobHunter.ContractTests.Dou;

public sealed class DouJobSourceTests
{
    [Fact]
    public async Task FetchAsyncUsesValidatorsAndHandlesNotModified()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(
            request =>
            {
                capturedRequest = request;
                return new HttpResponseMessage(HttpStatusCode.NotModified)
                {
                    Headers =
                    {
                        ETag = new EntityTagHeaderValue("\"new-etag\""),
                        CacheControl = new CacheControlHeaderValue
                        {
                            MaxAge = TimeSpan.FromMinutes(20)
                        }
                    }
                };
            });
        using var source = CreateSource(handler);
        var request = CreateRequest(
            new SourceCursorValue(
                "\"old-etag\"",
                DateTimeOffset.UnixEpoch,
                null));

        var result = await source.FetchAsync(request, CancellationToken.None);

        Assert.Equal(SourceRunStatus.Succeeded, result.Status);
        Assert.True(result.NotModified);
        Assert.Equal("\"old-etag\"", capturedRequest?.Headers.IfNoneMatch.Single().Tag);
        Assert.NotNull(result.NextPollNotBeforeUtc);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "HttpForbidden")]
    [InlineData(HttpStatusCode.TooManyRequests, "HttpRateLimited")]
    public async Task FetchAsyncMapsRestrictedResponsesWithoutParsing(
        HttpStatusCode statusCode,
        string expectedCode)
    {
        var response = new HttpResponseMessage(statusCode);
        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(30));
        }

        using var source = CreateSource(new StubHttpMessageHandler(_ => response));

        var result = await source.FetchAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(SourceRunStatus.Failed, result.Status);
        Assert.Equal(expectedCode, result.ErrorCode);
        Assert.Empty(result.Jobs);
        Assert.NotNull(result.RetryAfterUtc);
    }

    [Fact]
    public async Task FetchAsyncRejectsUnsupportedContentType()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>challenge</html>")
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        using var source = CreateSource(new StubHttpMessageHandler(_ => response));

        var result = await source.FetchAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(SourceRunStatus.Failed, result.Status);
        Assert.Equal("UnsupportedContentType", result.ErrorCode);
        Assert.Empty(result.Jobs);
    }

    [Fact]
    public async Task FetchAsyncReturnsFailureForMalformedXml()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(
                File.ReadAllBytes(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "Fixtures",
                        "Dou",
                        "malformed.xml")))
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/rss+xml");
        using var source = CreateSource(new StubHttpMessageHandler(_ => response));

        var result = await source.FetchAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(SourceRunStatus.Failed, result.Status);
        Assert.Equal("MalformedXml", result.ErrorCode);
        Assert.Empty(result.Jobs);
    }

    [Fact]
    public async Task FetchAsyncDoesNotExposeTransportExceptionTextInDiagnostic()
    {
        const string marker = "private upstream exception detail";
        using var source = CreateSource(
            new StubHttpMessageHandler(
                _ => throw new HttpRequestException(marker)));

        var result = await source.FetchAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(SourceRunStatus.Failed, result.Status);
        Assert.Equal("TransportFailure", result.ErrorCode);
        Assert.Equal("DOU request could not be completed.", result.Diagnostic);
        Assert.DoesNotContain(marker, result.Diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FetchAsyncUsesBoundedRetryForServerErrors()
    {
        var attempts = 0;
        using var source = CreateSource(
            new StubHttpMessageHandler(
                _ =>
                {
                    attempts++;
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                }));

        var result = await source.FetchAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(SourceRunStatus.Failed, result.Status);
        Assert.Equal("Http503", result.ErrorCode);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task FetchAsyncReturnsTimeoutWithoutThrowing()
    {
        using var source = CreateSource(
            new AsyncStubHttpMessageHandler(
                async (_, cancellationToken) =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return new HttpResponseMessage(HttpStatusCode.OK);
                }),
            requestTimeoutSeconds: 1);

        var result = await source.FetchAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(SourceRunStatus.Failed, result.Status);
        Assert.Equal("Timeout", result.ErrorCode);
        Assert.Empty(result.Jobs);
    }

    [Fact]
    public async Task FetchAsyncRejectsResponseAboveConfiguredLimit()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new string('x', 20_000))
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/rss+xml");
        using var source = CreateSource(
            new StubHttpMessageHandler(_ => response),
            maximumResponseBytes: 16 * 1024);

        var result = await source.FetchAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal("ResponseLimitExceeded", result.ErrorCode);
        Assert.Empty(result.Jobs);
    }

    [Fact]
    public async Task FetchAsyncExcludesVacanciesOutsideConfiguredSearchWindow()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(
                File.ReadAllBytes(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "Fixtures",
                        "Dou",
                        "standard-dotnet.xml")))
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/rss+xml");
        using var source = CreateSource(
            new StubHttpMessageHandler(_ => response),
            lookbackHours: 24,
            timeProvider: new FixedTimeProvider(
                new DateTimeOffset(2026, 9, 16, 10, 0, 0, TimeSpan.Zero)));

        var result = await source.FetchAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(SourceRunStatus.Succeeded, result.Status);
        Assert.Empty(result.Jobs);
    }

    private static DouJobSource CreateSource(
        HttpMessageHandler handler,
        int requestTimeoutSeconds = 5,
        int maximumResponseBytes = 2 * 1024 * 1024,
        int lookbackHours = 24,
        TimeProvider? timeProvider = null)
    {
        var options = Options.Create(
            new DouOptions
            {
                DefaultIntervalMinutes = 12,
                MaximumResponseBytes = maximumResponseBytes,
                MaximumItems = 200,
                MaximumDescriptionCharacters = 50_000,
                RequestTimeoutSeconds = requestTimeoutSeconds
            });
        return new DouJobSource(
            new HttpClient(handler),
            new DouRssParser(),
            timeProvider ?? TimeProvider.System,
            options,
            Options.Create(
                new JobSearchOptions
                {
                    LookbackHours = lookbackHours
                }));
    }

    private static JobSourceRequest CreateRequest(SourceCursorValue? cursor = null) =>
        new(
            Guid.NewGuid(),
            new Uri("https://jobs.dou.ua/vacancies/feeds/?category=.NET"),
            ".NET",
            cursor,
            200);

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }

    private sealed class AsyncStubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            responseFactory(request, cancellationToken);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
