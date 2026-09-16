using JobHunter.JobSources.Dou.Configuration;
using JobHunter.JobSources.Dou.Parsing;
using Microsoft.Extensions.Options;

namespace JobHunter.JobSources.Dou;

public sealed class DouDetailPageEnricher(
    HttpClient httpClient,
    TimeProvider timeProvider,
    IOptions<DouOptions> options)
    : IDouDetailPageEnricher, IDisposable
{
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private DateTimeOffset _nextRequestAtUtc = DateTimeOffset.MinValue;

    public async Task<SanitizedHtml?> FetchAsync(
        Uri detailPage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(detailPage);
        if (!options.Value.DetailEnrichmentEnabled)
        {
            return null;
        }

        ValidateEndpoint(detailPage);
        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            var now = timeProvider.GetUtcNow();
            if (_nextRequestAtUtc > now)
            {
                await Task.Delay(_nextRequestAtUtc - now, timeProvider, cancellationToken);
            }

            _nextRequestAtUtc = timeProvider
                .GetUtcNow()
                .AddSeconds(options.Value.DetailMinimumIntervalSeconds);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.RequestTimeoutSeconds));
            using var response = await httpClient.GetAsync(
                detailPage,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            response.EnsureSuccessStatusCode();

            var html = await ReadBoundedAsync(response.Content, timeout.Token);
            return SafeHtmlContent.Sanitize(
                html,
                options.Value.MaximumDescriptionCharacters);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    public void Dispose()
    {
        _requestGate.Dispose();
    }

    private async Task<string> ReadBoundedAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        var maximumBytes = options.Value.MaximumResponseBytes;
        if (content.Headers.ContentLength > maximumBytes)
        {
            throw new InvalidDataException(
                $"DOU detail response exceeds the configured {maximumBytes}-byte limit.");
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream();
        var buffer = new byte[8192];
        var total = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > maximumBytes)
            {
                throw new InvalidDataException(
                    $"DOU detail response exceeds the configured {maximumBytes}-byte limit.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return System.Text.Encoding.UTF8.GetString(destination.GetBuffer(), 0, total);
    }

    private static void ValidateEndpoint(Uri endpoint)
    {
        if (!endpoint.IsAbsoluteUri
            || endpoint.Scheme != Uri.UriSchemeHttps
            || !string.Equals(endpoint.IdnHost, "jobs.dou.ua", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(endpoint.UserInfo))
        {
            throw new ArgumentException(
                "A DOU detail endpoint must be an HTTPS URL on jobs.dou.ua.",
                nameof(endpoint));
        }
    }
}
