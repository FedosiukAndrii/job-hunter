using System.Net;
using System.Net.Http.Headers;
using System.Text;
using JobHunter.Application.Sources;
using JobHunter.Domain.Sources;
using JobHunter.JobSources.Dou;
using JobHunter.JobSources.Dou.Configuration;
using JobHunter.JobSources.Dou.Parsing;
using JobHunter.JobSources.JobSpy;
using JobHunter.JobSources.JobSpy.Configuration;
using Microsoft.Extensions.Options;

namespace JobHunter.ContractTests.JobSpy;

public sealed class SourceIsolationTests
{
    [Fact]
    public async Task JobSpyRateLimitDoesNotPreventDouSuccess()
    {
        using var jobSpy = new JobSpyJobSource(
            new HttpClient(
                new StubHttpMessageHandler(
                    _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests))),
            TimeProvider.System,
            Options.Create(
                new JobSpyOptions
                {
                    Enabled = true,
                    ExperimentalAcknowledged = true
                }));

        var douResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(
                File.ReadAllBytes(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "Fixtures",
                        "Dou",
                        "standard-dotnet.xml")))
        };
        douResponse.Content.Headers.ContentType = new MediaTypeHeaderValue("application/rss+xml");
        using var dou = new DouJobSource(
            new HttpClient(new StubHttpMessageHandler(_ => douResponse)),
            new DouRssParser(),
            TimeProvider.System,
            Options.Create(new DouOptions()));

        var jobSpyTask = jobSpy.FetchAsync(
            new JobSourceRequest(
                Guid.NewGuid(),
                new Uri("http://127.0.0.1:8080/v1/search"),
                ".NET",
                null,
                20),
            CancellationToken.None);
        var douTask = dou.FetchAsync(
            new JobSourceRequest(
                Guid.NewGuid(),
                new Uri("https://jobs.dou.ua/vacancies/feeds/?category=.NET"),
                ".NET",
                null,
                200),
            CancellationToken.None);

        var results = await Task.WhenAll(jobSpyTask, douTask);

        Assert.Equal(SourceRunStatus.Blocked, results[0].Status);
        Assert.Equal(SourceRunStatus.Succeeded, results[1].Status);
        Assert.Single(results[1].Jobs);
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
