using JobHunter.Application.Sources;
using JobHunter.JobSources.JobSpy.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JobHunter.JobSources.JobSpy.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddJobSpySource(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<JobSpyOptions>()
            .Bind(configuration.GetSection(JobSpyOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<JobSpyOptions>, JobSpyOptionsValidator>();
        services.AddHttpClient<JobSpyJobSource>(ConfigureClient)
            .ConfigurePrimaryHttpMessageHandler(
                () => new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    ConnectTimeout = TimeSpan.FromSeconds(10)
                });
        services.AddHttpClient<IJobSpyStatusProbe, JobSpyStatusProbe>(ConfigureClient)
            .ConfigurePrimaryHttpMessageHandler(
                () => new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    ConnectTimeout = TimeSpan.FromSeconds(10)
                });

        if (configuration.GetValue<bool>($"{JobSpyOptions.SectionName}:Enabled"))
        {
            services.AddSingleton<IJobSource>(
                serviceProvider => serviceProvider.GetRequiredService<JobSpyJobSource>());
        }

        return services;
    }

    private static void ConfigureClient(
        IServiceProvider serviceProvider,
        HttpClient client)
    {
        var jobSpyOptions = serviceProvider.GetRequiredService<IOptions<JobSpyOptions>>().Value;
        client.BaseAddress = new Uri(jobSpyOptions.Endpoint, UriKind.Absolute);
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("JobHunter/1.0");
    }
}
