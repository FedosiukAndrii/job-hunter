using System.Net.Http.Json;
using System.Text.Json;
using JobHunter.JobSources.JobSpy.Configuration;
using JobHunter.JobSources.JobSpy.Contracts;
using Microsoft.Extensions.Options;

namespace JobHunter.JobSources.JobSpy;

public interface IJobSpyStatusProbe
{
    Task<JobSpyReadinessResult> CheckAsync(CancellationToken cancellationToken);
}

public enum JobSpyReadiness
{
    Disabled = 0,
    Ready = 1,
    Unavailable = 2,
    InvalidContract = 3
}

public sealed record JobSpyReadinessResult(
    JobSpyReadiness Status,
    string Diagnostic,
    string? ServiceVersion,
    string? ContractVersion);

public sealed class JobSpyStatusProbe(
    HttpClient httpClient,
    IOptions<JobSpyOptions> options)
    : IJobSpyStatusProbe, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Skip,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true
    };

    public async Task<JobSpyReadinessResult> CheckAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return new JobSpyReadinessResult(
                JobSpyReadiness.Disabled,
                "The JobSpy source is disabled.",
                null,
                null);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.RequestTimeoutSeconds));

        try
        {
            using var healthResponse = await httpClient.GetAsync("health", timeout.Token);
            if (!healthResponse.IsSuccessStatusCode)
            {
                return Unavailable($"Health endpoint returned HTTP {(int)healthResponse.StatusCode}.");
            }

            var health = await healthResponse.Content.ReadFromJsonAsync<JobSpyHealthResponse>(
                JsonOptions,
                timeout.Token);
            if (!string.Equals(health?.Status, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return InvalidContract("Health endpoint did not return status 'ok'.");
            }

            using var versionResponse = await httpClient.GetAsync("version", timeout.Token);
            if (!versionResponse.IsSuccessStatusCode)
            {
                return Unavailable($"Version endpoint returned HTTP {(int)versionResponse.StatusCode}.");
            }

            var version = await versionResponse.Content.ReadFromJsonAsync<JobSpyVersionResponse>(
                JsonOptions,
                timeout.Token);
            return version is not null
                && version.ContractVersion == "v1"
                && !string.IsNullOrWhiteSpace(version.ServiceVersion)
                    ? new JobSpyReadinessResult(
                        JobSpyReadiness.Ready,
                        "The JobSpy service and v1 contract are ready.",
                        version.ServiceVersion,
                        version.ContractVersion)
                    : InvalidContract("Version endpoint returned an incompatible contract.");
        }
        catch (JsonException)
        {
            return InvalidContract("JobSpy returned malformed health or version JSON.");
        }
        catch (HttpRequestException)
        {
            return Unavailable("JobSpy is unavailable.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable("JobSpy readiness check timed out.");
        }
    }

    public void Dispose() => httpClient.Dispose();

    private static JobSpyReadinessResult Unavailable(string diagnostic) =>
        new(JobSpyReadiness.Unavailable, diagnostic, null, null);

    private static JobSpyReadinessResult InvalidContract(string diagnostic) =>
        new(JobSpyReadiness.InvalidContract, diagnostic, null, null);
}
