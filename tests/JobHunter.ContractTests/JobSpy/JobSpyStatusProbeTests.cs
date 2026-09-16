using System.Net;
using System.Text;
using JobHunter.JobSources.JobSpy;
using JobHunter.JobSources.JobSpy.Configuration;
using Microsoft.Extensions.Options;

namespace JobHunter.ContractTests.JobSpy;

public sealed class JobSpyStatusProbeTests
{
    [Fact]
    public async Task CheckAcceptsHealthyCompatibleService()
    {
        var responses = new Queue<HttpResponseMessage>(
            [
                JsonResponse("""{"status":"ok"}"""),
                JsonResponse(
                    """
                    {
                      "serviceVersion": "0.1.0",
                      "contractVersion": "v1"
                    }
                    """)
            ]);
        using var httpClient = new HttpClient(
            new StubHttpMessageHandler(
                (_, _) => Task.FromResult(responses.Dequeue())))
        {
            BaseAddress = new Uri("http://127.0.0.1:8080/")
        };
        using var probe = CreateProbe(httpClient);

        var result = await probe.CheckAsync(CancellationToken.None);

        Assert.Equal(JobSpyReadiness.Ready, result.Status);
        Assert.Equal("0.1.0", result.ServiceVersion);
        Assert.Equal("v1", result.ContractVersion);
        Assert.Empty(responses);
    }

    [Fact]
    public async Task CheckReportsUnavailableTransport()
    {
        using var httpClient = new HttpClient(
            new StubHttpMessageHandler(
                (_, _) => throw new HttpRequestException("Fixture failure.")))
        {
            BaseAddress = new Uri("http://127.0.0.1:8080/")
        };
        using var probe = CreateProbe(httpClient);

        var result = await probe.CheckAsync(CancellationToken.None);

        Assert.Equal(JobSpyReadiness.Unavailable, result.Status);
        Assert.Equal("JobSpy is unavailable.", result.Diagnostic);
    }

    private static JobSpyStatusProbe CreateProbe(HttpClient httpClient) =>
        new(
            httpClient,
            Options.Create(
                new JobSpyOptions
                {
                    Enabled = true,
                    RequestTimeoutSeconds = 30
                }));

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
